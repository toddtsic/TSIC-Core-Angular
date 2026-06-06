namespace TSIC.Contracts.Dtos;

/// <summary>
/// One flat row per registrant for the Roster Table Designer — the EF replacement for the
/// legacy wide-roster Crystal family (Club Rosters, No-Medical, Coaches, WithClubRep, STEPS,
/// Recruiting roster). Raw denormalized superset: the union of every column those procs emit
/// (player identity + contact, both parents, academics, medical, fees, equipment, sport-assn,
/// club rep). The PDF layer picks/orders columns per the request and does all display shaping
/// (name composition, phone formatting, money/date formatting). Redundant fields are expected.
/// </summary>
public record RosterTableRowDto
{
    public required Guid RegistrationId { get; init; }
    public string? RoleName { get; init; }

    // ── Team / grouping ──
    public string? LeagueName { get; init; }
    public string? AgegroupName { get; init; }
    public string? DivName { get; init; }
    public string? TeamName { get; init; }
    public string? ClubName { get; init; }
    public string? ClubTeamName { get; init; }

    /// <summary>Age group accent color (hex like "#RRGGBB") for the optional section tint.</summary>
    public string? Color { get; init; }

    // ── Player identity ──
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? Gender { get; init; }
    public DateTime? Dob { get; init; }

    // ── Assignment / academics ──
    public string? UniformNo { get; init; }
    public string? Position { get; init; }
    public string? SchoolName { get; init; }
    public string? SchoolGrade { get; init; }
    public string? GradYear { get; init; }
    public string? Gpa { get; init; }
    public string? SatMath { get; init; }
    public string? SatVerbal { get; init; }
    public string? SatWriting { get; init; }
    public string? Act { get; init; }

    // ── Camp grouping (Registrations) ──
    public string? DayGroup { get; init; }
    public string? NightGroup { get; init; }
    /// <summary>Roommate preference (entity <c>RoommatePref</c>) — the camp_roomies grouping key.</summary>
    public string? Roommate { get; init; }

    // ── Player contact ──
    public string? Email { get; init; }
    public string? Cellphone { get; init; }
    public string? StreetAddress { get; init; }
    public string? City { get; init; }
    public string? State { get; init; }
    public string? PostalCode { get; init; }

    // ── Parents (Families) ──
    public string? MomFirstName { get; init; }
    public string? MomLastName { get; init; }
    public string? MomEmail { get; init; }
    public string? MomCellphone { get; init; }
    public string? DadFirstName { get; init; }
    public string? DadLastName { get; init; }
    public string? DadEmail { get; init; }
    public string? DadCellphone { get; init; }

    // ── Medical / fees ──
    public string? MedicalNote { get; init; }
    public decimal PaidTotal { get; init; }
    public decimal OwedTotal { get; init; }

    // ── Equipment (camp / STEPS) ──
    public string? JerseySize { get; init; }
    public string? ShortsSize { get; init; }
    public string? Kilt { get; init; }
    public string? TShirt { get; init; }
    public string? Reversible { get; init; }
    public string? Gloves { get; init; }
    public string? Shoes { get; init; }

    // ── Sport association (US Lacrosse # etc.) ──
    public string? SportAssnId { get; init; }
    public DateTime? SportAssnIdexpDate { get; init; }

    // ── Club rep (per assigned team) ──
    public string? ClubRepFirstName { get; init; }
    public string? ClubRepLastName { get; init; }
    public string? ClubRepEmail { get; init; }
    public string? ClubRepCellphone { get; init; }
}

/// <summary>
/// One field the Roster Table Designer can place as a column. Returned by
/// GET /api/roster-table/fields so the frontend never hard-codes the pool.
/// </summary>
public record RosterTableFieldDto
{
    /// <summary>Column key, e.g. "player" / "uniform" / "medical".</summary>
    public required string Key { get; init; }

    /// <summary>Human label for the picker and the printed column header.</summary>
    public required string Label { get; init; }

    /// <summary>Default relative width (normalized against the page's content width at render time).</summary>
    public required int DefaultWidthWeight { get; init; }

    /// <summary>Default horizontal alignment: "Left" | "Right" | "Center".</summary>
    public required string DefaultAlign { get; init; }

    /// <summary>True when the column holds free text long enough to truncate or wrap.</summary>
    public required bool SupportsLongText { get; init; }
}

/// <summary>A single chosen column — selection, order (list position), width, align, long-text handling.</summary>
public record RosterTableColumnDto
{
    /// <summary>Matches <see cref="RosterTableFieldDto.Key"/>.</summary>
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
/// Full Roster Table Designer generate request. The broad flat dataset carries the superset of
/// registrant fields; this payload selects/orders the columns, groups + sorts the rows, sets
/// orientation, and chooses whether to include staff (players-only for recruiting/STEPS-style
/// reports; everyone for club rosters).
/// </summary>
public record RosterTableRequestDto
{
    /// <summary>
    /// Section grouping: "None" | "AgeGroup" | "Division" | "Team" | "Club" | "School"
    /// | "DayGroup" | "NightGroup" | "Roommate" (the camp groupings).
    /// </summary>
    public required string GroupBy { get; init; }

    /// <summary>Sort within each group: "Name" | "Uniform" | "School" | "GradYear".</summary>
    public required string SortBy { get; init; }

    /// <summary>Ordered columns (list order = print order, left to right).</summary>
    public required IReadOnlyList<RosterTableColumnDto> Columns { get; init; }

    /// <summary>"Portrait" | "Landscape" — landscape gives ~792pt of width for column-heavy reports.</summary>
    public required string Orientation { get; init; }

    /// <summary>True = Player rows only (recruiting / STEPS); false = all active registrants incl. Staff.</summary>
    public required bool PlayersOnly { get; init; }

    /// <summary>Start each group section on a fresh page.</summary>
    public required bool PageBreakPerGroup { get; init; }

    /// <summary>Tint each group's section header with the age group's color.</summary>
    public required bool ColorAccent { get; init; }
}
