namespace TSIC.Contracts.Dtos;

/// <summary>
/// One active player carrying a non-zero discount, for the "Discounted Players (Excel)" export —
/// the EF replacement for the <c>reporting_migrate.Get_DiscountedPlayers</c> proc. Job-scoped. The
/// discount-code fields are null when the discount was not applied through a code.
/// </summary>
public record DiscountedPlayerExcelRowDto
{
    public string? LastName { get; init; }
    public string? FirstName { get; init; }
    public string? AgegroupName { get; init; }
    public string? TeamName { get; init; }
    public string? MomFirstName { get; init; }
    public string? MomEmail { get; init; }
    public string? MomCellphone { get; init; }
    public string? MomLastName { get; init; }
    public string? DadFirstName { get; init; }
    public string? DadLastName { get; init; }
    public string? DadEmail { get; init; }
    public string? DadCellphone { get; init; }
    public required decimal FeeDiscount { get; init; }
    public string? CodeName { get; init; }
    public bool? CodeActive { get; init; }
    public DateTime? CodeStartDate { get; init; }
    public DateTime? CodeEndDate { get; init; }
}
