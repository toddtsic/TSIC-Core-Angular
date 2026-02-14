namespace TSIC.Contracts.Dtos.Scheduling;

// ══════════════════════════════════════════════════════════════════════
// Filter / Request DTOs
// ══════════════════════════════════════════════════════════════════════

/// <summary>
/// POST body for all view-schedule tab endpoints (games, standings, brackets, contacts).
/// Uses OR-union logic: show games matching ANY selected club/agegroup/division/team.
/// Empty arrays are omitted by the frontend (sanitized to null) so null = no filter.
/// </summary>
public record ScheduleFilterRequest
{
    public List<string>? ClubNames { get; init; }
    public List<Guid>? AgegroupIds { get; init; }
    public List<Guid>? DivisionIds { get; init; }
    public List<Guid>? TeamIds { get; init; }
    public List<DateTime>? GameDays { get; init; }
    public List<Guid>? FieldIds { get; init; }
    public bool? UnscoredOnly { get; init; }
}

/// <summary>
/// Quick inline score edit — the most common use case.
/// </summary>
public record EditScoreRequest
{
    public required int Gid { get; init; }
    public required int T1Score { get; init; }
    public required int T2Score { get; init; }
    public int? GStatusCode { get; init; }
}

/// <summary>
/// Full game edit modal — supports overriding teams, annotations, and status.
/// </summary>
public record EditGameRequest
{
    public required int Gid { get; init; }
    public int? T1Score { get; init; }
    public int? T2Score { get; init; }
    public Guid? T1Id { get; init; }
    public Guid? T2Id { get; init; }
    public string? T1Name { get; init; }
    public string? T2Name { get; init; }
    public string? T1Ann { get; init; }
    public string? T2Ann { get; init; }
    public int? GStatusCode { get; init; }
}

// ══════════════════════════════════════════════════════════════════════
// Filter Options (CADT tree)
// ══════════════════════════════════════════════════════════════════════

/// <summary>
/// Filter options for the view-schedule page, loaded once on init.
/// Contains the CADT tree (Club → Agegroup → Division → Team) plus
/// game days and field options.
/// </summary>
public record ScheduleFilterOptionsDto
{
    public required List<CadtClubNode> Clubs { get; init; }
    public required List<DateTime> GameDays { get; init; }
    public required List<FieldSummaryDto> Fields { get; init; }
}

/// <summary>Top-level club node in the CADT filter tree.</summary>
public record CadtClubNode
{
    public required string ClubName { get; init; }
    public required List<CadtAgegroupNode> Agegroups { get; init; }
}

/// <summary>Agegroup node — includes color for the colored dot in the tree UI.</summary>
public record CadtAgegroupNode
{
    public required Guid AgegroupId { get; init; }
    public required string AgegroupName { get; init; }
    public string? Color { get; init; }
    public required List<CadtDivisionNode> Divisions { get; init; }
}

/// <summary>Division node within an agegroup under a club.</summary>
public record CadtDivisionNode
{
    public required Guid DivId { get; init; }
    public required string DivName { get; init; }
    public required List<CadtTeamNode> Teams { get; init; }
}

/// <summary>Leaf-level team node.</summary>
public record CadtTeamNode
{
    public required Guid TeamId { get; init; }
    public required string TeamName { get; init; }
}

/// <summary>Field summary for the field filter dropdown.</summary>
public record FieldSummaryDto
{
    public required Guid FieldId { get; init; }
    public required string FName { get; init; }
}

// ══════════════════════════════════════════════════════════════════════
// Games Tab
// ══════════════════════════════════════════════════════════════════════

/// <summary>
/// A single game row for the Games tab.
/// </summary>
public record ViewGameDto
{
    public required int Gid { get; init; }
    public required DateTime GDate { get; init; }
    public required string FName { get; init; }
    public required Guid FieldId { get; init; }
    public double? Latitude { get; init; }
    public double? Longitude { get; init; }
    /// <summary>"U10:Gold" — agegroup:division display label.</summary>
    public required string AgDiv { get; init; }
    public required string T1Name { get; init; }
    public required string T2Name { get; init; }
    public Guid? T1Id { get; init; }
    public Guid? T2Id { get; init; }
    public int? T1Score { get; init; }
    public int? T2Score { get; init; }
    public required string T1Type { get; init; }
    public required string T2Type { get; init; }
    public string? T1Ann { get; init; }
    public string? T2Ann { get; init; }
    public int? Rnd { get; init; }
    public int? GStatusCode { get; init; }
    public string? Color { get; init; }
}

// ══════════════════════════════════════════════════════════════════════
// Standings + Records Tabs
// ══════════════════════════════════════════════════════════════════════

