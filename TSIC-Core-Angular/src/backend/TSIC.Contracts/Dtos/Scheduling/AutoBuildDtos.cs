namespace TSIC.Contracts.Dtos.Scheduling;

// ══════════════════════════════════════════════════════════
// Source Job Selection
// ══════════════════════════════════════════════════════════

/// <summary>
/// A candidate prior-year job whose schedule can be used as a template.
/// </summary>
public record AutoBuildSourceJobDto
{
    public required Guid JobId { get; init; }
    public required string JobName { get; init; }
    public required string JobPath { get; init; }
    public string? Year { get; init; }
    public string? Season { get; init; }
    public required int ScheduledGameCount { get; init; }
}

// ══════════════════════════════════════════════════════════
// Pattern Extraction
// ══════════════════════════════════════════════════════════

/// <summary>
/// A single game placement pattern extracted from a prior year's schedule.
/// Abstracted from literal dates to DayOfWeek + TimeOfDay for year-agnostic replay.
/// </summary>
public record GamePlacementPattern
{
    public required string AgegroupName { get; init; }
    public required string DivName { get; init; }
    public required int Rnd { get; init; }
    public required int GameNumber { get; init; }
    public required string FieldName { get; init; }
    public required Guid FieldId { get; init; }
    public required DayOfWeek DayOfWeek { get; init; }
    public required TimeSpan TimeOfDay { get; init; }
    /// <summary>Day ordinal within the tournament (0-based): first game day=0, second=1, etc.</summary>
    public required int DayOrdinal { get; init; }
    public required string T1Type { get; init; }
    public required string T2Type { get; init; }
}

// ══════════════════════════════════════════════════════════
// Agegroup Mapping (user-confirmed, before full analysis)
// ══════════════════════════════════════════════════════════

/// <summary>
/// A single proposed mapping from a source agegroup to a current agegroup.
/// System proposes best-guess; user confirms/adjusts before full analysis.
/// </summary>
public record AgegroupMappingProposal
{
    public required string SourceAgegroupName { get; init; }
    /// <summary>Division names in this source agegroup (for context).</summary>
    public required List<string> SourceDivisionNames { get; init; }
    public required int SourceDivisionCount { get; init; }
    /// <summary>Proposed current agegroup name. Null if no match found.</summary>
    public string? ProposedCurrentAgegroupName { get; init; }
    /// <summary>"exact" | "year-increment" | "none"</summary>
    public required string MatchStrategy { get; init; }
}

/// <summary>
/// Response for the agegroup mapping proposal step.
/// </summary>
public record AgegroupMappingResponse
{
    public required Guid SourceJobId { get; init; }
    public required string SourceJobName { get; init; }
    public required string SourceYear { get; init; }
    public required int SourceTotalGames { get; init; }
    public required List<AgegroupMappingProposal> Proposals { get; init; }
    /// <summary>Distinct active agegroup names in the current job (for dropdown options).</summary>
    public required List<string> CurrentAgegroupNames { get; init; }
    /// <summary>Agegroup name → hex color for current-year agegroups (null entries omitted).</summary>
    public required Dictionary<string, string> CurrentAgegroupColors { get; init; }
}

/// <summary>
/// User-confirmed mapping from source agegroup to current agegroup.
/// </summary>
public record ConfirmedAgegroupMapping
{
    public required string SourceAgegroupName { get; init; }
    /// <summary>Current agegroup name. Null = skip/unmapped.</summary>
    public string? CurrentAgegroupName { get; init; }
}

/// <summary>
/// Request body for the analyze and propose-mappings endpoints.
/// </summary>
public record AutoBuildAnalyzeRequest
{
    public required Guid SourceJobId { get; init; }
    /// <summary>User-confirmed agegroup mappings (null for propose-mappings call).</summary>
    public List<ConfirmedAgegroupMapping>? AgegroupMappings { get; init; }
}

// ══════════════════════════════════════════════════════════
// Pool-Size Pattern Matching
// ══════════════════════════════════════════════════════════

/// <summary>
/// Describes an available source-year pattern for a given pool size (team count).
/// </summary>
public record PoolSizePattern
{
    /// <summary>Number of teams in the pool.</summary>
    public required int TeamCount { get; init; }
    /// <summary>Number of round-robin games in the pattern.</summary>
    public required int GameCount { get; init; }
    /// <summary>How many source divisions had this pool size.</summary>
    public required int SourceDivisionCount { get; init; }
}

