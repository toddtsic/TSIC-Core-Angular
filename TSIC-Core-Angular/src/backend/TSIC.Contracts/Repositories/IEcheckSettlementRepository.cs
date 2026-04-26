using TSIC.Domain.Entities;

namespace TSIC.Contracts.Repositories;

/// <summary>
/// Data access for the eCheck settlement-tracking tables (echeck.Settlement,
/// echeck.SweepLog). Used by the daily settlement sweep BackgroundService.
/// </summary>
public interface IEcheckSettlementRepository
{
    /// <summary>
    /// Load all Settlement rows in Pending status whose NextCheckAt is at or
    /// before <paramref name="now"/>. Tracked entities, with the paired
    /// RegistrationAccounting and its Registration eagerly loaded so the
    /// sweep service can mutate them and SaveChangesAsync once at the end.
    /// </summary>
    Task<List<Settlement>> GetPendingDueAsync(DateTime now, CancellationToken ct = default);

    /// <summary>
    /// Insert a SweepLog row marking sweep start. Returns the tracked entity
    /// so the caller can update it on completion.
    /// </summary>
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
    /// Persist any pending tracked changes (Settlement status updates,
    /// reversal RA row inserts, Registration mutations).
    /// </summary>
    Task SaveChangesAsync(CancellationToken ct = default);
}
