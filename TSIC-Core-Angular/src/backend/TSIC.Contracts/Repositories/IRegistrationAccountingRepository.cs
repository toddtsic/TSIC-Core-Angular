using TSIC.Contracts.Dtos.RegistrationSearch;
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

    /// <summary>
    /// Get payment summaries (total payments and non-CC payments) for a batch of registrations.
    /// Used for fee recalculation — processing fees apply only to the CC-payable portion.
    /// </summary>
    Task<Dictionary<Guid, PaymentSummary>> GetPaymentSummariesAsync(
        IReadOnlyCollection<Guid> registrationIds,
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
}

public record PaymentSummary
{
    public required decimal TotalPayments { get; init; }
    public required decimal NonCcPayments { get; init; }
}
