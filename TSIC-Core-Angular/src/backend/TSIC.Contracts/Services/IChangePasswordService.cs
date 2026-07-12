using TSIC.Contracts.Dtos.ChangePassword;

namespace TSIC.Contracts.Services;

/// <summary>
/// The SuperUser Change Password utility. See <c>docs/Domain/change-password-contract.md</c>.
/// </summary>
public interface IChangePasswordService
{
    Task<List<ChangePasswordSearchResultDto>> SearchAsync(
        ChangePasswordSearchRequest request,
        CancellationToken ct = default);

    Task<List<ChangePasswordRoleOptionDto>> GetRoleOptionsAsync(
        CancellationToken ct = default);

    /// <summary>
    /// Reset the password on ONE of a registration's two accounts.
    ///
    /// The registration is the target, not the username: the server follows
    /// <c>Registrations.UserId</c> or <c>Registrations.Family_UserId</c> depending on
    /// <see cref="AdminResetPasswordRequest.Target"/>. <c>ExpectedUserName</c> is checked against
    /// what that resolves to and a mismatch is rejected.
    ///
    /// Previously this took a bare username and ignored the registration entirely, which meant any
    /// SuperUser could reset any account in the system — including another SuperUser's — by posting
    /// a username with an arbitrary GUID in the route.
    /// </summary>
    Task<string> ResetPasswordAsync(
        Guid registrationId,
        AdminResetPasswordRequest request,
        CancellationToken ct = default);

    Task UpdateUserEmailAsync(
        Guid registrationId,
        string? newEmail,
        CancellationToken ct = default);

    Task UpdateFamilyEmailsAsync(
        Guid registrationId,
        string? familyEmail,
        string? momEmail,
        string? dadEmail,
        CancellationToken ct = default);

    Task<MergeCandidatesResponse> GetUserMergeCandidatesAsync(
        Guid registrationId,
        CancellationToken ct = default);

    Task<MergeCandidatesResponse> GetFamilyMergeCandidatesAsync(
        Guid registrationId,
        CancellationToken ct = default);

    /// <summary>Irreversible. Returns what moved and where from — the audit payload, not a count.</summary>
    Task<MergeResultDto> MergeUsernameAsync(
        Guid registrationId,
        string targetUserName,
        CancellationToken ct = default);

    /// <summary>Irreversible. Returns what moved and where from — the audit payload, not a count.</summary>
    Task<MergeResultDto> MergeFamilyUsernameAsync(
        Guid registrationId,
        string targetFamilyUserName,
        CancellationToken ct = default);
}
