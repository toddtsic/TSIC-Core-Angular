namespace TSIC.Contracts.Dtos.AgeRange;

/// <summary>
/// Age range data for admin management view.
/// </summary>
public record AgeRangeDto
{
    public required int AgeRangeId { get; init; }
    public required string RangeName { get; init; }
    public required DateTime RangeLeft { get; init; }
    public required DateTime RangeRight { get; init; }
    public required DateTime Modified { get; init; }
    public string? ModifiedByUsername { get; init; }
}
