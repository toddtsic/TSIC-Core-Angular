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
