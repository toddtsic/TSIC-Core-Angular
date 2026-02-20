namespace TSIC.Contracts.Dtos.Logs;

public record LogStatsDto
{
    public required List<LogCountByHour> CountsByHour { get; init; }
    public required List<LogCountByHourByStatus> CountsByHourByStatus { get; init; }
    public required Dictionary<string, int> CountsByLevel { get; init; }
    public required List<TopErrorDto> TopErrors { get; init; }
    public required int TotalCount { get; init; }
}

public record LogCountByHourByStatus
{
    public required DateTimeOffset Hour { get; init; }
    public required int Count { get; init; }
    public required string StatusRange { get; init; }
}

public record LogCountByHour
{
    public required DateTimeOffset Hour { get; init; }
    public required int Count { get; init; }
    public required string Level { get; init; }
}

public record TopErrorDto
{
    public required string Message { get; init; }
    public required int Count { get; init; }
    public required DateTimeOffset LastSeen { get; init; }
}
