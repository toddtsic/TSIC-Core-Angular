using Microsoft.AspNetCore.Identity;
using TSIC.Contracts.Dtos.ChangePassword;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Constants;
using TSIC.Infrastructure.Data.Identity;

namespace TSIC.API.Services.Admin;

public class ChangePasswordService : IChangePasswordService
{
    private readonly IChangePasswordRepository _repo;
    private readonly UserManager<ApplicationUser> _userManager;

    public ChangePasswordService(
        IChangePasswordRepository repo,
        UserManager<ApplicationUser> userManager)
    {
        _repo = repo;
        _userManager = userManager;
    }

    public async Task<List<ChangePasswordSearchResultDto>> SearchAsync(
        ChangePasswordSearchRequest request,
        CancellationToken ct = default)
    {
        if (string.Equals(request.RoleId, RoleConstants.Player, StringComparison.OrdinalIgnoreCase))
        {
            return await _repo.SearchPlayerRegistrationsAsync(request, ct);
        }

        return await _repo.SearchAdultRegistrationsAsync(request, ct);
    }

    public Task<List<ChangePasswordRoleOptionDto>> GetRoleOptionsAsync(
        CancellationToken ct = default)
    {
        var options = new List<ChangePasswordRoleOptionDto>
        {
            new() { RoleId = RoleConstants.Player, RoleName = RoleConstants.Names.PlayerName },
            new() { RoleId = RoleConstants.ClubRep, RoleName = RoleConstants.Names.ClubRepName },
            new() { RoleId = RoleConstants.Director, RoleName = RoleConstants.Names.DirectorName },
            new() { RoleId = RoleConstants.SuperDirector, RoleName = RoleConstants.Names.SuperDirectorName },
            new() { RoleId = RoleConstants.UnassignedAdult, RoleName = RoleConstants.Names.UnassignedAdultName },
            new() { RoleId = RoleConstants.Staff, RoleName = RoleConstants.Names.StaffName }
        };

        return Task.FromResult(options);
    }

    public async Task<string> ResetPasswordAsync(
        string userName,
        string newPassword,
        CancellationToken ct = default)
    {
        var user = await _userManager.FindByNameAsync(userName)
            ?? throw new InvalidOperationException($"User '{userName}' not found.");

        var token = await _userManager.GeneratePasswordResetTokenAsync(user);
        var result = await _userManager.ResetPasswordAsync(user, token, newPassword);

        if (!result.Succeeded)
        {
            var errors = string.Join("; ", result.Errors.Select(e => e.Description));
            throw new InvalidOperationException($"Password reset failed: {errors}");
        }

        return $"Password for '{userName}' reset successfully.";
    }

    public async Task UpdateUserEmailAsync(
        Guid registrationId,
        string newEmail,
        CancellationToken ct = default)
    {
        await _repo.UpdateUserEmailAsync(registrationId, newEmail, ct);
    }

    public async Task UpdateFamilyEmailsAsync(
        Guid registrationId,
        string? familyEmail,
        string? momEmail,
        string? dadEmail,
        CancellationToken ct = default)
    {
        await _repo.UpdateFamilyEmailsAsync(registrationId, familyEmail, momEmail, dadEmail, ct);
    }

    public async Task<List<MergeCandidateDto>> GetUserMergeCandidatesAsync(
        Guid registrationId,
        CancellationToken ct = default)
    {
        return await _repo.GetUserMergeCandidatesAsync(registrationId, ct);
    }

    public async Task<List<MergeCandidateDto>> GetFamilyMergeCandidatesAsync(
        Guid registrationId,
        CancellationToken ct = default)
    {
        return await _repo.GetFamilyMergeCandidatesAsync(registrationId, ct);
    }

    public async Task<int> MergeUsernameAsync(
        Guid registrationId,
        string targetUserName,
        CancellationToken ct = default)
    {
        return await _repo.MergeUserRegistrationsAsync(registrationId, targetUserName, ct);
    }

    public async Task<int> MergeFamilyUsernameAsync(
        Guid registrationId,
        string targetFamilyUserName,
        CancellationToken ct = default)
    {
        return await _repo.MergeFamilyRegistrationsAsync(registrationId, targetFamilyUserName, ct);
    }
}
