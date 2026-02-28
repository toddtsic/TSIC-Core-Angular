namespace TSIC.Contracts.Dtos.AgeRange;

public record CreateAgeRangeRequest
{
    public required string RangeName { get; init; }
    public required DateTime RangeLeft { get; init; }
    public required DateTime RangeRight { get; init; }
}
