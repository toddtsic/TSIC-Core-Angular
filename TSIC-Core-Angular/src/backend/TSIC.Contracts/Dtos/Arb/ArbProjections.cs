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
    public required string RegistrantName { get; init; }
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
