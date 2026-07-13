using Microsoft.AspNetCore.Identity;
using TSIC.Contracts.Dtos.ChangePassword;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Constants;
using TSIC.Infrastructure.Data.Identity;

namespace TSIC.API.Services.Admin;

/// <summary>
/// See <c>docs/Domain/change-password-contract.md</c>. The identity model in §1 is not optional
/// reading — a player never signs in, and a merge is not a rename.
/// </summary>
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

    /// <summary>
    /// The six roles legacy offered. Deliberately NOT read from <c>AspNetRoles</c>:
    /// <c>Family</c> is absent because you reach a family login by searching Player (it arrives on
    /// the join), and <c>Superuser</c> is absent by policy.
    ///
    /// Referee / RefAssignor / Scorer / Recruiter / StoreAdmin / StpAdmin have real logins and are
    /// NOT findable here. That is an open product decision, not an oversight — contract §4.
    /// </summary>
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
        Guid registrationId,
        AdminResetPasswordRequest request,
        CancellationToken ct = default)
    {
        // THE targeting step. The account comes from the REGISTRATION's own FK — never from the
        // request body. The body only says which of the two FKs to follow.
        var target = await _repo.ResolveResetTargetAsync(registrationId, request.Target, ct)
            ?? throw new InvalidOperationException(request.Target == ResetPasswordTarget.Family
                ? "This registration has no family login."
                : "This registration has no user account.");

        // The caller told us who they think they're resetting. If the UI is stale — the row was
        // merged away under them, say — that disagreement must stop the write, not be papered over.
        if (!string.Equals(target.UserName, request.ExpectedUserName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"This registration's {(request.Target == ResetPasswordTarget.Family ? "family login" : "login")} " +
                $"is '{target.UserName}', not '{request.ExpectedUserName}'. Re-run the search and try again.");
        }

        var user = await _userManager.FindByIdAsync(target.UserId)
            ?? throw new InvalidOperationException($"Account '{target.UserName}' no longer exists.");

        var token = await _userManager.GeneratePasswordResetTokenAsync(user);
        var result = await _userManager.ResetPasswordAsync(user, token, request.NewPassword);

        if (!result.Succeeded)
        {
            var errors = string.Join("; ", result.Errors.Select(e => e.Description));
            throw new InvalidOperationException($"Password reset failed: {errors}");
        }

        return $"Password for '{target.UserName}' reset successfully.";
    }

    public async Task UpdateUserEmailAsync(
        Guid registrationId,
        string? newEmail,
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

    public async Task<MergeCandidatesResponse> GetUserMergeCandidatesAsync(
        Guid registrationId,
        CancellationToken ct = default)
    {
        return await _repo.GetUserMergeCandidatesAsync(registrationId, ct);
    }

    public async Task<MergeCandidatesResponse> GetFamilyMergeCandidatesAsync(
        Guid registrationId,
        CancellationToken ct = default)
    {
        return await _repo.GetFamilyMergeCandidatesAsync(registrationId, ct);
    }

    public async Task<MergeResultDto> MergeUsernameAsync(
        Guid registrationId,
        string targetUserName,
        IReadOnlyList<string> sourceUserNames,
        CancellationToken ct = default)
    {
        return await _repo.MergeUserRegistrationsAsync(registrationId, targetUserName, sourceUserNames, ct);
    }

    public async Task<MergeResultDto> MergeFamilyUsernameAsync(
        Guid registrationId,
        string targetFamilyUserName,
        IReadOnlyList<string> sourceFamilyUserNames,
        CancellationToken ct = default)
    {
        return await _repo.MergeFamilyRegistrationsAsync(
            registrationId, targetFamilyUserName, sourceFamilyUserNames, ct);
    }
}
