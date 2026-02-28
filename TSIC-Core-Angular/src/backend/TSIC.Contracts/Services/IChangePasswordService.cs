using TSIC.Contracts.Dtos.ChangePassword;

namespace TSIC.Contracts.Services;

public interface IChangePasswordService
{
    Task<List<ChangePasswordSearchResultDto>> SearchAsync(
        ChangePasswordSearchRequest request,
        CancellationToken ct = default);

    Task<List<ChangePasswordRoleOptionDto>> GetRoleOptionsAsync(
        CancellationToken ct = default);

    Task<string> ResetPasswordAsync(
        string userName,
        string newPassword,
        CancellationToken ct = default);

    Task UpdateUserEmailAsync(
        Guid registrationId,
        string newEmail,
        CancellationToken ct = default);

    Task UpdateFamilyEmailsAsync(
        Guid registrationId,
        string? familyEmail,
        string? momEmail,
        string? dadEmail,
        CancellationToken ct = default);

    Task<List<MergeCandidateDto>> GetUserMergeCandidatesAsync(
        Guid registrationId,
        CancellationToken ct = default);

    Task<List<MergeCandidateDto>> GetFamilyMergeCandidatesAsync(
        Guid registrationId,
        CancellationToken ct = default);

    Task<int> MergeUsernameAsync(
        Guid registrationId,
        string targetUserName,
        CancellationToken ct = default);

    Task<int> MergeFamilyUsernameAsync(
        Guid registrationId,
        string targetFamilyUserName,
        CancellationToken ct = default);
}
