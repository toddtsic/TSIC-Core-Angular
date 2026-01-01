using TSIC.Domain.Entities;

namespace TSIC.Contracts.Repositories;

/// <summary>
/// Repository for RegistrationAccounting entity data access.
/// </summary>
public interface IRegistrationAccountingRepository
{
    /// <summary>
    /// Add a new accounting entry (does NOT save changes).
    /// </summary>
    void Add(RegistrationAccounting entry);

    /// <summary>
    /// Persist changes to the database.
    /// </summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Check for duplicate accounting entries for given job, family, and idempotency key.
    /// </summary>
    Task<bool> AnyDuplicateAsync(Guid jobId, string familyUserId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the most recent Authorize.Net transaction ID for the given registration IDs.
    /// </summary>
    Task<string?> GetLatestAdnTransactionIdAsync(IEnumerable<Guid> registrationIds, CancellationToken cancellationToken = default);
}
