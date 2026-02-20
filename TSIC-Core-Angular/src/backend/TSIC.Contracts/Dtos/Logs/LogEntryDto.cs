namespace TSIC.Contracts.Dtos.Logs;

public record LogEntryDto
{
    public required long Id { get; init; }
    public required DateTimeOffset TimeStamp { get; init; }
    public required string Level { get; init; }
    public required string Message { get; init; }
    public string? Exception { get; init; }
    public string? SourceContext { get; init; }
    public string? RequestPath { get; init; }
    public int? StatusCode { get; init; }
    public double? Elapsed { get; init; }
}
