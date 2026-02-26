namespace TSIC.Contracts.Dtos.Scheduling;

public record MasterScheduleResponse
{
    public required List<MasterScheduleDay> Days { get; init; }
    public required List<string> FieldColumns { get; init; }
    public required int TotalGames { get; init; }
}

public record MasterScheduleDay
{
    public required string DayLabel { get; init; }
    public required string ShortLabel { get; init; }
    public required int GameCount { get; init; }
    public required List<MasterScheduleRow> Rows { get; init; }
}

public record MasterScheduleRow
{
    public required string TimeLabel { get; init; }
    public required DateTime SortKey { get; init; }
    public required List<MasterScheduleCell?> Cells { get; init; }
}

public record MasterScheduleCell
{
    public required int Gid { get; init; }
    public required string T1Name { get; init; }
    public required string T2Name { get; init; }
    public required string AgDiv { get; init; }
    public required string? Color { get; init; }
    public required string? ContrastColor { get; init; }
    public required int? T1Score { get; init; }
    public required int? T2Score { get; init; }
    public required string? T1Ann { get; init; }
    public required string? T2Ann { get; init; }
    public required int? GStatusCode { get; init; }
    public required List<string>? Referees { get; init; }
}

public record MasterScheduleExportRequest
{
    public required bool IncludeReferees { get; init; }
    public required int? DayIndex { get; init; }
}
