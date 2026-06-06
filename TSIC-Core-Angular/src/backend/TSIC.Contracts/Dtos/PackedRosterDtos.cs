namespace TSIC.Contracts.Dtos;

/// <summary>
/// One field the PackedRoster Designer can place as a player-row column. Returned by
/// GET /api/packed-roster/fields so the frontend never hard-codes the pool. Keys map
/// 1:1 to columns emitted by reporting_migrate.TournamentRosterPacked_Flat.
/// </summary>
public record PackedRosterFieldDto
{
    /// <summary>Proc column key, e.g. "uniform_no" / "collegeCommit".</summary>
    public required string Key { get; init; }

    /// <summary>Human label for the picker, e.g. "Uniform #".</summary>
    public required string Label { get; init; }

    /// <summary>Default relative width (normalized against the card's inner width at render time).</summary>
    public required int DefaultWidthWeight { get; init; }

    /// <summary>Default horizontal alignment: "Left" | "Right" | "Center".</summary>
    public required string DefaultAlign { get; init; }

    /// <summary>True when the column holds free text that can be truncated or wrapped (school / college commit).</summary>
    public required bool SupportsLongText { get; init; }
}

/// <summary>
/// A single chosen column in a generate request — selection, order (list position),
/// width, alignment, and long-text handling.
/// </summary>
public record PackedRosterColumnDto
{
    /// <summary>Matches <see cref="PackedRosterFieldDto.Key"/>.</summary>
    public required string Key { get; init; }

    /// <summary>Relative width; normalized against the card's inner width across all chosen columns.</summary>
    public required int WidthWeight { get; init; }

    /// <summary>"Left" | "Right" | "Center".</summary>
    public required string Align { get; init; }

    /// <summary>"Truncate" | "Wrap" — how over-long text is handled. Ignored for non-text columns.</summary>
    public required string LongText { get; init; }

    /// <summary>Character cap when <see cref="LongText"/> = "Truncate". Defaults to 14 when null.</summary>
    public int? TruncateAt { get; init; }
}

/// <summary>
/// Full PackedRoster Designer generate request. The single proc returns the superset of
/// fields; this payload selects/orders the columns and toggles the card chrome.
/// </summary>
public record PackedRosterRequestDto
{
    /// <summary>Cards per page-row: 2 or 3.</summary>
    public required int NUp { get; init; }

    /// <summary>Ordered player-row columns (list order = print order, left to right).</summary>
    public required IReadOnlyList<PackedRosterColumnDto> Columns { get; init; }

    /// <summary>Include coaches (Staff) as inline rows at the top of each card.</summary>
    public required bool ShowCoaches { get; init; }

    /// <summary>Club-rep line: include the rep's name.</summary>
    public required bool ShowRepName { get; init; }

    /// <summary>Club-rep line: include the rep's email.</summary>
    public required bool ShowRepEmail { get; init; }

    /// <summary>Club-rep line: include the rep's phone.</summary>
    public required bool ShowRepPhone { get; init; }

    /// <summary>
    /// When true, the school column shows the player's college commit (when committed)
    /// and the "* " committed-player marker on the name is dropped.
    /// </summary>
    public required bool SchoolShowsCommit { get; init; }

    /// <summary>
    /// Append the player's OWN club affiliation to the name ("NAME / CLUB"), reproducing the
    /// legacy PackedByPosition look (proc <c>JobRosters_Get_Teamplayers_Withcoach</c>). False is
    /// the "No Club Players" sibling (plain names). Staff rows are never affiliated.
    /// </summary>
    public required bool ShowClubAffiliation { get; init; }

    /// <summary>
    /// Within-card player order: "Uniform" (default) | "Position" | "Name". Staff always sort
    /// first. "Position" reproduces the by-position grouping of the PackedByPosition family.
    /// </summary>
    public required string SortBy { get; init; }
}
