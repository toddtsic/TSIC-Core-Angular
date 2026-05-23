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
    /// Check whether any active payment records exist for the given team.
    /// </summary>
    Task<bool> HasPaymentsForTeamAsync(Guid teamId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all accounting records for a registration, joined with payment method.
    /// Sets CanRefund = true for CC payments with a transaction ID.
    /// Ordered by Createdate desc. AsNoTracking.
    /// </summary>
    Task<List<AccountingRecordDto>> GetByRegistrationIdAsync(Guid registrationId, CancellationToken ct = default);

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
