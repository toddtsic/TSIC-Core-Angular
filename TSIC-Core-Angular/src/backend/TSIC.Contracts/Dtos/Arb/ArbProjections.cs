namespace TSIC.Contracts.Dtos.Arb;

/// <summary>
/// Repository projection: registration with ARB subscription + contact info.
/// </summary>
public record ArbRegistrationProjection
{
    public required Guid RegistrationId { get; init; }
    public required string SubscriptionId { get; init; }
    public string? SubscriptionStatus { get; init; }
    public DateTime? SubscriptionStartDate { get; init; }
    public int? BillingOccurrences { get; init; }
    public decimal? AmountPerOccurrence { get; init; }
    public int? IntervalLength { get; init; }
    /// <summary>Sort-order name ("Last, First") — this is the GRID column format.</summary>
    public required string RegistrantName { get; init; }

    /// <summary>
    /// First/Last carried separately so prose surfaces (the !PLAYER email token) can render natural
    /// order without splitting <see cref="RegistrantName"/> on ", " — a parse that breaks on suffixes
    /// and on any name that legitimately contains a comma.
    /// </summary>
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? Assignment { get; init; }
    public string? FamilyUsername { get; init; }
    public string? Role { get; init; }
    public string? RegistrantEmail { get; init; }
    public string? MomName { get; init; }
    public string? MomEmail { get; init; }
    public string? MomPhone { get; init; }
    public string? DadName { get; init; }
    public string? DadEmail { get; init; }
    public string? DadPhone { get; init; }
    public decimal FeeTotal { get; init; }
    public decimal PaidTotal { get; init; }
    public decimal OwedTotal { get; init; }
    public required string JobName { get; init; }
    public required string JobPath { get; init; }
    public Guid JobId { get; init; }

    /// <summary>Unsubscribe flag — the engine suppresses opted-out registrants uniformly.</summary>
    public bool BemailOptOut { get; init; }

    /// <summary>
    /// Createdate of the most recent NON-SETTLING ARB draft booked for this registration. The sweep
    /// writes one row per declined / generalError transaction (payamt 0, ARB paymeth, registration
    /// totals untouched), so this is durable evidence that a scheduled installment is KNOWN to have
    /// failed - not merely still in flight. It is what lets the balance math overrule its own
    /// 48-hour grace window. See ArbDefensiveService.CalculateOwedNow.
    /// </summary>
    public DateTime? LastFailedDraftDate { get; init; }

    /// <summary>
    /// Createdate of the most recent ARB draft booked for this registration of ANY outcome,
    /// settled or not. Where LastFailedDraftDate says "we know it failed", this says the weaker but
    /// sufficient "we know how it turned out" - which is all the 48-hour grace needs to stand down.
    /// The grace exists solely to cover the window where an installment may still be settling at ADN
    /// and PaidTotal has not caught up; once the draft is booked either way, that window is closed.
    /// </summary>
    public DateTime? LastArbDraftDate { get; init; }
}

/// <summary>
/// Single-registration deep detail for CC update flow.
/// </summary>
public record ArbRegistrationDetail
{
    public required Guid RegistrationId { get; init; }
    public required Guid JobId { get; init; }
    public required string SubscriptionId { get; init; }
    public string? SubscriptionStatus { get; init; }
    public DateTime? SubscriptionStartDate { get; init; }
    public int? BillingOccurrences { get; init; }
    public decimal? AmountPerOccurrence { get; init; }
    public int? IntervalLength { get; init; }
    public required string RegistrantName { get; init; }
    public required string JobName { get; init; }
    public decimal FeeTotal { get; init; }
    public decimal PaidTotal { get; init; }
    public string? FirstInvoiceNumber { get; init; }

    /// <summary>
    /// Createdate of the most recent NON-SETTLING ARB draft booked for this registration. The sweep
    /// writes one row per declined / generalError transaction (payamt 0, ARB paymeth, registration
    /// totals untouched), so this is durable evidence that a scheduled installment is KNOWN to have
    /// failed - not merely still in flight. It is what lets the balance math overrule its own
    /// 48-hour grace window. See ArbDefensiveService.CalculateOwedNow.
    /// </summary>
    public DateTime? LastFailedDraftDate { get; init; }

    /// <summary>
    /// Createdate of the most recent ARB draft booked for this registration of ANY outcome,
    /// settled or not. Where LastFailedDraftDate says "we know it failed", this says the weaker but
    /// sufficient "we know how it turned out" - which is all the 48-hour grace needs to stand down.
    /// The grace exists solely to cover the window where an installment may still be settling at ADN
    /// and PaidTotal has not caught up; once the draft is booked either way, that window is closed.
    /// </summary>
    public DateTime? LastArbDraftDate { get; init; }
}

/// <summary>
/// Minimal projection for the job-wide ARB status refresh: every registration in the
/// job with a subscription ID, regardless of bActive (an inactive registration can
/// still carry a live, billing subscription).
/// </summary>
public record ArbStatusRefreshTarget
{
    public required Guid RegistrationId { get; init; }
    public required string SubscriptionId { get; init; }
    public string? SubscriptionStatus { get; init; }
}

/// <summary>
/// Director contact info for notification emails.
/// </summary>
public record ArbDirectorProjection
{
    public required Guid JobId { get; init; }
    public required string Name { get; init; }
    public required string Email { get; init; }
}
