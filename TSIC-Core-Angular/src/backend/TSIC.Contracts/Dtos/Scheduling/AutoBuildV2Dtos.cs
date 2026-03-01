namespace TSIC.Contracts.Dtos.Scheduling;

// ══════════════════════════════════════════════════════════
// Source Schedule Property Enums
// ══════════════════════════════════════════════════════════

/// <summary>
/// Binary classification of how games within a round were arranged in the source.
/// </summary>
public enum RoundLayout
{
    /// <summary>All games in the round at the same tick on different fields.</summary>
    Horizontal,
    /// <summary>Games stacked on the GSI grid (tick 0, tick 1, tick 2...).</summary>
    Sequential
}

/// <summary>
/// Whether the source distributed teams evenly across fields or concentrated them.
/// </summary>
public enum FieldFairness
{
    /// <summary>All teams played roughly equal games on each field.</summary>
    Democratic,
    /// <summary>Some teams were assigned to specific fields disproportionately.</summary>
    Biased
}

// ══════════════════════════════════════════════════════════
// Division Size Profile (Q1–Q12 Attribute Extraction)
// ══════════════════════════════════════════════════════════

/// <summary>
/// Aggregate scheduling attributes for all divisions of a given team count (TCnt).
/// Extracted from a prior year's schedule to guide horizontal placement.
/// </summary>
public record DivisionSizeProfile
{
    /// <summary>Team count (pool size) — the sole grouping key.</summary>
    public required int TCnt { get; init; }
    /// <summary>How many current-year divisions have this team count.</summary>
    public required int DivisionCount { get; init; }

    /// <summary>Q1: Which days of the week did this TCnt play?</summary>
    public required List<DayOfWeek> PlayDays { get; init; }

    /// <summary>Q2 (primary): Offset from the source timeslot window start per day.
    /// Captures WHERE within the window this TCnt historically started, not the absolute clock time.
    /// Null when extracted without source timeslot data (fallback to TimeRangeAbsolute).</summary>
    public Dictionary<DayOfWeek, TimeSpan>? StartOffsetFromWindow { get; init; }

    /// <summary>Q2 (fallback): Absolute start/end time range per day for this TCnt.
    /// Used when offset is unavailable or for clean sheet mode.</summary>
    public required Dictionary<DayOfWeek, TimeRangeDto> TimeRangeAbsolute { get; init; }

    /// <summary>Q2 (diagnostic): Fraction of the timeslot window used per day (0.0–1.0).
    /// Helps detect whether a division used a narrow slice or the whole window.</summary>
    public Dictionary<DayOfWeek, double>? WindowUtilization { get; init; }

    /// <summary>Q3: Ordered list of field names used by this TCnt.</summary>
    public required List<string> FieldBand { get; init; }

    /// <summary>Q4a: Total round count for this TCnt.</summary>
    public required int RoundCount { get; init; }

    /// <summary>Q4b: Minimum games any team plays. Even TCnt = RoundCount; odd = RoundCount - 1.</summary>
    public required int GameGuarantee { get; init; }

    /// <summary>Q5: Per-round placement shape (horizontal vs vertical).</summary>
    public required Dictionary<int, RoundShapeDto> PlacementShapePerRound { get; init; }

    /// <summary>Q6: First-to-last game interval per day for this TCnt.</summary>
    public required Dictionary<DayOfWeek, TimeSpan> OnsiteIntervalPerDay { get; init; }

    /// <summary>Q7: Per-field usage profile for desirability tracking.</summary>
    public required Dictionary<string, FieldUsageDto> FieldDesirability { get; init; }

    /// <summary>Q8: How many rounds were played on each day.</summary>
    public required Dictionary<DayOfWeek, int> RoundsPerDay { get; init; }

    /// <summary>Q9: For odd TCnt, which day received the extra round. Null for even TCnt.</summary>
    public DayOfWeek? ExtraRoundDay { get; init; }

    /// <summary>Q10: Median time gap between consecutive round start times on the same day.</summary>
    public required TimeSpan InterRoundInterval { get; init; }

    /// <summary>Q11: Median team span per day from source — minutes from a team's first to last game.
    /// Used for reporting (comparing source vs result), not as a hard threshold.</summary>
    public TimeSpan? MedianTeamSpan { get; init; }

