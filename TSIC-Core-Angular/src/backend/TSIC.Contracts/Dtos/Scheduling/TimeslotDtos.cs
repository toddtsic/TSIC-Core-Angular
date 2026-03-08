namespace TSIC.Contracts.Dtos.Scheduling;

// ── Response DTOs ──

public record TimeslotDateDto
{
    public required int Ai { get; init; }
    public required Guid AgegroupId { get; init; }
    public required DateTime GDate { get; init; }
    public required int Rnd { get; init; }
    public Guid? DivId { get; init; }
    public string? DivName { get; init; }
}

public record TimeslotFieldDto
{
    public required int Ai { get; init; }
    public required Guid AgegroupId { get; init; }
    public required Guid FieldId { get; init; }
    public required string FieldName { get; init; }
    public required string StartTime { get; init; }
    public required int GamestartInterval { get; init; }
    public required int MaxGamesPerField { get; init; }
    public required string Dow { get; init; }
    public Guid? DivId { get; init; }
    public string? DivName { get; init; }
}

public record TimeslotConfigurationResponse
{
    public required List<TimeslotDateDto> Dates { get; init; }
    public required List<TimeslotFieldDto> Fields { get; init; }
}

public record CapacityPreviewDto
{
    public required string Dow { get; init; }
    public required int FieldCount { get; init; }
    public required int TotalGameSlots { get; init; }
    public required int GamesNeeded { get; init; }
    public required bool IsSufficient { get; init; }
}

// ── Readiness ──

/// <summary>
/// Per game-day scheduling parameters for the event summary dashboard.
/// One entry per actual date in the timeslot config.
/// </summary>
public record GameDayDto
{
    /// <summary>Ordinal game day number (1-based).</summary>
    public required int DayNumber { get; init; }

    /// <summary>Actual calendar date.</summary>
    public required DateTime Date { get; init; }

    /// <summary>Day of week abbreviation (e.g. "Sat").</summary>
    public required string Dow { get; init; }

    /// <summary>Number of distinct fields scheduled on this DOW.</summary>
    public required int FieldCount { get; init; }

    /// <summary>Earliest start time (e.g. "08:00 AM").</summary>
    public required string StartTime { get; init; }

    /// <summary>Calculated end time: startTime + (maxGamesPerField × GSI minutes).</summary>
    public required string EndTime { get; init; }

    /// <summary>Game-start interval in minutes.</summary>
    public required int Gsi { get; init; }

    /// <summary>Total game slots for this DOW (sum of MaxGamesPerField across all fields).</summary>
    public required int TotalSlots { get; init; }

    /// <summary>Number of round-slots configured on this date (row count in TimeslotsLeagueSeasonDates).</summary>
    public required int RoundCount { get; init; }
}

public record AgegroupCanvasReadinessDto
{
    public required Guid AgegroupId { get; init; }
    public required int DateCount { get; init; }
    public required int FieldCount { get; init; }
    public required bool IsConfigured { get; init; }

    /// <summary>Distinct days of week (e.g. ["Saturday", "Sunday"]).</summary>
    public required List<string> DaysOfWeek { get; init; }

    /// <summary>Game-start interval in minutes (null if mixed across fields).</summary>
    public int? GamestartInterval { get; init; }

    /// <summary>Start time (null if mixed across fields).</summary>
    public string? StartTime { get; init; }

    /// <summary>Max games per field (null if mixed across fields).</summary>
    public int? MaxGamesPerField { get; init; }

    /// <summary>Total game slots = sum of MaxGamesPerField across all field-timeslot rows × date count per DOW.</summary>
    public required int TotalGameSlots { get; init; }

    /// <summary>Per game-day breakdown with dates, field counts, time ranges, and GSI.</summary>
    public required List<GameDayDto> GameDays { get; init; }

    /// <summary>Total round-slots configured across all dates.</summary>
    public required int TotalRounds { get; init; }

