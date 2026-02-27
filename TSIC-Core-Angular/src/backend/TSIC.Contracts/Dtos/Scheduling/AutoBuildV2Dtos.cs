namespace TSIC.Contracts.Dtos.Scheduling;

// ══════════════════════════════════════════════════════════
// Division Size Profile (Q1–Q10 Attribute Extraction)
// ══════════════════════════════════════════════════════════

/// <summary>
/// Aggregate scheduling attributes for all divisions of a given team count (TCnt).
/// Extracted from a prior year's schedule to guide V2 horizontal placement.
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
// Profile Extraction Response
// ══════════════════════════════════════════════════════════

/// <summary>
/// Response for the profile extraction step — Q1–Q10 attributes per TCnt.
/// </summary>
public record ProfileExtractionResponse
{
    public required Guid SourceJobId { get; init; }
    public required string SourceJobName { get; init; }
    public required string SourceYear { get; init; }
    public required List<DivisionSizeProfile> Profiles { get; init; }
}

// ══════════════════════════════════════════════════════════
// V2 Build Request
// ══════════════════════════════════════════════════════════

/// <summary>
/// Request body for V2 auto-build with processing order, include/exclude, and constraint priorities.
/// </summary>
public record AutoBuildV2Request
{
    /// <summary>Source job to extract profiles from. Null = clean sheet mode.</summary>
    public Guid? SourceJobId { get; init; }

    /// <summary>Agegroup IDs in processing order (first processed gets best slots).</summary>
    public required List<Guid> AgegroupOrder { get; init; }

    /// <summary>"alpha" | "odd-first" | "custom"</summary>
    public required string DivisionOrderStrategy { get; init; }

    /// <summary>Division IDs to exclude from scheduling.</summary>
    public required List<Guid> ExcludedDivisionIds { get; init; }

    /// <summary>Constraint names in priority order (index 0 = highest priority).</summary>
    public required List<string> ConstraintPriorities { get; init; }

    /// <summary>User-confirmed agegroup mappings for name-first matching.</summary>
    public List<ConfirmedAgegroupMapping>? AgegroupMappings { get; init; }
}

// ══════════════════════════════════════════════════════════
// V2 Build Result
// ══════════════════════════════════════════════════════════

/// <summary>
/// Result of V2 auto-build with unplaced games and sacrifice log.
/// </summary>
public record AutoBuildV2Result
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
/// A candidate slot with its score and constraint violations.
/// </summary>
public record ScoredCandidate
{
    public required CandidateSlot Slot { get; init; }
    public required int Score { get; init; }
    public required int MaxPossibleScore { get; init; }
    public required List<string> Violations { get; init; }
}