    // ── Tick-based source properties (distance-from-source scoring) ──

    /// <summary>GSI (Game Start Interval) in minutes, inferred from source game patterns.
    /// The primitive clock tick — all tick-based properties are relative to this.</summary>
    public int GsiMinutes { get; init; }

    /// <summary>Binary round layout derived from Q5 VerticalityRatio.
    /// Horizontal = all games at same tick on different fields.
    /// Sequential = games stacked on GSI grid.</summary>
    public RoundLayout RoundLayout { get; init; }

    /// <summary>Q2 expressed as GSI ticks from field window start per day.
    /// Portable across different window starts and GSI values.</summary>
    public Dictionary<DayOfWeek, int>? StartTickOffset { get; init; }

    /// <summary>Q10 expressed as GSI ticks between consecutive round start times.</summary>
    public int InterRoundGapTicks { get; init; }

    /// <summary>Q12: Smallest observed gap in GSI ticks between any team's consecutive games on a day.
    /// 1 = BTBs existed, 2 = no BTBs (most common), 3+ = intentional wider spacing.</summary>
    public int MinTeamGapTicks { get; init; }

    /// <summary>Whether the source distributed teams evenly across fields (Q7 derived).</summary>
    public FieldFairness FieldFairness { get; init; }
}

/// <summary>
/// Time range (start and end) for a given day of the week.
/// </summary>
public record TimeRangeDto
{
    public required TimeSpan Start { get; init; }
    public required TimeSpan End { get; init; }
}

/// <summary>
/// Per-round placement shape describing horizontal vs vertical layout.
/// </summary>
public record RoundShapeDto
{
    /// <summary>Number of games in this round.</summary>
    public required int GameCount { get; init; }
    /// <summary>Number of distinct time slots used by games in this round.</summary>
    public required int DistinctTimeSlots { get; init; }
    /// <summary>0.0 = fully horizontal (all games at same time), 1.0 = fully vertical.</summary>
    public required double VerticalityRatio { get; init; }
}

/// <summary>
/// Per-field usage profile for desirability tracking (Q7).
/// </summary>
public record FieldUsageDto
{
    public required string FieldName { get; init; }
    /// <summary>Total games played on this field for this TCnt.</summary>
    public required int GameCount { get; init; }
    /// <summary>Ratio vs field average. &lt;1.0 = underused (possibly bad field).</summary>
    public required double UsageRatio { get; init; }
    /// <summary>Max times any single team played on this field.</summary>
    public required int MaxTeamRepeatCount { get; init; }
}

// ══════════════════════════════════════════════════════════
// Prerequisite Check
// ══════════════════════════════════════════════════════════

/// <summary>
/// Result of the three mandatory prerequisite checks before auto-build can execute.
/// </summary>
public record PrerequisiteCheckResponse
{
    /// <summary>All active teams have a division assignment.</summary>
    public required bool PoolsAssigned { get; init; }
    /// <summary>Number of active teams without a division.</summary>
    public required int UnassignedTeamCount { get; init; }

    /// <summary>All distinct TCnt values have pairings in PairingsLeagueSeason.</summary>
    public required bool PairingsCreated { get; init; }
    /// <summary>TCnt values that are missing pairings.</summary>
    public required List<int> MissingPairingTCnts { get; init; }

    /// <summary>All agegroups with active divisions have at least one timeslot date.</summary>
    public required bool TimeslotsConfigured { get; init; }
    /// <summary>Agegroup names missing timeslot configuration.</summary>
    public required List<string> AgegroupsMissingTimeslots { get; init; }

    /// <summary>True when all three checks pass.</summary>
    public required bool AllPassed { get; init; }
}

// ══════════════════════════════════════════════════════════
// Ensure Pairings (auto-generate missing round-robin)
// ══════════════════════════════════════════════════════════

/// <summary>
/// Request to auto-generate round-robin pairings for specified team counts.
/// </summary>
public record EnsurePairingsRequest
{
    /// <summary>Team counts that need round-robin pairings generated.</summary>
    public required List<int> TeamCounts { get; init; }
}