    /// <summary>Max round number from current RR pairings (T1Type='T'). 0 if no pairings.</summary>
    public required int MaxPairingRound { get; init; }

    /// <summary>Resolved game guarantee: agegroup override ?? job default ?? null (full RR).</summary>
    public int? GameGuarantee { get; init; }

    /// <summary>Distinct field IDs this agegroup has field-timeslot rows for. Empty = unconfigured.</summary>
    public required List<Guid> FieldIds { get; init; }
}

/// <summary>
/// Field schedule defaults inherited from the prior-year sibling job.
/// Null if no prior-year job exists or if it had no timeslot configuration.
/// </summary>
public record PriorYearFieldDefaults
{
    public required Guid PriorJobId { get; init; }
    public required string StartTime { get; init; }
    public required int GamestartInterval { get; init; }
    public required int MaxGamesPerField { get; init; }
    public required string PriorJobName { get; init; }
    public required string PriorYear { get; init; }
}

public record CanvasReadinessResponse
{
    public required List<AgegroupCanvasReadinessDto> Agegroups { get; init; }

    /// <summary>
    /// Number of fields assigned to this league-season (FieldsLeagueSeason count).
    /// Zero means Manage Fields must be completed before field schedules can be created.
    /// </summary>
    public required int AssignedFieldCount { get; init; }

    /// <summary>
    /// Field schedule defaults from the prior-year sibling job (same customer/type/sport/season).
    /// Null when no prior-year job exists or when it had no timeslot config.
    /// Used as fallback when the current job has no configured agegroups yet.
    /// </summary>
    public PriorYearFieldDefaults? PriorYearDefaults { get; init; }

    /// <summary>
    /// Per-agegroup total round counts from prior-year job, keyed by agegroup name.
    /// Null when no prior-year job exists or when it had no timeslot dates.
    /// </summary>
    public Dictionary<string, int>? PriorYearRounds { get; init; }

    /// <summary>
    /// All event-level fields (from FieldsLeagueSeason), with ID and name.
    /// Used by the field config section to display the full field list.
    /// </summary>
    public required List<EventFieldSummaryDto> EventFields { get; init; }
}

// ── Request DTOs ──

public record AddTimeslotDateRequest
{
    public required Guid AgegroupId { get; init; }
    public required DateTime GDate { get; init; }
    public required int Rnd { get; init; }
    public Guid? DivId { get; init; }
}

public record EditTimeslotDateRequest
{
    public required int Ai { get; init; }
    public required DateTime GDate { get; init; }
    public required int Rnd { get; init; }
}

public record AddTimeslotFieldRequest
{
    public required Guid AgegroupId { get; init; }
    /// <summary>null = create for ALL assigned fields (cartesian product).</summary>
    public Guid? FieldId { get; init; }
    /// <summary>null = create for ALL active divisions in agegroup.</summary>
    public Guid? DivId { get; init; }
    public required string StartTime { get; init; }
    public required int GamestartInterval { get; init; }
    public required int MaxGamesPerField { get; init; }
    public required string Dow { get; init; }
}

public record EditTimeslotFieldRequest
{
    public required int Ai { get; init; }
    public required string StartTime { get; init; }
    public required int GamestartInterval { get; init; }
    public required int MaxGamesPerField { get; init; }
    public required string Dow { get; init; }
    public Guid? FieldId { get; init; }
    public Guid? DivId { get; init; }
}

public record CloneDatesRequest
{
    public required Guid SourceAgegroupId { get; init; }
    public required Guid TargetAgegroupId { get; init; }
}

public record CloneFieldsRequest
{
    public required Guid SourceAgegroupId { get; init; }
    public required Guid TargetAgegroupId { get; init; }
}

public record CloneByFieldRequest
{
    public required Guid AgegroupId { get; init; }
    public required Guid SourceFieldId { get; init; }
    public required Guid TargetFieldId { get; init; }
}