/// <summary>
/// Per current-year division: how it will be scheduled (name-match, pool-size fallback, or auto-schedule).
/// </summary>
public record PoolSizeCoverage
{
    public required Guid DivId { get; init; }
    public required Guid AgegroupId { get; init; }
    public required string AgegroupName { get; init; }
    public string? AgegroupColor { get; init; }
    public required string DivName { get; init; }
    public required int TeamCount { get; init; }
    /// <summary>True if a pattern (name-matched or pool-size) is available.</summary>
    public required bool HasPattern { get; init; }
    /// <summary>Number of RR games the pattern will place (0 if no pattern).</summary>
    public required int PatternGameCount { get; init; }
    /// <summary>"name-matched" | "pool-size-fallback" | "no-match"</summary>
    public required string MatchStrategy { get; init; }
    /// <summary>Source agegroup used for name-match (null if pool-size fallback).</summary>
    public string? SourceAgegroupName { get; init; }
    /// <summary>Source division used for name-match (null if pool-size fallback).</summary>
    public string? SourceDivName { get; init; }
}

// ══════════════════════════════════════════════════════════
// Feasibility & Analysis
// ══════════════════════════════════════════════════════════

/// <summary>
/// Overall feasibility assessment for auto-build.
/// </summary>
public record AutoBuildFeasibility
{
    public required int TotalCurrentDivisions { get; init; }
    /// <summary>Divisions covered by any pattern (name-match or pool-size).</summary>
    public required int CoveredDivisions { get; init; }
    /// <summary>Divisions with no pattern at all (will use auto-schedule).</summary>
    public required int UncoveredDivisions { get; init; }
    /// <summary>Divisions matched by exact agegroup + division name from source.</summary>
    public required int NameMatchedDivisions { get; init; }
    /// <summary>"green" (>80%), "yellow" (50-80%), "red" (&lt;50%).</summary>
    public required string ConfidenceLevel { get; init; }
    public required int ConfidencePercent { get; init; }
    public required List<string> FieldMismatches { get; init; }
    public required List<string> Warnings { get; init; }
    /// <summary>Distinct pool sizes found in the source job.</summary>
    public required List<PoolSizePattern> AvailablePatterns { get; init; }
}

/// <summary>
/// Complete analysis response returned to the agent UI.
/// </summary>
public record AutoBuildAnalysisResponse
{
    public required Guid SourceJobId { get; init; }
    public required string SourceJobName { get; init; }
    public required string SourceYear { get; init; }
    public required int SourceTotalGames { get; init; }
    /// <summary>Per-division coverage with match strategy (name-matched, pool-size, or no-match).</summary>
    public required List<PoolSizeCoverage> DivisionCoverage { get; init; }
    public required AutoBuildFeasibility Feasibility { get; init; }
    /// <summary>The confirmed agegroup mappings used for this analysis.</summary>
    public required List<ConfirmedAgegroupMapping> AgegroupMappings { get; init; }
}

// ══════════════════════════════════════════════════════════
// Build Request (after user answers questions)
// ══════════════════════════════════════════════════════════

/// <summary>
/// Request to execute the auto-build schedule generation.
/// </summary>
public record AutoBuildRequest
{
    public required Guid SourceJobId { get; init; }
    /// <summary>Divisions to skip entirely.</summary>
    public List<Guid>? SkipDivisionIds { get; init; }
    /// <summary>If true, include bracket games in the pattern replay.</summary>
    public bool IncludeBracketGames { get; init; }
    /// <summary>If true, skip divisions that already have games scheduled.</summary>
    public bool SkipAlreadyScheduled { get; init; }
    /// <summary>User-confirmed agegroup mappings for name-first matching.</summary>
    public List<ConfirmedAgegroupMapping>? AgegroupMappings { get; init; }
}

// ══════════════════════════════════════════════════════════
// Build Result
// ══════════════════════════════════════════════════════════

/// <summary>
/// Per-division result of the auto-build process.
/// </summary>
public record AutoBuildDivisionResult
{
    public required string AgegroupName { get; init; }
    public required string DivName { get; init; }
    public required Guid DivId { get; init; }
    public required int GamesPlaced { get; init; }
    public required int GamesFailed { get; init; }
    /// <summary>"pattern-replay" | "auto-schedule" | "skipped" | "already-scheduled"</summary>
    public required string Status { get; init; }
}

