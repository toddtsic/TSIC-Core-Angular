namespace TSIC.Contracts.Dtos.DiscountCode;

/// <summary>
/// DTO representing a discount code with computed status fields.
/// </summary>
public record DiscountCodeDto
{
    public required int Ai { get; init; }
    public required string CodeName { get; init; }
    public required string DiscountType { get; init; } // "Percentage" or "DollarAmount"
    public required decimal Amount { get; init; }
    public required int UsageCount { get; init; }
    public required DateTime StartDate { get; init; }
    public required DateTime EndDate { get; init; }
    public required bool IsActive { get; init; }
    public required bool IsExpired { get; init; } // Computed: EndDate < now
    public required DateTime Modified { get; init; }
}