public record CloneByDivisionRequest
{
    public required Guid AgegroupId { get; init; }
    public required Guid SourceDivId { get; init; }
    public required Guid TargetDivId { get; init; }
}

public record CloneByDowRequest
{
    public required Guid AgegroupId { get; init; }
    public required string SourceDow { get; init; }
    public required string TargetDow { get; init; }
    /// <summary>null = keep source start time.</summary>
    public string? NewStartTime { get; init; }
}

public record CloneDateRecordRequest
{
    public required int Ai { get; init; }
    /// <summary>"day" (+1 day), "week" (+7 days), or "round" (same date, rnd+1).</summary>
    public required string CloneType { get; init; }
}

public record CloneFieldDowRequest
{
    public required int Ai { get; init; }
}

public record DeleteAgegroupTimeslotsRequest
{
    public required Guid AgegroupId { get; init; }
}

// ── Bulk Date Assignment ──

public record BulkDateAssignRequest
{
    public required DateTime GDate { get; init; }
    public required string StartTime { get; init; }
    public required int GamestartInterval { get; init; }
    public required int MaxGamesPerField { get; init; }

    /// <summary>How many sequential rounds to create on this date (default 1).
    /// Additive: if the agegroup already has some rounds for this date, only the
    /// missing ones are added.</summary>
    public int RoundsPerDay { get; init; } = 1;

    /// <summary>Per-agegroup entries with wave assignment. Processed in list order.</summary>
    public required List<BulkDateAgegroupEntry> Entries { get; init; }

    /// <summary>Legacy flat list — ignored when Entries is populated.</summary>
    public List<Guid>? AgegroupIds { get; init; }

    /// <summary>Agegroup IDs to REMOVE from this date. Used when editing an existing
    /// date — unchecked agegroups that previously had this date will have their
    /// date entries deleted.</summary>
    public List<Guid>? RemovedAgegroupIds { get; init; }
}

public record BulkDateAgegroupEntry
{
    public required Guid AgegroupId { get; init; }

    /// <summary>Wave group (1-3). Controls start time offset: wave N starts at
    /// StartTime + (N-1) × MaxGamesPerField × GamestartInterval minutes.</summary>
    public int Wave { get; init; } = 1;

    /// <summary>Rounds to assign on this date for this agegroup.
    /// Overrides request-level RoundsPerDay when set. Null = use request-level default.</summary>
    public int? RoundsPerDay { get; init; }
}

public record BulkDateAssignResult
{
    public required Guid AgegroupId { get; init; }
    public required bool DateCreated { get; init; }
    public required int RoundsCreated { get; init; }
    public required int FieldTimeslotsCreated { get; init; }
}

public record BulkDateAssignResponse
{
    public required List<BulkDateAssignResult> Results { get; init; }
}

// ── Field Config Update (Time Config section ③) ──

/// <summary>
/// Bulk-update GSI, StartTime, and/or MaxGamesPerField on existing field timeslot rows.
/// Supports uniform mode (apply to all agegroups with wave-adjusted start times)
/// and per-agegroup mode (client provides pre-calculated values per AG).
/// Does NOT touch TimeslotsLeagueSeasonDates — preserves R/day and wave assignments.
/// </summary>
public record UpdateFieldConfigRequest
{
    /// <summary>New base start time (Wave 1). Wave 2+ agegroups get auto-adjusted.
    /// Null = don't change start times (unless GSI/MaxGames change, which triggers wave recalc).</summary>
    public string? BaseStartTime { get; init; }

    /// <summary>New GSI in minutes. Null = don't change.</summary>
    public int? GamestartInterval { get; init; }

    /// <summary>New max games per field. Null = don't change.</summary>
    public int? MaxGamesPerField { get; init; }

    /// <summary>Per-agegroup overrides. When provided, each entry specifies values for that agegroup.
    /// Takes precedence over uniform values above. Agegroups not listed keep their current values.</summary>
    public List<FieldConfigAgegroupEntry>? Entries { get; init; }

