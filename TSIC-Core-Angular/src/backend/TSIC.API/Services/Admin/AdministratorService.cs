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
    private readonly IRegistrationRepository _registrationRepo;
    private readonly IDeviceRepository _deviceRepo;
    private readonly UserManager<ApplicationUser> _userManager;

    /// <summary>
    /// AM-004 lane model: eligibility for granting a role is confined to that role's lane —
    /// an account qualifies only if every registration it has ever held (any job, any customer)
    /// lies within the lane. No cross-type grants (a Director cannot be handed Store Admin; a
    /// referee's account cannot become Ref Assignor — fresh admins come through the Unassigned
    /// Adult funnel instead). Director and SuperDirector share one lane: they are the same kind
    /// of person at two trust levels, and mixed D/SD accounts exist by design — a person may
    /// hold BOTH roles on one job (two registrations, one per hat, chosen at the login role
    /// picker), and the Edit modal flips a single registration between them. Every other admin
    /// type is strictly its own lane.
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
        IRegistrationRepository registrationRepo,
        IDeviceRepository deviceRepo,
        UserManager<ApplicationUser> userManager)
    {
        _adminRepo = adminRepo;
        _userRepo = userRepo;
        _registrationRepo = registrationRepo;
        _deviceRepo = deviceRepo;
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

        // ── Eligibility wall (server-side; the search filter is not the only gate) ──
        // Family logins are shared within the household: elevating one hands Director access
        // to everyone who knows the password (global check — never liveness-gated). Two
        // qualifying shapes (ruling 2026-08-13 replaced the customer-scoped convert-in-place
        // funnel):
        //  1. Lane-pure admin accounts — every LIVE registration (bActive, and the job
        //     unexpired for the role type: admin roles ride Jobs.ExpiryAdmin, every other
        //     role rides Jobs.ExpiryUsers) carries a lane role → new registration.
        //  2. Pending coach — EXACTLY ONE active Unassigned Adult registration, on any job of
        //     any customer, no expiry gate. Other roles on the account (a Staff row from a
        //     roster-swapper approval, player history, …) do not disqualify. → new admin
        //     registration on this job; the pending row is DELETED in the same stroke (it
        //     leaves the coach-approval queue; a Staff team hat is left untouched).
        //  3. No registrations at all — a known account with an empty footprint (2026-08-23).
        //     → new registration, same as shape 1.
        if (await _adminRepo.IsFamilyCredentialHolderAsync(user.Id, cancellationToken))
            throw new ArgumentException(
                $"'{request.UserName}' is a family login — family credentials are shared within the household " +
                "and cannot hold admin roles. Have the person register as a coach/staff adult " +
                "with their own account, then accept them here.");

        var now = DateTime.Now;
        // Liveness filter applies to lane CLASSIFICATION only. The duplicate-on-job guards
        // below must see ALL registrations — a deactivated admin on this job is managed via
        // the grid's Active toggle, never by stacking a second registration.
        var allRegistrations = await _adminRepo.GetRegistrationsByUserIdAsync(user.Id, cancellationToken);
        var liveRegistrations = allRegistrations
            .Where(r => r.BActive == true
                && (RoleConstants.AdminRoleIds.Contains(r.RoleId, StringComparer.OrdinalIgnoreCase)
                    ? r.Job.ExpiryAdmin > now
                    : r.Job.ExpiryUsers > now))
            .ToList();

        var lane = GetRoleLane(roleId);
        var isLanePure = liveRegistrations.Count > 0 && liveRegistrations.All(r =>
            lane.Contains(r.RoleId, StringComparer.OrdinalIgnoreCase));

        Registrations? pendingToDelete = null;

        // Third shape: no registration footprint at all — nothing to be impure about and
        // nothing on this job to collide with. Takes the same mint-a-new-row path as a
        // lane-pure account (Todd 2026-08-23 — returning admins whose history has aged out).
        // The duplicate guards below are vacuously satisfied on an empty set.
        if (isLanePure || allRegistrations.Count == 0)
        {
            // Lane-pure account: pin with a new registration on this job.
            // An inactive admin reg on this job still blocks; reactivate via the grid instead
            // of creating a duplicate row. Within the D/SD lane the OTHER role does not block:
            // dual-hat accounts hold Director AND SuperDirector on one job as two
            // registrations, one per hat, chosen at login. For single-role lanes
            // (lane = [requested role]) this collapses to "any reg on this job".
            if (allRegistrations.Any(r => r.JobId == jobId
                    && (string.Equals(r.RoleId, roleId, StringComparison.OrdinalIgnoreCase)
                        || !lane.Contains(r.RoleId, StringComparer.OrdinalIgnoreCase))))
                throw new ArgumentException(
                    $"'{request.UserName}' already has a registration on this job that blocks adding " +
                    $"{request.RoleName} — a deactivated admin is reactivated via the grid's Active toggle, " +
                    "never re-added.");
        }
        else
        {
            // Pending-coach funnel.
            var pendingRegs = allRegistrations
                .Where(r => r.BActive == true
                    && string.Equals(r.RoleId, RoleConstants.UnassignedAdult, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (pendingRegs.Count == 0)
                throw new ArgumentException(
                    $"'{request.UserName}' has no pending coach registration. Have the person register " +
                    "as a coach/staff adult on any event site, then accept them here.");

            if (pendingRegs.Count > 1)
                throw new ArgumentException(
                    $"'{request.UserName}' has {pendingRegs.Count} pending coach registrations — it is ambiguous " +
                    "which one the grant consumes. Delete or deactivate the extras in Search Registrations first.");

            // The person may hold other roles here (e.g. a Staff team hat) — only an existing
            // registration with the REQUESTED role blocks.
            if (allRegistrations.Any(r => r.JobId == jobId
                    && string.Equals(r.RoleId, roleId, StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException(
                    $"'{request.UserName}' already holds {request.RoleName} on this job — a deactivated " +
                    "admin is reactivated via the grid's Active toggle, never re-added.");

            pendingToDelete = pendingRegs[0];

            // Deletion preconditions — same wall as the Search Registrations delete. Coach
            // registrations are normally fee-free, so these almost never fire.
            if (pendingToDelete.PaidTotal != 0)
                throw new ArgumentException(
                    $"'{request.UserName}' has payments recorded on their pending coach registration — " +
                    "it cannot be removed automatically. Resolve the payment first.");
            if (await _registrationRepo.HasAccountingRecordsAsync(pendingToDelete.RegistrationId, cancellationToken))
                throw new ArgumentException(
                    $"'{request.UserName}' has accounting records on their pending coach registration — " +
                    "it cannot be removed automatically. Resolve them first.");
            if (await _registrationRepo.HasStoreCartBatchRecordsAsync(pendingToDelete.RegistrationId, cancellationToken))
                throw new ArgumentException(
                    $"'{request.UserName}' has store purchase records on their pending coach registration — " +
                    "it cannot be removed automatically.");
            if (!string.IsNullOrEmpty(pendingToDelete.RegsaverPolicyId))
                throw new ArgumentException(
                    $"'{request.UserName}' has an insurance policy on their pending coach registration — " +
                    "it cannot be removed automatically.");

            // Device links reference the row being deleted.
            var deviceRegIds = await _deviceRepo.GetDeviceRegistrationIdsByRegistrationAsync(
                pendingToDelete.RegistrationId, cancellationToken);
            if (deviceRegIds.Count > 0)
            {
                _deviceRepo.RemoveDeviceRegistrationIds(deviceRegIds);
                await _deviceRepo.SaveChangesAsync(cancellationToken);
            }
        }

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
        // Funnel path: the admin row is minted and the pending coach row deleted in ONE
        // SaveChanges (same scoped context → one transaction) — never an admin without the
        // pending row consumed, never the reverse.
        if (pendingToDelete != null)
            _adminRepo.Remove(pendingToDelete);
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

        // Dual-hat accounts hold Director AND SuperDirector on one job as two rows — flipping
        // this row's role must not mint a same-role duplicate of its sibling. Same invariant
        // the add path enforces; both write chokepoints carry it.
        if (!string.Equals(registration.RoleId, roleId, StringComparison.OrdinalIgnoreCase)
            && registration.UserId != null)
        {
            var siblings = await _adminRepo.GetRegistrationsByUserIdAsync(registration.UserId, cancellationToken);
            if (siblings.Any(r => r.RegistrationId != registrationId
                    && r.JobId == registration.JobId
                    && string.Equals(r.RoleId, roleId, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException(
                    $"This person already holds {request.RoleName} on this job on a separate row — " +
                    "manage that row instead of duplicating it.");
        }

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

        var lane = GetRoleLane(roleId);
        var results = await _userRepo.SearchAdminCandidatesAsync(
            query, jobId, roleId, lane, 10, cancellationToken);

        if (results.Count == 0)
        {
            // Empty is ambiguous to the user (not registered? wrong kind of account? broken?) —
            // diagnose so the modal can show the matching funnel message.
            var reason = await _userRepo.DiagnoseAdminCandidateMissAsync(
                query, jobId, roleId, lane, cancellationToken);

            return new UserSearchResponseDto
            {
                Results = [],
                EmptyReason = reason switch
                {
                    AdminCandidateMissReason.FamilyOrPlayer => "familyOrPlayer",
                    AdminCandidateMissReason.AlreadyAdmin => "alreadyAdmin",
                    AdminCandidateMissReason.OutsideLane => "outsideLane",
                    AdminCandidateMissReason.MultiplePending => "multiplePending",
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
