namespace TSIC.Contracts.Dtos;

public sealed class FamilyRegistrationItemDto
{
    public required Guid RegistrationId { get; init; }
    public required string PlayerId { get; init; } = string.Empty;
    public string? PlayerFirstName { get; init; }
    public string? PlayerLastName { get; init; }
    public required Guid JobId { get; init; }
    public required string JobPath { get; init; } = string.Empty;
    public Guid? AssignedTeamId { get; init; }
    public string? AssignedTeamName { get; init; }
    public required DateTime Modified { get; init; }

    // Useful profile bits
    public string? GradYear { get; init; }
    public string? SportAssnId { get; init; }

    // Fees (per registration)
    public required decimal FeeBase { get; init; }
    public required decimal FeeDiscount { get; init; }
    public required decimal FeeDiscountMp { get; init; }
    public required decimal FeeDonation { get; init; }
    public required decimal FeeLatefee { get; init; }
    public required decimal FeeProcessing { get; init; }
    public required decimal FeeTotal { get; init; }
    public required decimal OwedTotal { get; init; }
    public required decimal PaidTotal { get; init; }
}
