using TSIC.Domain.Entities;

namespace TSIC.Contracts.Repositories;

/// <summary>
/// Repository for managing Families entity data access.
/// Encapsulates all EF Core queries related to family records.
/// </summary>
public interface IFamilyRepository
{
    /// <summary>
    /// Get a family record by family user ID
    /// </summary>
    Task<Families?> GetByFamilyUserIdAsync(string familyUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get player emails for a family within a specific job (projected — no entity loading).
    /// </summary>
    Task<List<string>> GetFamilyPlayerEmailsForJobAsync(
        Guid jobId,
        string familyUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get player emails for a family within a specific job by jobPath (projected — no entity loading).
    /// </summary>
    Task<List<string>> GetFamilyPlayerEmailsForJobAsync(
        string jobPath,
        string familyUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get family contact information for insurance purposes.
    /// </summary>
    Task<FamilyContactInfo?> GetFamilyContactAsync(string familyUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// True iff the caller and the named player belong to the same family group.
    /// Either party may be the family-account holder (FamilyMembers.FamilyUserId)
    /// or a member (FamilyMembers.FamilyMemberUserId). Used by med-form upload/
    /// download/delete to verify ownership before granting access.
    /// </summary>
    Task<bool> IsPlayerInFamilyAsync(
        string callerUserId,
        string playerUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The ids in <paramref name="userIds"/> that are family accounts (a Families row keyed by
    /// the login). Usage analysis uses it to name the role of a signed-in request that carried
    /// no registration: the player wizard runs on a Family token with no regId.
    /// </summary>
    Task<List<string>> GetFamilyUserIdsAmongAsync(
        IReadOnlyList<string> userIds,
        CancellationToken cancellationToken = default);
}

public record FamilyContactInfo
{
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? Email { get; init; }
    public string? Phone { get; init; }
    public string? City { get; init; }
    public string? State { get; init; }
    public string? Zip { get; init; }
}
