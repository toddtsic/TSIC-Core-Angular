namespace TSIC.Contracts.Dtos;

/// <summary>
/// One field the Schedule List Designer can place as a column. Returned by
/// GET /api/schedule-list/fields so the frontend never hard-codes the pool. Keys map
/// 1:1 to columns emitted by reporting_migrate.ScheduleList_Flat.
/// </summary>
public record ScheduleListFieldDto
{
    /// <summary>Column key, e.g. "time" / "home" / "homeScore".</summary>
    public required string Key { get; init; }

    /// <summary>Human label for the picker and the printed column header, e.g. "Home".</summary>
    public required string Label { get; init; }

    /// <summary>Default relative width (normalized against the page's content width at render time).</summary>
    public required int DefaultWidthWeight { get; init; }

    /// <summary>Default horizontal alignment: "Left" | "Right" | "Center".</summary>
    public required string DefaultAlign { get; init; }

    /// <summary>True when the column holds free text long enough to truncate or wrap (team names, field).</summary>
    public required bool SupportsLongText { get; init; }

    /// <summary>
    /// True for the two score columns. The request's <see cref="ScheduleListRequestDto.ScoreMode"/>
    /// governs whether these print the number, draw a blank write-in box, or are dropped.
    /// </summary>
    public required bool IsScore { get; init; }
}

/// <summary>
/// A single chosen column in a generate request — selection, order (list position),
/// width, alignment, and long-text handling.
/// </summary>
public record ScheduleListColumnDto
{
    /// <summary>Matches <see cref="ScheduleListFieldDto.Key"/>.</summary>
    public required string Key { get; init; }

    /// <summary>Relative width; normalized against the page content width across all chosen columns.</summary>
    public required int WidthWeight { get; init; }

    /// <summary>"Left" | "Right" | "Center".</summary>
    public required string Align { get; init; }

    /// <summary>"Truncate" | "Wrap" — how over-long text is handled. Ignored for non-text columns.</summary>
    public required string LongText { get; init; }

    /// <summary>Character cap when <see cref="LongText"/> = "Truncate". Defaults to 28 when null.</summary>
    public int? TruncateAt { get; init; }
}

/// <summary>
/// Full Schedule List Designer generate request. The single proc returns the superset of
/// game fields; this payload selects/orders the columns, groups + sorts the games, and
/// chooses how scores render.
/// </summary>
public record ScheduleListRequestDto
{
    /// <summary>Section grouping: "None" | "Day" | "Field" | "AgeGroup" | "Division".</summary>
    public required string GroupBy { get; init; }

    /// <summary>Sort within each group: "Time" | "Field" | "Team".</summary>
    public required string SortBy { get; init; }

    /// <summary>Ordered columns (list order = print order, left to right).</summary>
    public required IReadOnlyList<ScheduleListColumnDto> Columns { get; init; }

    /// <summary>
    /// How the score columns render: "Printed" (the recorded score), "Blank" (an empty
    /// write-in box — the officials' score sheet), or "Hidden" (score columns dropped).
    /// </summary>
    public required string ScoreMode { get; init; }

    /// <summary>Start each group section on a fresh page.</summary>
    public required bool PageBreakPerGroup { get; init; }

    /// <summary>Tint each group's section header with the age group's color.</summary>
    public required bool ColorAccent { get; init; }
}