/// <summary>
/// Result of ensure-pairings operation.
/// </summary>
public record EnsurePairingsResponse
{
    /// <summary>Team counts that had pairings generated.</summary>
    public required List<int> Generated { get; init; }
    /// <summary>Team counts that already had pairings.</summary>
    public required List<int> AlreadyExisted { get; init; }
}

// ══════════════════════════════════════════════════════════
// Profile Extraction Response
// ══════════════════════════════════════════════════════════

/// <summary>
/// Response for the profile extraction step — Q1–Q11 attributes per TCnt.
/// </summary>
public record ProfileExtractionResponse
{
    public required Guid SourceJobId { get; init; }
    public required string SourceJobName { get; init; }
    public required string SourceYear { get; init; }
    public required List<DivisionSizeProfile> Profiles { get; init; }
    /// <summary>Pre-flight disconnects between source discoveries and target timeslot canvas.</summary>
    public List<PreFlightDisconnect>? Disconnects { get; init; }
}

// ══════════════════════════════════════════════════════════
// Pre-Flight Disconnects
// ══════════════════════════════════════════════════════════

/// <summary>
/// A mismatch between what the source schedule had and what the target job's
/// timeslot canvas offers. Surfaced before building so the scheduler knows
/// exactly where the engine may have to deviate from the source pattern.
/// </summary>
public record PreFlightDisconnect
{
    /// <summary>"field", "time", "interval"</summary>
    public required string Category { get; init; }
    /// <summary>Human-readable description of the disconnect.</summary>
    public required string Description { get; init; }
}

// ══════════════════════════════════════════════════════════
// Build Request
// ══════════════════════════════════════════════════════════

/// <summary>
/// Request body for auto-build with processing order, include/exclude, and constraint priorities.
/// </summary>
public record AutoBuildRequest
{
    /// <summary>Source job to extract profiles from. Null = clean sheet mode.
    /// RETAINED for backward compat and migration inference only.</summary>
    public Guid? SourceJobId { get; init; }

    /// <summary>Agegroup IDs in processing order (first processed gets best slots).</summary>
    public required List<Guid> AgegroupOrder { get; init; }

    /// <summary>"alpha" | "odd-first" | "custom"</summary>
    public required string DivisionOrderStrategy { get; init; }

    /// <summary>Division IDs to exclude from scheduling.</summary>
    public required List<Guid> ExcludedDivisionIds { get; init; }

    /// <summary>Per-division-name scheduling strategies.</summary>
    public List<DivisionStrategyEntry>? DivisionStrategies { get; init; }

    /// <summary>When true, persist DivisionStrategies to DB after successful build.</summary>
    public bool SaveProfiles { get; init; }
}

// ══════════════════════════════════════════════════════════
// Build Result
// ══════════════════════════════════════════════════════════

/// <summary>
/// Result of auto-build with unplaced games and sacrifice log.
/// </summary>
public record AutoBuildResult
{
    public required int TotalDivisions { get; init; }
    public required int DivisionsScheduled { get; init; }
    public required int DivisionsSkipped { get; init; }
    public required int TotalGamesPlaced { get; init; }
    public required int GamesFailedToPlace { get; init; }
    public required List<AutoBuildDivisionResult> DivisionResults { get; init; }
    /// <summary>Games that could not be placed (never silently dropped).</summary>
    public required List<UnplacedGameDto> UnplacedGames { get; init; }
    /// <summary>Which constraints were sacrificed and how often.</summary>
    public required List<ConstraintSacrificeDto> SacrificeLog { get; init; }
}

/// <summary>
/// A game that could not be placed during auto-build.
/// </summary>
public record UnplacedGameDto
{
    public required string AgegroupName { get; init; }
    public required string DivName { get; init; }
    public required int Round { get; init; }
    public required int T1No { get; init; }
    public required int T2No { get; init; }
    public required string Reason { get; init; }
}

/// <summary>
/// Summary of how often a constraint was sacrificed during placement.
/// </summary>
public record ConstraintSacrificeDto
{
    public required string ConstraintName { get; init; }
    public required int ViolationCount { get; init; }
    /// <summary>First 3 example games where this constraint was violated.</summary>
    public required List<string> ExampleGames { get; init; }
    /// <summary>Human-readable explanation of what this sacrifice means for the schedule.</summary>
    public required string ImpactDescription { get; init; }
}

