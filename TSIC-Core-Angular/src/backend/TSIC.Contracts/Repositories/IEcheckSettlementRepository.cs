using TSIC.Domain.Entities;

namespace TSIC.Contracts.Repositories;

/// <summary>
/// Data access for echeck.Settlement / echeck.SweepLog. A Settlement row is the
/// return-watcher for a booked eCheck: minted "Pending" at submit (one-shot) or
/// "Settled" at sweep import (ARB drafts), matched by ADN transaction id when the
/// draft settles, returns, or goes stale (watchdog).
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

    /// <summary>
    /// Tracked Settlement rows still "Pending" submitted before <paramref name="olderThan"/> —
    /// the watchdog's work list (a healthy draft settles in 1–2 business days). Includes
    /// RegistrationAccounting → Registration → Job for reversal and digest reporting.
    /// </summary>
    Task<List<Settlement>> GetStalePendingAsync(DateTime olderThan, CancellationToken ct = default);

    /// <summary>
    /// Integrity net: eCheck payment RegistrationAccounting rows (method = eCheck, positive
    /// Payamt, ADN transaction id stamped) with NO Settlement row — booked money invisible to
    /// the sweep's settle/return/watchdog machinery. Expected count every run: 0. Report-only.
    /// </summary>
    Task<List<UntrackedEcheckRaDto>> GetUntrackedEcheckAccountingAsync(
        Guid echeckPaymentMethodId, CancellationToken ct = default);
}

/// <summary>
/// Integrity-net finding: an eCheck accounting row whose Settlement (return-watcher) row is
/// missing. Digest/report shape only — never persisted.
/// </summary>
public sealed record UntrackedEcheckRaDto
{
    public required int AId { get; init; }
    public required string? AdnTransactionId { get; init; }
    public required decimal Payamt { get; init; }
    public required DateTime? Createdate { get; init; }
}
