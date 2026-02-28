using TSIC.Contracts.Dtos.ChangePassword;

namespace TSIC.Contracts.Repositories;

public interface IChangePasswordRepository
{
    // ── Search ──

    Task<List<ChangePasswordSearchResultDto>> SearchPlayerRegistrationsAsync(
        ChangePasswordSearchRequest request,
        CancellationToken ct = default);

    Task<List<ChangePasswordSearchResultDto>> SearchAdultRegistrationsAsync(
        ChangePasswordSearchRequest request,
        CancellationToken ct = default);

    // ── Email updates ──

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

    // ── Merge candidates ──

    Task<List<MergeCandidateDto>> GetUserMergeCandidatesAsync(
        Guid registrationId,
        CancellationToken ct = default);

    Task<List<MergeCandidateDto>> GetFamilyMergeCandidatesAsync(
        Guid registrationId,
        CancellationToken ct = default);

    // ── Merge operations ──

    Task<int> MergeUserRegistrationsAsync(
        Guid registrationId,
        string targetUserName,
        CancellationToken ct = default);

    Task<int> MergeFamilyRegistrationsAsync(
        Guid registrationId,
        string targetFamilyUserName,
        CancellationToken ct = default);
}