// ══════════════════════════════════════════════════════════
// Scoring Engine Types (shared between scorer and evaluators)
// ══════════════════════════════════════════════════════════

/// <summary>
/// A candidate (field, datetime) slot to evaluate for game placement.
/// </summary>
public record CandidateSlot
{
    public required Guid FieldId { get; init; }
    public required string FieldName { get; init; }
    public required DateTime GDate { get; init; }
}

/// <summary>
/// Context about the game being placed — used by constraint evaluators.
/// </summary>
public record GameContext
{
    public required int Round { get; init; }
    public required int GameNumber { get; init; }
    public required int T1No { get; init; }
    public required int T2No { get; init; }
    public required Guid DivId { get; init; }
    public required Guid AgegroupId { get; init; }
    public required string AgegroupName { get; init; }
    public required string DivName { get; init; }
    public required int TCnt { get; init; }
    /// <summary>Target day for this round (from profile or default).</summary>
    public DayOfWeek? TargetDay { get; init; }
    /// <summary>Target time for this round (from profile or first-game-sets-target).</summary>
    public TimeSpan? TargetTime { get; init; }
}

/// <summary>
/// A candidate slot with its distance-from-source penalty.
/// Lower TotalPenalty = closer match to source schedule pattern.
/// </summary>
public record ScoredCandidate
{
    public required CandidateSlot Slot { get; init; }
    /// <summary>Sum of all penalty components. 0 = perfect source match.</summary>
    public required int TotalPenalty { get; init; }
    /// <summary>Per-property penalty breakdown for diagnostics and sacrifice reporting.</summary>
    public required Dictionary<string, int> PenaltyBreakdown { get; init; }
}

// ══════════════════════════════════════════════════════════
// Division Strategy Profile DTOs
// ══════════════════════════════════════════════════════════

/// <summary>
/// Gap pattern between a team's consecutive games.
/// Maps to MinTeamGapTicks = GapPattern + 1.
/// </summary>
public enum GapPattern
{
    /// <summary>Back-to-back games allowed (MinTeamGapTicks = 1).</summary>
    BackToBack = 0,
    /// <summary>One game on, one off (MinTeamGapTicks = 2). Default.</summary>
    OneOnOneOff = 1,
    /// <summary>One game on, two off (MinTeamGapTicks = 3).</summary>
    OneOnTwoOff = 2
}

/// <summary>
/// A single division's scheduling strategy, identified by division name.
/// Division names are consistent across agegroups by convention.
/// </summary>
public record DivisionStrategyEntry
{
    /// <summary>Division name (e.g. "Division 1", "Pool A") — stable across agegroups.</summary>
    public required string DivisionName { get; init; }

    /// <summary>0 = Horizontal (default), 1 = Sequential/Vertical (showcase).</summary>
    public required int Placement { get; init; }

    /// <summary>0 = BackToBack, 1 = OneOnOneOff (default), 2 = OneOnTwoOff.</summary>
    public required int GapPattern { get; init; }

    /// <summary>Wave group for staggered scheduling. 1 = default (all together),
    /// 2+ = later waves. Engine completes all Wave 1 divisions before starting Wave 2.</summary>
    public int Wave { get; init; } = 1;
}

/// <summary>
/// Response containing strategy profiles for a job, with source attribution.
/// </summary>
public record DivisionStrategyProfileResponse
{
    /// <summary>Per-division strategy entries.</summary>
    public required List<DivisionStrategyEntry> Strategies { get; init; }

    /// <summary>"saved" | "inferred" | "defaults"</summary>
    public required string Source { get; init; }

    /// <summary>When Source="inferred", the job ID that was analyzed.</summary>
    public Guid? InferredFromJobId { get; init; }

    /// <summary>When Source="inferred", the human-readable job name.</summary>
    public string? InferredFromJobName { get; init; }

    /// <summary>Differences between source schedule and current timeslot setup.</summary>
    public List<PreFlightDisconnect>? Disconnects { get; init; }
}
