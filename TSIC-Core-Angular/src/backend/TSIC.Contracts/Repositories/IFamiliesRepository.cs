using TSIC.Domain.Entities;

namespace TSIC.Contracts.Repositories;

/// <summary>
/// Repository for Families entity data access.
/// </summary>
public interface IFamiliesRepository
{
    /// <summary>
    /// Get a family by its FamilyUserId.
    /// </summary>
    Task<Families?> GetByFamilyUserIdAsync(string familyUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get recipient emails for a family and its players under a job.
    /// Returns distinct normalized emails.
    /// </summary>
    Task<List<string>> GetEmailsForFamilyAndPlayersAsync(Guid jobId, string familyUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Add a new Families record (does NOT call SaveChanges).
    /// </summary>
    void Add(Families family);

    /// <summary>
    /// Update an existing Families record (does NOT call SaveChanges).
    /// </summary>
    void Update(Families family);

    /// <summary>
    /// Persist changes to the database.
    /// </summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
