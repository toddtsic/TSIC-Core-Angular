using Microsoft.AspNetCore.Identity;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Constants;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Data.Identity;

namespace TSIC.API.Services.Admin;

/// <summary>
/// Service for managing administrator registrations within a job.
/// </summary>
public sealed class AdministratorService : IAdministratorService
{
    private readonly IAdministratorRepository _adminRepo;
    private readonly IUserRepository _userRepo;
    private readonly IJobRepository _jobRepo;
    private readonly UserManager<ApplicationUser> _userManager;

    /// <summary>
    /// AM-004 lane model: eligibility for granting a role is confined to that role's lane —
    /// an account qualifies only if every registration it has ever held (any job, any customer)
    /// lies within the lane. No cross-type grants (a Director cannot be handed Store Admin; a
    /// referee's account cannot become Ref Assignor — fresh admins come through the Unassigned
    /// Adult funnel instead). Director and SuperDirector share one lane: they are the same kind
    /// of person at two trust levels, and mixed D/SD accounts exist by design (the Edit modal
    /// flips between them). Every other admin type is strictly its own lane.
    /// </summary>
    private static string[] GetRoleLane(string roleId)
    {
        return roleId == RoleConstants.Director || roleId == RoleConstants.SuperDirector
            ? [RoleConstants.Director, RoleConstants.SuperDirector]
            : [roleId];
    }