/// <summary>
/// A single team's standings row.
/// Used for both pool-play standings (Standings tab) and full-season records (Records tab).
/// </summary>
public record StandingsDto
{
    public required Guid TeamId { get; init; }
    public required string TeamName { get; init; }
    public required string AgegroupName { get; init; }
    public required string DivName { get; init; }
    public required Guid DivId { get; init; }
    public required int Games { get; init; }
    public required int Wins { get; init; }
    public required int Losses { get; init; }
    public required int Ties { get; init; }
    public required int GoalsFor { get; init; }
    public required int GoalsAgainst { get; init; }
    public required int GoalDiffMax9 { get; init; }
    public required int Points { get; init; }
    public required decimal PointsPerGame { get; init; }
    public int? RankOrder { get; init; }
}

/// <summary>
/// Standings grouped by division, with teams sorted by rank within each division.
/// </summary>
public record DivisionStandingsDto
{
    public required Guid DivId { get; init; }
    public required string AgegroupName { get; init; }
    public required string DivName { get; init; }
    public required List<StandingsDto> Teams { get; init; }
}

/// <summary>
/// Response wrapper for standings/records — one entry per division.
/// </summary>
public record StandingsByDivisionResponse
{
    public required List<DivisionStandingsDto> Divisions { get; init; }
    /// <summary>Sport name determines sort order (soccer vs lacrosse).</summary>
    public required string SportName { get; init; }
}

// ══════════════════════════════════════════════════════════════════════
// Team Results Drill-down
// ══════════════════════════════════════════════════════════════════════

/// <summary>
/// A single game from a specific team's perspective — used for the team results drill-down modal.
/// </summary>
public record TeamResultDto
{
    public required int Gid { get; init; }
    public required DateTime GDate { get; init; }
    public required string Location { get; init; }
    public required string OpponentName { get; init; }
    public Guid? OpponentTeamId { get; init; }
    public int? TeamScore { get; init; }
    public int? OpponentScore { get; init; }
    /// <summary>"W", "L", "T", or null if unscored.</summary>
    public string? Outcome { get; init; }
    /// <summary>"Pool Play" or bracket round type (QF, SF, F, etc.).</summary>
    public required string GameType { get; init; }
}

// ══════════════════════════════════════════════════════════════════════
// Brackets Tab
// ══════════════════════════════════════════════════════════════════════

/// <summary>
/// A single bracket match — includes parent game reference for tree rendering.
/// </summary>
public record BracketMatchDto
{
    public required int Gid { get; init; }
    public required string T1Name { get; init; }
    public required string T2Name { get; init; }
    public int? T1Score { get; init; }
    public int? T2Score { get; init; }
    /// <summary>"winner", "loser", or "pending" — determines CSS styling.</summary>
    public required string T1Css { get; init; }
    /// <summary>"winner", "loser", or "pending" — determines CSS styling.</summary>
    public required string T2Css { get; init; }
    public string? LocationTime { get; init; }
    /// <summary>Round type: Z, Y, X, Q, S, F.</summary>
    public required string RoundType { get; init; }
    /// <summary>Parent game ID (the game this feeds into). Null for the final.</summary>
    public int? ParentGid { get; init; }
}

/// <summary>
/// All bracket matches for a single division (or agegroup if BChampionsByDivision is false).
/// </summary>
public record DivisionBracketResponse
{
    public required string AgegroupName { get; init; }
    public required string DivName { get; init; }
    public string? Champion { get; init; }
    public required List<BracketMatchDto> Matches { get; init; }
}

// ══════════════════════════════════════════════════════════════════════
// Contacts Tab
// ══════════════════════════════════════════════════════════════════════

/// <summary>
/// A staff contact for the Contacts tab — grouped by agegroup/division/club/team in the UI.
/// </summary>
public record ContactDto
{
    public required string AgegroupName { get; init; }
    public required string DivName { get; init; }
    public required string ClubName { get; init; }
    public required string TeamName { get; init; }
    public required string FirstName { get; init; }
    public required string LastName { get; init; }
    public string? Cellphone { get; init; }
    public string? Email { get; init; }
}

// ══════════════════════════════════════════════════════════════════════
// Field Display (directions / map)
// ══════════════════════════════════════════════════════════════════════

/// <summary>
/// Detailed field info for the field directions display.
/// </summary>
public record FieldDisplayDto
{
    public required Guid FieldId { get; init; }
    public required string FName { get; init; }
    public string? Address { get; init; }
    public string? City { get; init; }
    public string? State { get; init; }
    public string? Zip { get; init; }
    public string? Directions { get; init; }
    public double? Latitude { get; init; }
    public double? Longitude { get; init; }
}

// ══════════════════════════════════════════════════════════════════════
// Schedule Metadata (for public access / capability flags)
// ══════════════════════════════════════════════════════════════════════

/// <summary>
/// Capability flags returned alongside filter options — tells the frontend
/// what features are available for this job/user combination.
/// </summary>
public record ScheduleCapabilitiesDto
{
    public required bool CanScore { get; init; }
    public required bool HideContacts { get; init; }
    public required bool IsPublicAccess { get; init; }
    public required string SportName { get; init; }
}
