namespace TSIC.Contracts.Payments;

/// <summary>
/// Raw per-method sums of RegistrationAccounting.Payamt for one entity
/// (registration or team). Method semantics match what's actually stored:
///   CreditCard — gross (principal + proc when proc enabled)
///   Echeck     — principal only (proc handled at write time)
///   Check / Cash / Correction — principal only (no proc)
/// </summary>
public record PaymentMethodTotals
{
    public required decimal CreditCard { get; init; }
    public required decimal Echeck { get; init; }
    public required decimal Check { get; init; }
    public required decimal Cash { get; init; }
    public required decimal Correction { get; init; }

    /// <summary>
    /// Net money this entity has paid: the sum of all five method buckets. This is the
    /// figure written into entity.PaidTotal (mirrors <see cref="PaymentState.GrossPaid"/>),
    /// so a PaidTotal recomputed from the ledger reads <c>totals.GrossPaid</c> directly.
    /// Refunds/voids already net in via negative/zeroed Payamt rows.
    /// </summary>
    public decimal GrossPaid => CreditCard + Echeck + Check + Cash + Correction;

    public static readonly PaymentMethodTotals Zero = new()
    {
        CreditCard = 0m, Echeck = 0m, Check = 0m, Cash = 0m, Correction = 0m,
    };
}

/// <summary>
/// Discriminator for the unified GetPaymentTotalsByEntityAsync repo method —
/// payments are tagged with RegistrationId, TeamId, or both, so reads filter
/// on whichever column applies for the consumer's entity kind.
/// </summary>
public enum PaymentEntityKind
{
    Registration = 1,
    Team = 2,
}
