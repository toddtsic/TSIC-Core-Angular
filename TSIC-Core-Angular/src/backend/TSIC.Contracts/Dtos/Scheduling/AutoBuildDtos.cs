namespace TSIC.Contracts.Dtos.Scheduling;

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
    /// <summary>Team 1 position number (for Q7 field desirability per-team tracking).</summary>
    public int? T1No { get; init; }
    /// <summary>Team 2 position number (for Q7 field desirability per-team tracking).</summary>
    public int? T2No { get; init; }
}

// ══════════════════════════════════════════════════════════
// Source Job Analysis Request
// ══════════════════════════════════════════════════════════

/// <summary>
/// Request body for the extract-profiles endpoint.
/// </summary>
public record AutoBuildAnalyzeRequest
{
    public required Guid SourceJobId { get; init; }
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
    /// <summary>Pool size (number of active teams in this division).</summary>
    public required int TeamCount { get; init; }
    public required int GamesPlaced { get; init; }
    public required int GamesFailed { get; init; }
    /// <summary>"placed" | "excluded" | "no-teams" | "no-pairings" | "no-timeslots" | "no-slots" | "kept"</summary>
    public required string Status { get; init; }
}

// ══════════════════════════════════════════════════════════
// Repository Helper DTOs
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
    public string? AgegroupColor { get; init; }
    public int? GradYearMin { get; init; }
    public int? GradYearMax { get; init; }
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
    public required List<QaTeamBelowGuarantee> TeamsBelowGuarantee { get; init; }

    // ── Informational ──
    public required List<QaGamesPerDate> GamesPerDate { get; init; }
    public required List<QaGamesPerTeam> GamesPerTeam { get; init; }
    public required List<QaGamesPerTeamPerDay> GamesPerTeamPerDay { get; init; }
    public required List<QaGamesPerFieldPerDay> GamesPerFieldPerDay { get; init; }
    public required List<QaGameSpread> GameSpreads { get; init; }
    public required List<QaRrGamesPerDiv> RrGamesPerDivision { get; init; }
    public required List<QaBracketGame> BracketGames { get; init; }

    // ── Cross-Event (null when job is not in a comparison group) ──
    public CrossEventQaResult? CrossEventAnalysis { get; init; }
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
/// A back-to-back game — team appears in consecutive timeslot rows of the master schedule.
/// The master schedule is one row per distinct game-start time (across all fields);
/// consecutive rows means no rest slot between games, regardless of clock time.
/// </summary>
public record QaBackToBack
{
    public required string AgegroupName { get; init; }
    public required string DivName { get; init; }
    public required string TeamName { get; init; }
    public required string FieldName { get; init; }
    public required DateTime GameDate { get; init; }
    public required int MinutesSincePrevious { get; init; }
    /// <summary>Slot index in the master timeslot grid (0-based, per day).</summary>
    public required int SlotIndex { get; init; }
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
/// A team whose scheduled game count is below its resolved game guarantee.
/// </summary>
public record QaTeamBelowGuarantee
{
    public required string AgegroupName { get; init; }
    public required string DivName { get; init; }
    public required string TeamName { get; init; }
    public required int GameCount { get; init; }
    public required int Guarantee { get; init; }
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
// Cross-Event Raw Data (internal — not exposed via API)
// ══════════════════════════════════════════════════════════

/// <summary>
/// Raw matchup record from a single game across compared events.
/// Each game produces TWO records (team-perspective and opponent-perspective).
/// </summary>
public record CrossEventMatchupRaw
{
    public required string Agegroup { get; init; }
    public required string TeamClub { get; init; }
    public required string TeamName { get; init; }
    public required string OpponentClub { get; init; }
    public required string OpponentName { get; init; }
    public required Guid JobId { get; init; }
}

// ══════════════════════════════════════════════════════════
// Cross-Event Overplay Analysis
// ══════════════════════════════════════════════════════════

/// <summary>
/// Cross-event analysis result — only populated when the current job
/// belongs to a comparison group (e.g. Girls Summer events).
/// </summary>
public record CrossEventQaResult
{
    public required string GroupName { get; init; }
    public required List<CrossEventJobInfo> ComparedEvents { get; init; }
    public required List<CrossEventClubOverplay> ClubOverplay { get; init; }
    public required List<CrossEventTeamOverplay> TeamOverplay { get; init; }
}

/// <summary>
/// An event participating in the cross-event comparison.
/// </summary>
public record CrossEventJobInfo
{
    public required string Abbreviation { get; init; }
    public required string JobName { get; init; }
    public required int GameCount { get; init; }
}

/// <summary>
/// Club-level overplay: teams from Club A play teams from Club B
/// more than once across compared events.
/// </summary>
public record CrossEventClubOverplay
{
    public required string Agegroup { get; init; }
    public required string TeamClub { get; init; }
    public required string OpponentClub { get; init; }
    public required int MatchCount { get; init; }
}

/// <summary>
/// Team-level overplay: specific Team X plays specific Team Y
/// more than once across compared events, with event names.
/// </summary>
public record CrossEventTeamOverplay
{
    public required string Agegroup { get; init; }
    public required string TeamClub { get; init; }
    public required string TeamName { get; init; }
    public required string OpponentClub { get; init; }
    public required string OpponentName { get; init; }
    public required int MatchCount { get; init; }
    public required string Events { get; init; }
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
    /// <summary>Expected games based on pairing table (reflects game guarantee, not necessarily full RR).</summary>
    public required int ExpectedRrGames { get; init; }
}

/// <summary>
/// Overall game summary response for the current job.
/// </summary>
public record GameSummaryResponse
{
    public required string JobName { get; init; }
    public required int TotalGames { get; init; }
    public required int TotalDivisions { get; init; }
    public required int DivisionsWithGames { get; init; }
    public required List<ScheduleGameSummaryDto> Divisions { get; init; }
    /// <summary>Effective game guarantee derived from the pairing table.
    /// Null when no pairings exist yet. Computed as min games any team plays
    /// across all pool sizes (even TCnt = roundCount, odd = roundCount - 1).</summary>
    public int? GameGuarantee { get; init; }
}

// ══════════════════════════════════════════════════════════
// Source Preconfiguration (returning tournament carry-forward)
// ══════════════════════════════════════════════════════════

/// <summary>
/// Agegroup-level metadata from the source job — color and graduation year info.
/// Used for color carry-forward and year-offset agegroup name mapping.
/// </summary>
public record SourceAgegroupMeta
{
    public required string AgegroupName { get; init; }
    public string? Color { get; init; }
    public int? GradYearMin { get; init; }
    public int? GradYearMax { get; init; }
}

/// <summary>
/// A single date+round entry from the source job's timeslot dates.
/// Used for date carry-forward (advance by yearDelta, match DOW).
/// </summary>
public record SourceDateEntry
{
    public required DateTime GDate { get; init; }
    public required int Rnd { get; init; }
}

/// <summary>
/// Per-field usage pattern for a single agegroup from the source schedule.
/// Derived from actual game placements — used for field constraint learning.
/// </summary>
public record SourceFieldUsage
{
    public required string FieldName { get; init; }
    public required Guid FieldId { get; init; }
    public required int GameCount { get; init; }
    public required List<DayOfWeek> DaysUsed { get; init; }
}

/// <summary>
/// Per-agegroup per-day earliest game time from source Schedule table.
/// Used to derive correct per-agegroup start times, wave assignments, and ordering.
/// </summary>
public record SourceAgegroupTiming
{
    public required DayOfWeek DayOfWeek { get; init; }
    public required TimeSpan EarliestTime { get; init; }
}

/// <summary>Per-division per-day earliest game time from source Schedule table.
/// Groups by (AgegroupName, DivName, DayOfWeek) for per-division wave derivation.</summary>
public record SourceDivisionTiming
{
    public required string AgegroupName { get; init; }
    public required string DivName { get; init; }
    public required DayOfWeek DayOfWeek { get; init; }
    public required TimeSpan EarliestTime { get; init; }
}

/// <summary>
/// Result of seeding dates from a source job.
/// </summary>
public record DateSeedResult
{
    public required int AgegroupsSeeded { get; init; }
}

/// <summary>
/// A field entry from the source job's FieldsLeagueSeason, used for Tier 1 auto-copy.
/// </summary>
public record SourceFieldLeagueSeasonEntry
{
    public required Guid FieldId { get; init; }
    public required byte FieldPreference { get; init; }
}

/// <summary>
/// Result of seeding field-timeslot assignments from source usage patterns.
/// </summary>
public record FieldSeedResult
{
    public required int AgegroupsSeeded { get; init; }
    public required int TimeslotRowsCreated { get; init; }
    /// <summary>True if Tier 1 auto-copied FieldsLeagueSeason rows from source (no current fields existed).</summary>
    public bool FieldsLeagueSeasonCopied { get; init; }
}

/// <summary>
/// Request to preconfigure a returning tournament from a source job.
/// Runs color carry-forward, date seeding, and field constraint learning.
/// </summary>
public record PreconfigureRequest
{
    public required Guid SourceJobId { get; init; }
}

/// <summary>
/// Result of the unified preconfiguration operation.
/// </summary>
public record PreconfigureResult
{
    public required int ColorsApplied { get; init; }
    public required int DatesSeeded { get; init; }
    public required int FieldAssignmentsSeeded { get; init; }
    public required int FieldTimeslotRowsCreated { get; init; }
    /// <summary>Team counts that had round-robin pairings generated.</summary>
    public required List<int> PairingsGenerated { get; init; }
    /// <summary>Team counts that already had pairings (skipped).</summary>
    public required List<int> PairingsAlreadyExisted { get; init; }
    /// <summary>Whether cascade config (GameGuarantee, placement, rest) was seeded from source.</summary>
    public bool CascadeSeeded { get; init; }
    /// <summary>True if Tier 1 auto-copied FieldsLeagueSeason rows from source.</summary>
    public bool FieldsLeagueSeasonCopied { get; init; }
}

// ══════════════════════════════════════════════════════════
// Auto-Seed Field Timeslots (hub init — pattern-based)
// ══════════════════════════════════════════════════════════

/// <summary>
/// Result of auto-seeding field timeslots from a prior-year job's timing patterns.
/// Applies source agegroup timing (DOW, start time, GSI) to ALL currently assigned fields.
/// No 1:1 field mapping — every agegroup gets every assigned field.
/// </summary>
public record AutoSeedFieldTimeslotsResult
{
    /// <summary>Number of agegroups that had field timeslots created.</summary>
    public required int AgegroupsSeeded { get; init; }
    /// <summary>Total TimeslotsLeagueSeasonFields rows created.</summary>
    public required int TimeslotRowsCreated { get; init; }
    /// <summary>True if Tier 1 auto-copied FieldsLeagueSeason rows from source (no current fields existed).</summary>
    public bool FieldsLeagueSeasonCopied { get; init; }
    /// <summary>Number of fields assigned to the league-season after seeding.</summary>
    public int AssignedFieldCount { get; init; }
    /// <summary>Number of agegroups that had dates created (Tier 3).</summary>
    public int DatesSeeded { get; init; }
    /// <summary>Total TimeslotsLeagueSeasonDates rows created.</summary>
    public int DateRowsCreated { get; init; }
}

// ══════════════════════════════════════════════════════════
// Projected Config (read-only — no DB writes)
// ══════════════════════════════════════════════════════════

/// <summary>
/// Read-only projection of a complete schedule configuration derived from a prior year's
/// game records. Used to pre-populate the stepper so the director can review and confirm
/// before anything is written to the DB.
/// </summary>
public record ProjectedScheduleConfigDto
{
    public required Guid SourceJobId { get; init; }
    public required string SourceJobName { get; init; }
    public required string SourceYear { get; init; }

