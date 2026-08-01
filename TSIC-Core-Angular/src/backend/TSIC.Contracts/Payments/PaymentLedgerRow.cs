namespace TSIC.Contracts.Payments;

/// <summary>
/// Canonical payment bucket a ledger row sums into — the same five buckets
/// <see cref="PaymentMethodTotals"/> carries, resolved from PaymentMethodId
/// via <see cref="PaymentMethodIds.Classify"/>.
/// </summary>
public enum PaymentMethodBucket
{
    CreditCard = 1,
    Echeck = 2,
    Check = 3,
    Cash = 4,
    Correction = 5,
}

/// <summary>
/// One RegistrationAccounting row in chronological order, pre-classified into its
/// canonical bucket. Powers the slice-aware PaymentState hydration walk: on a
/// proc-on-balance-only job the walk replays rows oldest-first to determine how
/// much CC/eCheck gross paid the proc-free deposit slice (billing order is
/// deposit-first, so the oldest dollars are deposit dollars regardless of tender).
/// </summary>
public record PaymentLedgerRow
{
    public required decimal Amount { get; init; }
    public required PaymentMethodBucket Bucket { get; init; }
    public required DateTime? Createdate { get; init; }
    /// <summary>Identity tiebreaker for rows sharing a Createdate (insert order).</summary>
    public required int AId { get; init; }
}
