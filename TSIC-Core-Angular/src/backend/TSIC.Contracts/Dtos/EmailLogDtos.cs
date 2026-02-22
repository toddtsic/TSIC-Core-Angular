namespace TSIC.Contracts.Dtos;

public record EmailLogSummaryDto
{
    public required int EmailId { get; init; }
    public required DateTime SendTs { get; init; }
    public string? SendFrom { get; init; }
    public int? Count { get; init; }
    public string? Subject { get; init; }
}

public record EmailLogDetailDto
{
    public required int EmailId { get; init; }
    public string? SendTo { get; init; }
    public string? Msg { get; init; }
}