    /// <summary>Per-agegroup projected dates and field assignments.</summary>
    public required List<ProjectedAgegroupConfig> Agegroups { get; init; }

    /// <summary>Event-level timing defaults derived from source (dominant GSI, start time, max games).</summary>
    public required ProjectedTimingDefaults TimingDefaults { get; init; }

    /// <summary>Suggested wave assignments derived from source game times (agegroupId → wave).
    /// Summary/fallback — see SuggestedDivisionWaves for per-division granularity.</summary>
    public Dictionary<Guid, int>? SuggestedWaves { get; init; }

    /// <summary>Suggested agegroup processing order derived from source (day + start time sort).
    /// Summary/fallback — see SuggestedDivisionOrder for division-level granularity.</summary>
    public List<Guid>? SuggestedOrder { get; init; }

    /// <summary>Per-division wave assignment (divisionId → wave). Overrides SuggestedWaves.
    /// Derived from per-(agegroupName, divName, day) source timing clusters.</summary>
    public Dictionary<Guid, int>? SuggestedDivisionWaves { get; init; }

    /// <summary>Division-level processing order (divisionIds sorted by source timing).
    /// Wave 1 divisions first, then wave 2, within each wave sorted by earliest start time.</summary>
    public List<Guid>? SuggestedDivisionOrder { get; init; }
}

/// <summary>
/// Projected configuration for a single agegroup: dates, rounds-per-day, and per-day field assignments.
/// All dates have been advanced by yearDelta and adjusted to match the original DOW.
/// </summary>
public record ProjectedAgegroupConfig
{
    /// <summary>Current agegroup ID (mapped by name from source).</summary>
    public required Guid AgegroupId { get; init; }
    public required string AgegroupName { get; init; }

