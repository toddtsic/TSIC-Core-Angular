namespace TSIC.Contracts.Dtos;

/// <summary>
/// Which gateway operation actually reversed the money. Authorize.Net has two, and which one
/// applies is decided by the original transaction's settlement state, never by the caller.
/// </summary>
public enum AdnReversalKind
{
    /// <summary>Nothing was reversed — the result carries the reason.</summary>
    None = 0,

    /// <summary>
    /// The charge had not settled yet, so it was VOIDED. A void is always for the FULL original
    /// amount — Authorize.Net offers no partial void — so a partial request becomes a full
    /// reversal. Callers must book <see cref="AdnReversalResult.ReversedAmount"/>, not the amount
    /// they asked for.
    /// </summary>
    Void = 1,

    /// <summary>The charge had settled, so a refund was issued for the requested amount.</summary>
    Refund = 2
}

/// <summary>
/// Where a charge stands at Authorize.Net, which is what decides how it can be reversed.
/// </summary>
public enum AdnChargeStatus
{
    /// <summary>The gateway could not be asked, or did not recognise the transaction.</summary>
    Unknown = 0,

    /// <summary>
    /// Captured but not settled. Reversible ONLY by a full void — so a UI must not offer a partial
    /// refund against it, and a caller asking for one will get a full reversal.
    /// </summary>
    Unsettled = 1,

    /// <summary>Settled. Reversible by a refund, partial or full.</summary>
    Settled = 2,

    /// <summary>
    /// A real transaction in a state that supports neither (already voided, declined, expired).
    /// </summary>
    NotReversible = 3
}

/// <summary>
/// Reverse a card charge at Authorize.Net. Carries only what the gateway needs; where the money
/// is booked afterwards is the caller's business.
/// </summary>
public sealed record AdnReversalRequest
{
    /// <summary>Job whose merchant credentials process the reversal.</summary>
    public required Guid JobId { get; init; }

    /// <summary>
    /// The ORIGINAL charge's Authorize.Net transaction id. Nullable because callers read it
    /// straight off an accounting row where it may be absent (a check payment, say); the service
    /// rejects that case, so no caller needs its own null guard.
    /// </summary>
    public required string? AdnTransactionId { get; init; }

    /// <summary>
    /// What the original charge actually collected. The requested amount is validated against
    /// this, and a void reverses exactly this.
    /// </summary>
    public required decimal OriginalPaidAmount { get; init; }

    /// <summary>How much to reverse. Must be greater than zero and no more than the original.</summary>
    public required decimal RequestedAmount { get; init; }

    /// <summary>Last four of the card, required by ADN to match a settled refund to its charge.</summary>
    public string? CardLast4 { get; init; }

    public string? CardExpiry { get; init; }

    public string? InvoiceNumber { get; init; }
}

/// <summary>
/// Outcome of a reversal attempt. On failure <see cref="Message"/> is safe to show a director —
/// it carries the gateway's own wording rather than a generic substitute.
/// </summary>
public sealed record AdnReversalResult
{
    public required bool Success { get; init; }
    public required AdnReversalKind Kind { get; init; }
    public required string Message { get; init; }

    /// <summary>The NEW transaction id from the void or refund. Null on failure.</summary>
    public string? TransactionId { get; init; }

    /// <summary>
    /// What was actually reversed: the requested amount on a refund, the FULL original payment on
    /// a void. Book this figure — never the requested amount — or a void of a partial request
    /// leaves the ledger overstating what the customer still paid.
    /// </summary>
    public decimal ReversedAmount { get; init; }

    public static AdnReversalResult Failed(string message) =>
        new() { Success = false, Kind = AdnReversalKind.None, Message = message };
}
