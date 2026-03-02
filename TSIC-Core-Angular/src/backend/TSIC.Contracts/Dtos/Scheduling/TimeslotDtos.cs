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
}

/// <summary>
/// Field schedule defaults inherited from the prior-year sibling job.
/// Null if no prior-year job exists or if it had no timeslot configuration.
/// </summary>
public record PriorYearFieldDefaults
{
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

    /// <summary>Per-agegroup entries with wave assignment. Processed in list order.</summary>
    public required List<BulkDateAgegroupEntry> Entries { get; init; }

    /// <summary>Legacy flat list — ignored when Entries is populated.</summary>
    public List<Guid>? AgegroupIds { get; init; }
}

public record BulkDateAgegroupEntry
{
    public required Guid AgegroupId { get; init; }

    /// <summary>Wave group (1-3). Controls start time offset: wave N starts at
    /// StartTime + (N-1) × MaxGamesPerField × GamestartInterval minutes.</summary>
    public int Wave { get; init; } = 1;
}

public record BulkDateAssignResult
{
    public required Guid AgegroupId { get; init; }
    public required bool DateCreated { get; init; }
    public required int FieldTimeslotsCreated { get; init; }
}

public record BulkDateAssignResponse
{
    public required List<BulkDateAssignResult> Results { get; init; }
}
