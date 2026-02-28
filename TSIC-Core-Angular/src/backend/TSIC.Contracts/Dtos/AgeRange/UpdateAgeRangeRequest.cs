namespace TSIC.Contracts.Dtos.AgeRange;

public record UpdateAgeRangeRequest
{
    public required string RangeName { get; init; }
    public required DateTime RangeLeft { get; init; }
    public required DateTime RangeRight { get; init; }
}
