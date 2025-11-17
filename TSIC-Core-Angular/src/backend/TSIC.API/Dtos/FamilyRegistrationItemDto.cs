namespace TSIC.API.Dtos;

public sealed class FamilyRegistrationItemDto
{
    public Guid RegistrationId { get; init; }
    public string PlayerId { get; init; } = string.Empty;
    public string? PlayerFirstName { get; init; }
    public string? PlayerLastName { get; init; }
    public Guid JobId { get; init; }
    public string JobPath { get; init; } = string.Empty;
    public Guid? AssignedTeamId { get; init; }
    public string? AssignedTeamName { get; init; }
    public DateTime Modified { get; init; }

    // Useful profile bits
    public string? GradYear { get; init; }
    public string? SportAssnId { get; init; }

    // Fees (per registration)
    public decimal FeeBase { get; init; }
    public decimal FeeDiscount { get; init; }
    public decimal FeeDiscountMp { get; init; }
    public decimal FeeDonation { get; init; }
    public decimal FeeLatefee { get; init; }
    public decimal FeeProcessing { get; init; }
    public decimal FeeTotal { get; init; }
    public decimal OwedTotal { get; init; }
    public decimal PaidTotal { get; init; }
}