/// <summary>
/// Overall result of the auto-build operation.
/// </summary>
public record AutoBuildResult
{
    public required int TotalDivisions { get; init; }
    public required int DivisionsScheduled { get; init; }
    public required int DivisionsSkipped { get; init; }
    public required int TotalGamesPlaced { get; init; }
    public required int GamesFailedToPlace { get; init; }
    public required List<AutoBuildDivisionResult> DivisionResults { get; init; }
}

// ══════════════════════════════════════════════════════════
// Parking Preview
// ══════════════════════════════════════════════════════════

/// <summary>
/// Peak parking load for a single field complex.
/// </summary>
public record ParkingPreviewDto
{
    public required string ComplexName { get; init; }
    public required DateTime PeakTime { get; init; }
    public required int PeakTeamsOnSite { get; init; }
    public required int EstimatedCars { get; init; }
    /// <summary>"ok" | "warn" | "critical"</summary>
    public required string Severity { get; init; }
}

/// <summary>
/// Parking validation result for the proposed schedule.
/// </summary>
public record AutoBuildParkingResponse
{
    public required List<ParkingPreviewDto> Complexes { get; init; }
    public required bool HasWarnings { get; init; }
}

// ══════════════════════════════════════════════════════════
// Repository helper DTOs (internal to pattern extraction)
// ══════════════════════════════════════════════════════════

/// <summary>
/// Summary of a division from the source (prior year) job's schedule.
/// </summary>
public record SourceDivisionSummary
{
    public required string AgegroupName { get; init; }
    public required string DivName { get; init; }
    public required int TeamCount { get; init; }
    public required int GameCount { get; init; }
}

/// <summary>
/// Summary of a division from the current year's job.
/// </summary>
public record CurrentDivisionSummary
{
    public required Guid AgegroupId { get; init; }
    public required string AgegroupName { get; init; }
    public string? AgegroupColor { get; init; }
    public required Guid DivId { get; init; }
    public required string DivName { get; init; }
    public required int TeamCount { get; init; }
}

/// <summary>
/// Field name mapping for field-name matching between years.
/// </summary>
public record FieldNameMapping
{
    public required Guid FieldId { get; init; }
    public required string FName { get; init; }
}

// ══════════════════════════════════════════════════════════
// Post-Build QA Validation
// ══════════════════════════════════════════════════════════

/// <summary>
/// Post-build QA validation result — comprehensive schedule quality checks.
/// Mirrors the checks in [utility].[Schedule_QA_Tourny].
/// </summary>
public record AutoBuildQaResult
{
    public required int TotalGames { get; init; }

    // ── Critical (errors) ──
    public required List<QaUnscheduledTeam> UnscheduledTeams { get; init; }
    public required List<QaDoubleBooking> FieldDoubleBookings { get; init; }
    public required List<QaDoubleBooking> TeamDoubleBookings { get; init; }
    public required List<QaRankMismatch> RankMismatches { get; init; }

    // ── Warnings ──
    public required List<QaBackToBack> BackToBackGames { get; init; }
    public required List<QaRepeatedMatchup> RepeatedMatchups { get; init; }
    public required List<QaInactiveTeamInGame> InactiveTeamsInGames { get; init; }

    // ── Informational ──
    public required List<QaGamesPerDate> GamesPerDate { get; init; }
    public required List<QaGamesPerTeam> GamesPerTeam { get; init; }
    public required List<QaGamesPerTeamPerDay> GamesPerTeamPerDay { get; init; }
    public required List<QaGamesPerFieldPerDay> GamesPerFieldPerDay { get; init; }
    public required List<QaGameSpread> GameSpreads { get; init; }
    public required List<QaRrGamesPerDiv> RrGamesPerDivision { get; init; }
    public required List<QaBracketGame> BracketGames { get; init; }
}

/// <summary>
/// An active team with zero scheduled games.
/// </summary>
public record QaUnscheduledTeam
{
    public required string AgegroupName { get; init; }
    public required string DivName { get; init; }
    public required string TeamName { get; init; }
    public required int DivRank { get; init; }
}

/// <summary>
/// A double-booking conflict (field or team).
/// </summary>
public record QaDoubleBooking
{
    public required string Label { get; init; }
    public required DateTime GameDate { get; init; }
    public required int Count { get; init; }
}

/// <summary>
/// A back-to-back game pair (games within gamestartInterval of each other).
/// </summary>
public record QaBackToBack
{
    public required string AgegroupName { get; init; }
    public required string DivName { get; init; }
    public required string TeamName { get; init; }
    public required string FieldName { get; init; }
    public required DateTime GameDate { get; init; }
    public required int MinutesSincePrevious { get; init; }
}

