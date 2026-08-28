using Microsoft.AspNetCore.Identity;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Dtos.Store;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Constants;
using TSIC.Infrastructure.Data.Identity;

namespace TSIC.API.Services.Store;

/// <summary>
/// Store Administrators roster — port of legacy <c>StoreAdminAddController</c>.
/// See <see cref="IStoreAdminRosterService"/> for why this exists alongside the SuperUser
/// Administrators screen.
/// </summary>
public sealed class StoreAdminRosterService : IStoreAdminRosterService
{
    /// <summary>
    /// The display name <see cref="IAdministratorService"/> maps to
    /// <see cref="RoleConstants.StoreAdmin"/>. Every delegation below pins this — the role is
    /// never taken from the caller, so no request to this surface can grant anything else.
    /// </summary>
    private const string StoreAdminRoleName = RoleConstants.Names.StoreAdminName;

    private readonly IAdministratorRepository _adminRepo;
    private readonly IAdministratorService _adminService;
    private readonly UserManager<ApplicationUser> _userManager;

    public StoreAdminRosterService(
        IAdministratorRepository adminRepo,
        IAdministratorService adminService,
        UserManager<ApplicationUser> userManager)
    {
        _adminRepo = adminRepo;
        _adminService = adminService;
        _userManager = userManager;
    }

    public Task<List<StoreAdminRosterRowDto>> GetRosterAsync(
        Guid jobId,
        CancellationToken cancellationToken = default)
        => _adminRepo.GetStoreAdminRosterAsync(jobId, cancellationToken);

    public async Task<List<StoreAdminRosterRowDto>> AddAsync(
        Guid jobId,
        StoreAdminAddRequest request,
        string currentUserId,
        CancellationToken cancellationToken = default)
    {
        // The whole eligibility wall — family credentials, the AM-004 lane, the
        // pending-coach funnel, the duplicate-on-job guards — lives in AdministratorService.
        // Delegating keeps one implementation of it rather than a store-flavoured second
        // copy that would drift the first time the ruling changes.
        await _adminService.AddAdministratorAsync(
            jobId,
            new AddAdministratorRequest
            {
                UserName = request.UserName,
                RoleName = StoreAdminRoleName
            },
            currentUserId,
            cancellationToken);

        return await _adminRepo.GetStoreAdminRosterAsync(jobId, cancellationToken);
    }

    public async Task<List<StoreAdminRosterRowDto>> UpdateAsync(
        Guid jobId,
        Guid registrationId,
        StoreAdminUpdateRequest request,
        string currentUserId,
        CancellationToken cancellationToken = default)
    {
        var registration = await _adminRepo.GetByIdAsync(registrationId, cancellationToken)
            ?? throw new KeyNotFoundException($"Registration '{registrationId}' not found.");

        // Both halves matter. The job check is the standard cross-job boundary. The role check
        // is what keeps this Director-reachable surface from becoming a way to edit a Director
        // or a Superuser: the caller supplies only a registration id, so without it any admin
        // row on the job would be in range.
        if (registration.JobId != jobId
            || !string.Equals(registration.RoleId, RoleConstants.StoreAdmin, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("That registration is not a Store Admin on this job.");

        registration.BActive = request.IsActive;
        registration.Modified = DateTime.Now;
        registration.LebUserId = currentUserId;
        await _adminRepo.SaveChangesAsync(cancellationToken);

        if (registration.UserId != null)
        {
            var user = await _userManager.FindByIdAsync(registration.UserId);
            if (user != null)
            {
                // SetEmailAsync rather than assigning the property: it also rewrites
                // NormalizedEmail, which the forgot-password lookup keys off. Legacy assigned
                // the raw column and left the normalized copy stale.
                if (!string.Equals(user.Email, request.Email, StringComparison.OrdinalIgnoreCase))
                {
                    var emailResult = await _userManager.SetEmailAsync(user, request.Email);
                    if (!emailResult.Succeeded)
                        throw new ArgumentException(
                            emailResult.Errors.FirstOrDefault()?.Description ?? "Could not update the email address.");
                }

                user.Cellphone = request.Cellphone;
                var updateResult = await _userManager.UpdateAsync(user);
                if (!updateResult.Succeeded)
                    throw new ArgumentException(
                        updateResult.Errors.FirstOrDefault()?.Description ?? "Could not update the contact details.");
            }
        }

        return await _adminRepo.GetStoreAdminRosterAsync(jobId, cancellationToken);
    }

    public Task<UserSearchResponseDto> SearchCandidatesAsync(
        string query,
        Guid jobId,
        CancellationToken cancellationToken = default)
        => _adminService.SearchUsersAsync(query, jobId, StoreAdminRoleName, cancellationToken);
}
