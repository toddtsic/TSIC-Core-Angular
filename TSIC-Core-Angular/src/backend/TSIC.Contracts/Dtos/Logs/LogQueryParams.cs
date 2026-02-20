namespace TSIC.Contracts.Dtos.Logs;

public record LogQueryParams
{
    public string? Level { get; init; }
    public string? Search { get; init; }
    public DateTimeOffset? From { get; init; }
    public DateTimeOffset? To { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 50;
}