    /// <summary>Projected game dates with round counts (derived from source dates + DOW shift).</summary>
    public required List<ProjectedGameDay> GameDays { get; init; }

    /// <summary>Per-day field assignments derived from source game records.
    /// Key = DOW name ("Saturday"), Value = list of field names used on that day.</summary>
    public required Dictionary<string, List<string>> FieldsByDay { get; init; }

    /// <summary>GSI in minutes for this agegroup (from source profile or dominant default).</summary>
    public required int Gsi { get; init; }

    /// <summary>Start time string for this agegroup (e.g. "08:00 AM").</summary>
    public required string StartTime { get; init; }

    /// <summary>Max games per field for this agegroup.</summary>
    public required int MaxGamesPerField { get; init; }
}

/// <summary>A single projected game day with its date and round count.</summary>
public record ProjectedGameDay
{
    /// <summary>Projected date (advanced from source, DOW-matched).</summary>
    public required DateTime Date { get; init; }
    /// <summary>Number of rounds on this day (from source).</summary>
    public required int Rounds { get; init; }
    /// <summary>Day of week name (e.g. "Saturday").</summary>
    public required string Dow { get; init; }
}

/// <summary>Event-level timing defaults from the source job (dominant values).</summary>
public record ProjectedTimingDefaults
{
    public required int Gsi { get; init; }
    public required string StartTime { get; init; }
    public required int MaxGamesPerField { get; init; }
}
