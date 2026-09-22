using TSIC.Contracts.Dtos.RegistrationSearch;
using TSIC.Contracts.Payments;
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
    /// Atomically record a payment ledger row AND re-derive the keyed entity's PaidTotal
    /// from the ledger, so the stored total can never drift from the accounting rows that
    /// back it. PaidTotal becomes the sum of the entity's active Payamt across the five
    /// payment buckets (refunds/voids already netted in via negative/zeroed rows); OwedTotal
    /// is recomputed via RecalcTotals. A team-tagged row (TeamId set) reconciles the Team;
    /// otherwise the Registration. The row write and the recompute share one transaction —
    /// joining the caller's ambient transaction if one is already open — so the ledger row
    /// and the total it implies always commit together or not at all.
    ///
    /// Scope is the directly keyed entity only. For a team row, the owning club rep's
    /// rolled-up financials remain the caller's responsibility (SynchronizeClubRepFinancials),
    /// because that aggregate deliberately excludes WAITLIST/DROPPED agegroups and so cannot
    /// be reproduced by a flat sum here.
    /// </summary>
    Task RecordPaymentAndRecomputeAsync(RegistrationAccounting row, string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Persist changes to the database.
    /// </summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the most recent Authorize.Net transaction ID for the given registration IDs.
    /// </summary>
    Task<string?> GetLatestAdnTransactionIdAsync(IEnumerable<Guid> registrationIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns true iff any RegistrationAccounting row already references this Authorize.Net
    /// transaction ID. Used by the daily sweep to skip already-imported transactions.
    /// </summary>
    Task<bool> AnyByAdnTransactionIdAsync(string adnTransactionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Per-entity, per-method sums of RegistrationAccounting.Payamt. Single read
    /// surface for all payment-state consumers (recalc, display, per-payment writers).
    /// kind=Registration filters by RegistrationId; kind=Team filters by TeamId.
    /// Entities with zero payments are absent from the dictionary; callers default
    /// to <see cref="PaymentMethodTotals.Zero"/>.
    /// </summary>
    Task<Dictionary<Guid, PaymentMethodTotals>> GetPaymentTotalsByEntityAsync(
        PaymentEntityKind kind,
        IReadOnlyCollection<Guid> entityIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Per-entity payment rows in chronological order (Createdate, then AId), classified
    /// into the same five buckets as <see cref="GetPaymentTotalsByEntityAsync"/> (same
    /// Active filter, same excluded methods — summing a result's amounts per bucket
    /// reproduces the totals exactly). Powers the slice-aware PaymentState hydration
    /// walk on proc-on-balance-only jobs, which needs payment ORDER to know how much
    /// CC/eCheck gross paid the proc-free deposit slice. Entities with zero payments
    /// are absent from the dictionary.
    /// </summary>
    Task<Dictionary<Guid, List<PaymentLedgerRow>>> GetPaymentRowsByEntityAsync(
        PaymentEntityKind kind,
        IReadOnlyCollection<Guid> entityIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Check whether any active payment records exist for the given team.
    /// </summary>
    Task<bool> HasPaymentsForTeamAsync(Guid teamId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all accounting records for a registration, joined with payment method.
    /// Sets CanRefund = true for CC payments with a transaction ID.
    /// Ordered by Createdate desc. AsNoTracking.
    ///
    /// ⚠ This is the PAYER-keyed read, and it is correct for PLAYER and FAMILY money only —
    /// that money is written with a RegistrationId and no TeamId, so the payer is the only key
    /// it has. It is the WRONG read for a club rep: team money carries both keys, and a team can
    /// be moved to another club rep (<c>LadtService.MoveTeamToClubAsync</c>) without its ledger
    /// rows being rewritten. A club-rep screen built on this read shows the new rep an EMPTY
    /// ledger beside a correct balance. Use <see cref="GetClubRepLedgerAsync"/> there.
    /// </summary>
    Task<List<AccountingRecordDto>> GetByRegistrationIdAsync(Guid registrationId, CancellationToken ct = default);

    /// <summary>
    /// The club rep ledger: every row belonging to the teams this rep CURRENTLY holds, plus any
    /// row tendered by the rep that carries no team at all. Rows follow the TEAM, which is what a
    /// club rep's account is made of — their fees are the sum of their teams, so their ledger has
    /// to be the sum of their teams' payments or the two halves of the screen disagree.
    ///
    /// <c>RegistrationID</c> is deliberately NOT rewritten when a team moves. The row stays a
    /// truthful record of who paid, and any refund follows its <c>adnTransactionID</c> back to the
    /// card that was actually charged. Where the payer differs from <paramref name="clubRepRegistrationId"/>,
    /// the row carries <see cref="AccountingRecordDto.PaidByClubName"/> so it can explain itself
    /// on screen instead of appearing unattributed.
    ///
    /// The two halves cannot overlap (one requires a TeamId, the other requires none), so there is
    /// nothing to de-duplicate. Ordered by Createdate desc. AsNoTracking.
    /// </summary>
    /// <param name="clubRepRegistrationId">The rep whose ledger this is.</param>
    /// <param name="teamIds">That rep's current teams — waitlisted, dropped and inactive included,
    /// since their payments are still theirs to see.</param>
    Task<List<AccountingRecordDto>> GetClubRepLedgerAsync(
        Guid clubRepRegistrationId,
        IReadOnlyCollection<Guid> teamIds,
        CancellationToken ct = default);

    /// <summary>
    /// Get a single accounting record by AId (tracked, for refund operations).
    /// Includes Registration navigation for financial recalculation.
    /// </summary>
    Task<RegistrationAccounting?> GetByAIdAsync(int aId, CancellationToken ct = default);

    /// <summary>
    /// Get all payment method options for the create-accounting dropdown. AsNoTracking.
    /// </summary>
    Task<List<PaymentMethodOptionDto>> GetPaymentMethodOptionsAsync(CancellationToken ct = default);

    /// <summary>
    /// Get all accounting records for a team, joined with payment method.
    /// Sets CanRefund = true for CC payments with a transaction ID.
    /// Ordered by Createdate desc. AsNoTracking.
    /// </summary>
    Task<List<AccountingRecordDto>> GetByTeamIdAsync(Guid teamId, CancellationToken ct = default);

    /// <summary>
    /// Delete all accounting records for a registration. Dev/test only.
    /// </summary>
    Task DeleteByRegistrationIdAsync(Guid registrationId, CancellationToken ct = default);
}
