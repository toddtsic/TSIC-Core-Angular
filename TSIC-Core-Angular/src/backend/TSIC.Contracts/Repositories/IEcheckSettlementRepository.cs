using TSIC.Domain.Entities;

namespace TSIC.Contracts.Repositories;

/// <summary>
/// Data access for echeck.Settlement / echeck.SweepLog. Used by the daily ADN sweep
/// to find Settlement rows that match returned eCheck transactions, and by future
/// eCheck submission flow to insert new pending settlements.
/// </summary>
public interface IEcheckSettlementRepository
{
    /// <summary>
    /// Fetch tracked Settlement rows whose AdnTransactionId is in the given set,
    /// joined with their RegistrationAccounting → Registration. Used by the sweep
    /// to locate originals for returned eCheck transactions.
    /// </summary>
    Task<List<Settlement>> GetByAdnTransactionIdsAsync(
        IEnumerable<string> adnTransactionIds, CancellationToken ct = default);

    /// <summary>Insert a new SweepLog row at sweep start. Returns the tracked entity.</summary>
    Task<SweepLog> StartSweepLogAsync(string triggeredBy, CancellationToken ct = default);

    /// <summary>Update the SweepLog row with completion timestamp + counts.</summary>
    Task CompleteSweepLogAsync(
        SweepLog log,
        int recordsChecked,
        int recordsSettled,
        int recordsReturned,
        int recordsErrored,
        string? errorMessage,
        CancellationToken ct = default);

    /// <summary>
    /// Track a new Settlement row (does NOT save changes). Status is the caller's
    /// responsibility — for fresh customer eCheck submissions use "Pending".
    /// </summary>
    void Add(Settlement settlement);

    /// <summary>Persist any pending tracked changes.</summary>
    Task SaveChangesAsync(CancellationToken ct = default);
}