/// <summary>
/// Games-per-team count for fairness validation.
/// </summary>
public record QaGamesPerTeam
{
    public required string AgegroupName { get; init; }
    public required string DivName { get; init; }
    public required string TeamName { get; init; }
    public required int GameCount { get; init; }
}

/// <summary>
/// First-to-last game time spread per team per day.
/// </summary>
public record QaGameSpread
{
    public required string AgegroupName { get; init; }
    public required string DivName { get; init; }
    public required string TeamName { get; init; }
    public required string GameDay { get; init; }
    public required int SpreadMinutes { get; init; }
    public required int GameCount { get; init; }
}

/// <summary>
/// Schedule team number (T1_No/T2_No) doesn't match team's actual divRank.
/// </summary>
public record QaRankMismatch
{
    public required string AgegroupName { get; init; }
    public required string DivName { get; init; }
    public required string FieldName { get; init; }
    public required DateTime GameDate { get; init; }
    public required string TeamName { get; init; }
    public required int ScheduleNo { get; init; }
    public required int ActualDivRank { get; init; }
}

/// <summary>
/// Total games scheduled per calendar date.
/// </summary>
public record QaGamesPerDate
{
    public required string GameDay { get; init; }
    public required int GameCount { get; init; }
}

/// <summary>
/// Games per team per day (more granular than overall GamesPerTeam).
/// </summary>
public record QaGamesPerTeamPerDay
{
    public required string AgegroupName { get; init; }
    public required string DivName { get; init; }
    public required string ClubName { get; init; }
    public required string TeamName { get; init; }
    public required string GameDay { get; init; }
    public required int GameCount { get; init; }
}

/// <summary>
/// Games per field per calendar date (field utilization).
/// </summary>
public record QaGamesPerFieldPerDay
{
    public required string FieldName { get; init; }
    public required string GameDay { get; init; }
    public required int GameCount { get; init; }
}

/// <summary>
/// Round-robin completeness: pool size vs game count per division.
/// </summary>
public record QaRrGamesPerDiv
{
    public required string AgegroupName { get; init; }
    public required string DivName { get; init; }
    public required int PoolSize { get; init; }
    public required int GameCount { get; init; }
}

/// <summary>
/// An inactive team appearing in a scheduled game.
/// </summary>
public record QaInactiveTeamInGame
{
    public required string AgegroupName { get; init; }
    public required string DivName { get; init; }
    public required string TeamName { get; init; }
    public required int DivRank { get; init; }
    public required bool Active { get; init; }
}

/// <summary>
/// Two teams playing each other more than once in the same job.
/// </summary>
public record QaRepeatedMatchup
{
    public required string AgegroupName { get; init; }
    public required string DivName { get; init; }
    public required string Team1Name { get; init; }
    public required string Team2Name { get; init; }
    public required int GameCount { get; init; }
}

/// <summary>
/// A bracket/playoff game (T1_Type or T2_Type is not 'T').
/// </summary>
public record QaBracketGame
{
    public required string AgegroupName { get; init; }
    public required string FieldName { get; init; }
    public required DateTime GameDate { get; init; }
    public required string T1Type { get; init; }
    public required int T1No { get; init; }
    public required string T2Type { get; init; }
    public required int T2No { get; init; }
}

// ══════════════════════════════════════════════════════════
// Game Summary (current schedule status)
// ══════════════════════════════════════════════════════════

/// <summary>
/// Per-division game count summary for the current job.
/// </summary>
public record ScheduleGameSummaryDto
{
    public required string AgegroupName { get; init; }
    public required Guid AgegroupId { get; init; }
    public string? AgegroupColor { get; init; }
    public required string DivName { get; init; }
    public required Guid DivId { get; init; }
    public required int TeamCount { get; init; }
    public required int GameCount { get; init; }
    /// <summary>Expected round-robin games: n*(n-1)/2 where n = TeamCount.</summary>
    public required int ExpectedRrGames { get; init; }
}

/// <summary>
/// Overall game summary response for the current job.
/// </summary>
public record GameSummaryResponse
{
    public required int TotalGames { get; init; }
    public required int TotalDivisions { get; init; }
    public required int DivisionsWithGames { get; init; }
    public required List<ScheduleGameSummaryDto> Divisions { get; init; }
}
