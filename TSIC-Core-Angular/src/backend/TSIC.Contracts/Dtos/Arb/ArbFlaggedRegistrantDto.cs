namespace TSIC.Contracts.Dtos.Arb;

public record ArbFlaggedRegistrantDto
{
    public required Guid RegistrationId { get; init; }
    public required string SubscriptionId { get; init; }
    public required string SubscriptionStatus { get; init; }
    public required ArbFlagType FlagType { get; init; }

    // Registrant
    public required string RegistrantName { get; init; }
    public string? Assignment { get; init; }
    public string? FamilyUsername { get; init; }
    public string? Role { get; init; }
    public string? RegistrantEmail { get; init; }

    // Contact — Mom
    public string? MomName { get; init; }
    public string? MomEmail { get; init; }
    public string? MomPhone { get; init; }

    // Contact — Dad
    public string? DadName { get; init; }
    public string? DadEmail { get; init; }
    public string? DadPhone { get; init; }

    // Financials
    public decimal FeeTotal { get; init; }
    public decimal PaidTotal { get; init; }
    public decimal CurrentlyOwes { get; init; }
    public decimal OwedTotal { get; init; }

    // Schedule
    public DateTime? NextPaymentDate { get; init; }
    public string? PaymentProgress { get; init; }

    // Job context
    public required string JobName { get; init; }
    public required string JobPath { get; init; }
}