    /// <summary>Per-DOW overrides for uniform mode. When present, each entry provides
    /// DOW-specific BaseStartTime and/or MaxGamesPerField. DOWs not listed fall back
    /// to the global BaseStartTime/MaxGamesPerField above.</summary>
    public List<DowConfigOverride>? DowOverrides { get; init; }

    /// <summary>Per-agegroup-per-DOW overrides. Each entry specifies StartTime and/or
    /// MaxGamesPerField for a specific agegroup on a specific day-of-week.
    /// Takes precedence over both uniform values and DowOverrides.
    /// Used by the time config matrix (agegroup × date grid).</summary>
    public List<AgDowFieldConfigEntry>? AgDowOverrides { get; init; }
}

/// <summary>Per day-of-week override values for uniform mode.
/// Allows different start times and max games on different play days (e.g., Sat 7:30 AM, Sun 8:00 AM).</summary>
public record DowConfigOverride
{
    /// <summary>Day of week name (e.g., "Saturday", "Sunday"). Case-insensitive matching.</summary>
    public required string Dow { get; init; }

    /// <summary>Wave 1 start time for this DOW. Null = use global BaseStartTime.</summary>
    public string? BaseStartTime { get; init; }

    /// <summary>Max games per field for this DOW. Null = use global MaxGamesPerField.</summary>
    public int? MaxGamesPerField { get; init; }
}

/// <summary>Per-agegroup field config override. Client sends pre-calculated values
/// (including wave-adjusted StartTime when applicable).</summary>
public record FieldConfigAgegroupEntry
{
    public required Guid AgegroupId { get; init; }

    /// <summary>Direct start time value (already wave-adjusted by client). Null = don't change.</summary>
    public string? StartTime { get; init; }

    /// <summary>GSI in minutes. Null = don't change.</summary>
    public int? GamestartInterval { get; init; }

    /// <summary>Max games per field. Null = don't change.</summary>
    public int? MaxGamesPerField { get; init; }
}

/// <summary>Per-agegroup-per-DOW field config override. Allows the time config matrix
/// to set different StartTime and MaxGamesPerField for each agegroup on each play day.
/// The client sends DOW (not date) because the DB schema keys on DOW.</summary>
public record AgDowFieldConfigEntry
{
    public required Guid AgegroupId { get; init; }

    /// <summary>Day of week name (e.g., "Saturday"). Case-insensitive matching.</summary>
    public required string Dow { get; init; }

    /// <summary>Wave 1 start time for this agegroup on this DOW. Null = don't change.</summary>
    public string? StartTime { get; init; }

    /// <summary>Max games per field for this agegroup on this DOW. Null = don't change.</summary>
    public int? MaxGamesPerField { get; init; }
}

public record UpdateFieldConfigResponse
{
    public required int RowsUpdated { get; init; }
}

// ── Field Assignment Management (Field Config section ①) ──

/// <summary>
/// Lightweight field summary for the readiness response.
/// One entry per field assigned to the event's league-season (from FieldsLeagueSeason).
/// </summary>
public record EventFieldSummaryDto
{
    public required Guid FieldId { get; init; }
    public required string FieldName { get; init; }
}

/// <summary>
/// Reconcile which fields each agegroup uses.
/// Only agegroups with overrides (fewer than all event fields) need entries.
/// </summary>
public record SaveFieldAssignmentsRequest
{
    public required List<AgegroupFieldAssignmentEntry> Entries { get; init; }
}

/// <summary>Per-agegroup field assignment: which of the event fields this agegroup should use.</summary>
public record AgegroupFieldAssignmentEntry
{
    public required Guid AgegroupId { get; init; }

    /// <summary>Field IDs this agegroup should use. Empty list = use NO fields (unusual but valid).</summary>
    public required List<Guid> FieldIds { get; init; }
}

public record SaveFieldAssignmentsResponse
{
    public required int RowsCreated { get; init; }
    public required int RowsDeleted { get; init; }
}
