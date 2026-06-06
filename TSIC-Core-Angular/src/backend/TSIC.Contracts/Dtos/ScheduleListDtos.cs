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
/// One flat row per scheduled game — the EF (<c>GetScheduleListGamesAsync</c>) replacement for
/// <c>reporting_migrate.ScheduleList_Flat</c>. Raw superset: denormalized AgegroupName/DivName
/// and the team name/type/ann/score fields come straight off <c>Schedule</c>; LeagueName /
/// FieldName / Color are the joined lookups; ClubRep first/last are each side's club rep. The
/// PDF layer owns all display shaping — the proc's TBD-slot bracket CASE (Finals/Semis/Quarters/
/// R16 + annotation), score→string, and rep "First Last" concat are applied in C#, not here.
/// </summary>
public record ScheduleListGameDto
{
    public required int Gid { get; init; }

    public string? AgegroupName { get; init; }
    public string? DivName { get; init; }
    public string? LeagueName { get; init; }
    public string? FieldName { get; init; }

    /// <summary>Age group accent color (hex like "#RRGGBB"); not denormalized on Schedule.</summary>
    public string? Color { get; init; }

    public DateTime? GDate { get; init; }

    /// <summary>Null = a TBD bracket slot (use Type + Ann for the label); else a real team.</summary>
    public Guid? T1Id { get; init; }
    public string? T1Name { get; init; }
    public string? T1Type { get; init; }
    public string? T1Ann { get; init; }
    public int? T1Score { get; init; }

    public Guid? T2Id { get; init; }
    public string? T2Name { get; init; }
    public string? T2Type { get; init; }
    public string? T2Ann { get; init; }
    public int? T2Score { get; init; }

    public string? ClubRep1First { get; init; }
    public string? ClubRep1Last { get; init; }
    public string? ClubRep2First { get; init; }
    public string? ClubRep2Last { get; init; }
}

/// <summary>
/// Full Schedule List Designer generate request. The flat game dataset carries the superset of
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
