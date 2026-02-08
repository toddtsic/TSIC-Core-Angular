namespace TSIC.Contracts.Dtos.Ladt;

public record DivisionDetailDto
{
    public required Guid DivId { get; init; }
    public required Guid AgegroupId { get; init; }
    public string? DivName { get; init; }
    public int? MaxRoundNumberToShow { get; init; }
}

public record CreateDivisionRequest
{
    public required Guid AgegroupId { get; init; }
    public required string DivName { get; init; }
    public int? MaxRoundNumberToShow { get; init; }
}

public record UpdateDivisionRequest
{
    public string? DivName { get; init; }
    public int? MaxRoundNumberToShow { get; init; }
}
