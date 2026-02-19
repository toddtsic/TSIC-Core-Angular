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
    private readonly UserManager<ApplicationUser> _userManager;

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
        UserManager<ApplicationUser> userManager)
    {
        _adminRepo = adminRepo;
        _userRepo = userRepo;
        _userManager = userManager;
    }

    public async Task<List<AdministratorDto>> GetAdministratorsAsync(
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        return await _adminRepo.GetByJobIdAsync(jobId, cancellationToken);
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

        var registration = new Registrations
        {
            RegistrationId = Guid.NewGuid(),
            BActive = true,
            JobId = jobId,
            LebUserId = currentUserId,
            Modified = DateTime.UtcNow,
            RegistrationCategory = "Director",
            RegistrationTs = DateTime.UtcNow,
            RoleId = roleId,
            UserId = user.Id,
            FeeBase = 0,
            FeeDiscount = 0,
            FeeDiscountMp = 0,
            FeeDonation = 0,
            FeeLatefee = 0,
            FeeProcessing = 0,
            FeeTotal = 0,
            OwedTotal = 0,
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
        registration.Modified = DateTime.UtcNow;
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
        registration.Modified = DateTime.UtcNow;

        await _adminRepo.SaveChangesAsync(cancellationToken);
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

        // Toggle: if already primary contact, clear it; otherwise set it
        var currentPrimaryId = await _adminRepo.GetPrimaryContactIdAsync(jobId, cancellationToken);
        var newPrimaryId = currentPrimaryId == registrationId ? null : (Guid?)registrationId;

        await _adminRepo.SetPrimaryContactAsync(jobId, newPrimaryId, cancellationToken);

        // Return refreshed list so UI updates in one round-trip
        return await _adminRepo.GetByJobIdAsync(jobId, cancellationToken);
    }

    public async Task<List<UserSearchResultDto>> SearchUsersAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Length < 2)
            return [];

        var results = await _userRepo.SearchAsync(query, 10, cancellationToken);

        return results.Select(r => new UserSearchResultDto
        {
            UserId = r.UserId,
            UserName = r.UserName,
            DisplayName = $"{r.LastName}, {r.FirstName}".Trim(' ', ',')
        }).ToList();
    }

    private static AdministratorDto MapToDto(Registrations r)
    {
        var isSuperuser = r.RoleId == RoleConstants.Superuser;
        var lastName = r.User?.LastName ?? "";
        var firstName = r.User?.FirstName ?? "";

        return new AdministratorDto
        {
            RegistrationId = r.RegistrationId,
            AdministratorName = $"{lastName}, {firstName}".Trim(' ', ','),
            UserName = r.User?.UserName ?? "",
            RoleName = isSuperuser ? null : r.Role?.Name,
            IsActive = r.BActive ?? false,
            RegisteredDate = r.RegistrationTs,
            IsSuperuser = isSuperuser,
            IsPrimaryContact = false
        };
    }
}