    /// <summary>
    /// Maps display role names to role ID constants.
    /// </summary>
    private static readonly Dictionary<string, string> RoleNameToIdMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Director"] = RoleConstants.Director,
        ["SuperDirector"] = RoleConstants.SuperDirector,
        ["Superuser"] = RoleConstants.Superuser,
        ["ApiAuthorized"] = RoleConstants.ApiAuthorized,
        ["Ref Assignor"] = RoleConstants.RefAssignor,
        ["Store Admin"] = RoleConstants.StoreAdmin,
        ["STPAdmin"] = RoleConstants.StpAdmin
    };

    public AdministratorService(
        IAdministratorRepository adminRepo,
        IUserRepository userRepo,
        IJobRepository jobRepo,
        UserManager<ApplicationUser> userManager)
    {
        _adminRepo = adminRepo;
        _userRepo = userRepo;
        _jobRepo = jobRepo;
        _userManager = userManager;
    }

    public async Task<List<AdministratorDto>> GetAdministratorsAsync(
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        await EnsurePrimaryContactAsync(jobId, cancellationToken);
        return await _adminRepo.GetByJobIdAsync(jobId, cancellationToken);
    }

    /// <summary>
    /// AM-003: a job should always carry a real primary contact, not lean on read-time
    /// fallbacks. If the persisted star is missing or invalid (points at a registration
    /// that is gone, inactive, or not a Director on this job), persist the default —
    /// the earliest-registered active Director (the same rule the !DIRECTOR text
    /// substitution used as its silent fallback). Jobs with no active Director are left
    /// untouched: an inactive starred Director keeps the star for when they're
    /// reactivated between seasons, and read paths already skip inactive contacts.
    /// </summary>
    private async Task EnsurePrimaryContactAsync(Guid jobId, CancellationToken cancellationToken)
    {
        var currentId = await _adminRepo.GetPrimaryContactIdAsync(jobId, cancellationToken);
        if (currentId != null
            && await _adminRepo.IsActiveDirectorOnJobAsync(jobId, currentId.Value, cancellationToken))
            return;

        var fallbackId = await _adminRepo.GetEarliestActiveDirectorIdAsync(jobId, cancellationToken);
        if (fallbackId != null && fallbackId != currentId)
            await _adminRepo.SetPrimaryContactAsync(jobId, fallbackId, cancellationToken);
    }

    public async Task<AdministratorDto> AddAdministratorAsync(
        Guid jobId,
        AddAdministratorRequest request,
        string currentUserId,
        CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByNameAsync(request.UserName);
        if (user == null)
            throw new ArgumentException($"User '{request.UserName}' not found.");

        if (!RoleNameToIdMap.TryGetValue(request.RoleName, out var roleId))
            throw new ArgumentException($"Invalid role name: '{request.RoleName}'.");

        // ── AM-004 eligibility wall (server-side; the search filter is not the only gate) ──
        // Family logins are shared within the household: elevating one hands Director access
        // to everyone who knows the password (global check — never liveness-gated). Role
        // classification counts only LIVE registrations — bActive, and the job unexpired for
        // the role type (admin roles ride Jobs.ExpiryAdmin, every other role rides
        // Jobs.ExpiryUsers): a stale/expired grant must not poison an otherwise lane-pure
        // account. Two qualifying shapes:
        //  1. Admin-only accounts (every windowed registration carries an admin role)
        //     → new registration.
        //  2. Unassigned Adult–only accounts on this customer (the pending-coach funnel)
        //     → their pending registration is CONVERTED in place, which also removes them
        //       from the coach-approval queue.
        if (await _adminRepo.IsFamilyCredentialHolderAsync(user.Id, cancellationToken))
            throw new ArgumentException(
                $"'{request.UserName}' is a family login — family credentials are shared within the household " +
                "and cannot hold admin roles. Have the person register on this site as a coach/staff adult " +
                "with their own account, then accept them here.");

        var now = DateTime.Now;
        // Liveness filter applies to CLASSIFICATION only. The duplicate-on-job guard below must
        // see ALL registrations — a deactivated admin on this job is managed via the grid's
        // Active toggle, never by stacking a second registration.
        var allRegistrations = await _adminRepo.GetRegistrationsByUserIdAsync(user.Id, cancellationToken);
        var registrations = allRegistrations
            .Where(r => r.BActive == true
                && (RoleConstants.AdminRoleIds.Contains(r.RoleId, StringComparer.OrdinalIgnoreCase)
                    ? r.Job.ExpiryAdmin > now
                    : r.Job.ExpiryUsers > now))
            .ToList();

        if (registrations.Count == 0)
            throw new ArgumentException(
                $"'{request.UserName}' has no active registrations. Have the person register on this " +
                "site as a coach/staff adult, then accept them here.");

        var lane = GetRoleLane(roleId);
        var isLanePure = registrations.All(r =>
            lane.Contains(r.RoleId, StringComparer.OrdinalIgnoreCase));
        var isPendingAdultOnly = registrations.All(r =>
            string.Equals(r.RoleId, RoleConstants.UnassignedAdult, StringComparison.OrdinalIgnoreCase));

        if (!isLanePure && !isPendingAdultOnly)
            throw new ArgumentException(
                $"'{request.UserName}' has active registrations outside the {request.RoleName} role and is " +
                "not eligible. Have the person register on this site as a coach/staff adult with a " +
                "separate account.");

        if (isPendingAdultOnly)
        {
            var customerId = await _jobRepo.GetCustomerIdAsync(jobId, cancellationToken)
                ?? throw new InvalidOperationException($"Job '{jobId}' not found.");

            // Prefer a pending registration already on this job; otherwise the most recent
            // one on this customer (retargeted to this job on convert).
            var candidate = registrations.FirstOrDefault(r => r.JobId == jobId);
            if (candidate == null)
            {
                foreach (var reg in registrations.OrderByDescending(r => r.RegistrationTs))
                {
                    var regCustomerId = await _jobRepo.GetCustomerIdAsync(reg.JobId, cancellationToken);
                    if (regCustomerId == customerId)
                    {
                        candidate = reg;
                        break;
                    }
                }
            }

            if (candidate == null)
                throw new ArgumentException(
                    $"'{request.UserName}' has no pending adult registration with this customer. " +
                    "Have the person register on this site as a coach/staff adult, then accept them here.");

            if (candidate.PaidTotal != 0)
                throw new InvalidOperationException(
                    $"'{request.UserName}' has payments recorded on their pending registration — " +
                    "it cannot be converted to an admin registration automatically. Resolve the payment first.");

            // Convert in place: the pending row BECOMES the admin registration (single row,
            // continuous history) and drops out of the coach-approval queue in the same stroke.
            candidate.JobId = jobId;
            candidate.RoleId = roleId;
            candidate.RegistrationCategory = "Director";
            candidate.BActive = true;
            candidate.Modified = DateTime.Now;
            candidate.LebUserId = currentUserId;
            // Admin registrations are fee-free
            candidate.FeeBase = 0;
            candidate.FeeDiscount = 0;
            candidate.FeeDiscountMp = 0;
            candidate.FeeDonation = 0;
            candidate.FeeLatefee = 0;
            candidate.FeeProcessing = 0;

            await _adminRepo.SaveChangesAsync(cancellationToken);

            return await _adminRepo.GetAdminProjectionByIdAsync(candidate.RegistrationId, cancellationToken)
                ?? throw new InvalidOperationException("Failed to retrieve converted administrator.");
        }

        // Lane-pure account: pin with a new registration on this job.
        // Guard on ALL registrations (not the live subset) — an inactive admin reg on this job
        // still blocks; reactivate via the grid instead of creating a duplicate row.
        if (allRegistrations.Any(r => r.JobId == jobId))
            throw new ArgumentException($"'{request.UserName}' already has a registration on this job.");

        var registration = new Registrations
        {
            RegistrationId = Guid.NewGuid(),
            BActive = true,
            JobId = jobId,
            LebUserId = currentUserId,
            Modified = DateTime.Now,
            RegistrationCategory = "Director",
            RegistrationTs = DateTime.Now,
            RoleId = roleId,
            UserId = user.Id,
            FeeBase = 0,
            FeeDiscount = 0,
            FeeDiscountMp = 0,
            FeeDonation = 0,
            FeeLatefee = 0,
            FeeProcessing = 0,
            PaidTotal = 0
        };

        _adminRepo.Add(registration);
        await _adminRepo.SaveChangesAsync(cancellationToken);

        // Return projected DTO (no full entity reload needed)
        return await _adminRepo.GetAdminProjectionByIdAsync(registration.RegistrationId, cancellationToken)
            ?? throw new InvalidOperationException("Failed to retrieve saved administrator.");
    }

    public async Task<AdministratorDto> UpdateAdministratorAsync(
        Guid registrationId,
        UpdateAdministratorRequest request,
        string currentUserId,
        CancellationToken cancellationToken = default)
    {
        var registration = await _adminRepo.GetByIdAsync(registrationId, cancellationToken)
            ?? throw new KeyNotFoundException($"Registration '{registrationId}' not found.");

        if (registration.RoleId == RoleConstants.Superuser)
            throw new InvalidOperationException("Cannot edit a Superuser registration.");

        if (!RoleNameToIdMap.TryGetValue(request.RoleName, out var roleId))
            throw new ArgumentException($"Invalid role name: '{request.RoleName}'.");

        registration.BActive = request.IsActive;
        registration.RoleId = roleId;
        registration.Modified = DateTime.Now;
        registration.LebUserId = currentUserId;

        await _adminRepo.SaveChangesAsync(cancellationToken);

        return await _adminRepo.GetAdminProjectionByIdAsync(registrationId, cancellationToken)
            ?? throw new InvalidOperationException("Failed to retrieve updated administrator.");
    }

    public async Task DeleteAdministratorAsync(
        Guid registrationId,
        CancellationToken cancellationToken = default)
    {
        var registration = await _adminRepo.GetByIdAsync(registrationId, cancellationToken)
            ?? throw new KeyNotFoundException($"Registration '{registrationId}' not found.");

        if (registration.RoleId == RoleConstants.Superuser)
            throw new InvalidOperationException("Cannot delete a Superuser registration.");

        // Jobs.PrimaryContactRegistrationId references this row — clear the star before
        // deleting or the FK blocks the delete. The next load re-seeds the default.
        var primaryId = await _adminRepo.GetPrimaryContactIdAsync(registration.JobId, cancellationToken);
        if (primaryId == registration.RegistrationId)
            await _adminRepo.SetPrimaryContactAsync(registration.JobId, null, cancellationToken);

        _adminRepo.Remove(registration);
        await _adminRepo.SaveChangesAsync(cancellationToken);
    }

    public async Task<List<AdministratorDto>> ToggleStatusAsync(
        Guid jobId,
        Guid registrationId,
        CancellationToken cancellationToken = default)
    {
        var registration = await _adminRepo.GetByIdAsync(registrationId, cancellationToken)
            ?? throw new KeyNotFoundException($"Registration '{registrationId}' not found.");

        if (registration.JobId != jobId)
            throw new InvalidOperationException("Registration does not belong to this job.");

        if (registration.RoleId == RoleConstants.Superuser)
            throw new InvalidOperationException("Cannot modify a Superuser registration.");

        registration.BActive = !(registration.BActive ?? false);
        registration.Modified = DateTime.Now;

        await _adminRepo.SaveChangesAsync(cancellationToken);
        await EnsurePrimaryContactAsync(jobId, cancellationToken);
        return await _adminRepo.GetByJobIdAsync(jobId, cancellationToken);
    }

    public async Task<List<AdministratorDto>> SetAllStatusAsync(
        Guid jobId,
        bool isActive,
        CancellationToken cancellationToken = default)
    {
        var registrations = await _adminRepo.GetNonSuperuserByJobIdAsync(jobId, cancellationToken);

        foreach (var reg in registrations)
        {
            reg.BActive = isActive;
            reg.Modified = DateTime.Now;
        }

        await _adminRepo.SaveChangesAsync(cancellationToken);
        await EnsurePrimaryContactAsync(jobId, cancellationToken);
        return await _adminRepo.GetByJobIdAsync(jobId, cancellationToken);
    }

    public async Task<List<AdministratorDto>> SetPrimaryContactAsync(
        Guid jobId,
        Guid registrationId,
        CancellationToken cancellationToken = default)
    {
        // Verify the registration exists and belongs to this job
        var registration = await _adminRepo.GetByIdAsync(registrationId, cancellationToken)
            ?? throw new KeyNotFoundException($"Registration '{registrationId}' not found.");

        if (registration.JobId != jobId)
            throw new InvalidOperationException("Registration does not belong to this job.");

        var currentPrimaryId = await _adminRepo.GetPrimaryContactIdAsync(jobId, cancellationToken);

        // The star is a Director-only concept (matches the UI and the !DIRECTOR fallback);
        // without this check a non-Director star would just be silently overwritten by the
        // heal below. Clearing (re-clicking the current star) is exempt.
        if (currentPrimaryId != registrationId
            && !await _adminRepo.IsActiveDirectorOnJobAsync(jobId, registrationId, cancellationToken))
            throw new InvalidOperationException("Only an active Director can be the primary contact.");

        // Toggle: if already primary contact, clear it; otherwise set it. A job always
        // carries a primary contact (AM-003), so "clear" doesn't leave a void — the heal
        // below re-seeds the default (earliest-registered active Director). Net effect:
        // un-starring a hand-picked Director reverts the star to the default.
        var newPrimaryId = currentPrimaryId == registrationId ? null : (Guid?)registrationId;

        await _adminRepo.SetPrimaryContactAsync(jobId, newPrimaryId, cancellationToken);
        await EnsurePrimaryContactAsync(jobId, cancellationToken);

        // Return refreshed list so UI updates in one round-trip
        return await _adminRepo.GetByJobIdAsync(jobId, cancellationToken);
    }

    public async Task<UserSearchResponseDto> SearchUsersAsync(
        string query,
        Guid jobId,
        string roleName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Length < 2)
            return new UserSearchResponseDto { Results = [] };

        if (!RoleNameToIdMap.TryGetValue(roleName, out var roleId))
            throw new ArgumentException($"Invalid role name: '{roleName}'.");

        var customerId = await _jobRepo.GetCustomerIdAsync(jobId, cancellationToken);
        if (customerId == null)
            return new UserSearchResponseDto { Results = [] };

        var lane = GetRoleLane(roleId);
        var results = await _userRepo.SearchAdminCandidatesAsync(
            query, customerId.Value, jobId, lane, 10, cancellationToken);

        if (results.Count == 0)
        {
            // Empty is ambiguous to the user (not registered? wrong kind of account? broken?) —
            // diagnose so the modal can show the matching funnel message.
            var reason = await _userRepo.DiagnoseAdminCandidateMissAsync(
                query, customerId.Value, jobId, lane, cancellationToken);

            return new UserSearchResponseDto
            {
                Results = [],
                EmptyReason = reason switch
                {
                    AdminCandidateMissReason.FamilyOrPlayer => "familyOrPlayer",
                    AdminCandidateMissReason.AlreadyAdmin => "alreadyAdmin",
                    AdminCandidateMissReason.OutsideLane => "outsideLane",
                    _ => "notFound"
                }
            };
        }

        return new UserSearchResponseDto
        {
            Results = results.Select(r => new UserSearchResultDto
            {
                UserId = r.UserId,
                UserName = r.UserName,
                DisplayName = $"{r.LastName}, {r.FirstName}".Trim(' ', ','),
                AccountType = r.AccountType
            }).ToList()
        };
    }

}
