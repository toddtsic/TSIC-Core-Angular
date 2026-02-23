namespace TSIC.Contracts.Dtos.Arb;

public record ArbSubscriptionInfoDto
{
    public required string SubscriptionId { get; init; }
    public required string SubscriptionStatus { get; init; }
    public required decimal ChargePerOccurrence { get; init; }
    public required decimal BalanceDue { get; init; }
    public required string RegistrantName { get; init; }
    public required string JobName { get; init; }
    public required DateTime StartDate { get; init; }
    public required int TotalOccurrences { get; init; }
    public required int IntervalMonths { get; init; }
}

public record ArbUpdateCcRequest
{
    public required Guid RegistrationId { get; init; }
    public required string SubscriptionId { get; init; }
    public required string CardNumber { get; init; }
    public required string CardCode { get; init; }
    public required string ExpirationMonth { get; init; }
    public required string ExpirationYear { get; init; }
    public required string FirstName { get; init; }
    public required string LastName { get; init; }
    public required string Address { get; init; }
    public required string Zip { get; init; }
    public required string Email { get; init; }
    public required decimal BalanceDue { get; init; }
}

public record ArbUpdateCcResultDto
{
    public required bool SubscriptionUpdated { get; init; }
    public required bool BalanceCharged { get; init; }
    public decimal AmountCharged { get; init; }
    public string? TransactionId { get; init; }
    public required string Message { get; init; }
}
