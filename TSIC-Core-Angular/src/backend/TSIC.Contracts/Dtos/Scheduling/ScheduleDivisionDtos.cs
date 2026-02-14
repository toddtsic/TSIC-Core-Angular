namespace TSIC.Contracts.Dtos.Scheduling;

// ── Response DTOs ──

/// <summary>
/// A single scheduled game for display in the schedule grid cell.
/// </summary>
public record ScheduleGameDto
{
    public required int Gid { get; init; }
    public required DateTime GDate { get; init; }
    public required Guid FieldId { get; init; }
    public required string FName { get; init; }
    public required int Rnd { get; init; }
    /// <summary>"U10:Gold" — agegroup:division label for grid cell display.</summary>
    public required string AgDivLabel { get; init; }
    /// <summary>Team 1 display label, e.g. "(T1) Storm Blue".</summary>
    public required string T1Label { get; init; }
    /// <summary>Team 2 display label, e.g. "(T2) Lonestar Red".</summary>
    public required string T2Label { get; init; }
    /// <summary>Agegroup color for the grid cell border/tint.</summary>
    public string? Color { get; init; }
    public required string T1Type { get; init; }
    public required string T2Type { get; init; }
    public int? T1No { get; init; }
    public int? T2No { get; init; }
    /// <summary>Resolved Team 1 ID — used for conflict detection.</summary>
    public Guid? T1Id { get; init; }
    /// <summary>Resolved Team 2 ID — used for conflict detection.</summary>
    public Guid? T2Id { get; init; }
    /// <summary>Division ID this game belongs to.</summary>
    public Guid? DivId { get; init; }
}

/// <summary>
/// One row in the schedule grid: a timeslot with one cell per field column.
/// </summary>
public record ScheduleGridRow
{
    public required DateTime GDate { get; init; }
    /// <summary>One cell per field column — null means open slot.</summary>
    public required List<ScheduleGameDto?> Cells { get; init; }
}

/// <summary>
/// Complete schedule grid data for a division: column definitions + rows.
/// </summary>
public record ScheduleGridResponse
{
    /// <summary>Field column definitions (FieldId + display name).</summary>
    public required List<ScheduleFieldColumn> Columns { get; init; }
    /// <summary>Grid rows: each row = one timeslot, cells = game or null (open).</summary>
    public required List<ScheduleGridRow> Rows { get; init; }
}

/// <summary>
/// A field column in the schedule grid.
/// </summary>
public record ScheduleFieldColumn
{
    public required Guid FieldId { get; init; }
    public required string FName { get; init; }
}

// ── Request DTOs ──

/// <summary>
/// Place a game from a pairing into a specific date/field slot.
/// </summary>
public record PlaceGameRequest
{
    /// <summary>PairingsLeagueSeason.Ai — which pairing to schedule.</summary>
    public required int PairingAi { get; init; }
    public required DateTime GDate { get; init; }
    public required Guid FieldId { get; init; }
    public required Guid AgegroupId { get; init; }
    public required Guid DivId { get; init; }
}

/// <summary>
/// Move an existing game to a new date/field. If the target slot is occupied, games are swapped.
/// </summary>
public record MoveGameRequest
{
    public required int Gid { get; init; }
    public required DateTime TargetGDate { get; init; }
    public required Guid TargetFieldId { get; init; }
}

/// <summary>
/// Delete all games for a division (with typed confirmation on the frontend).
/// </summary>
public record DeleteDivGamesRequest
{
    public required Guid DivId { get; init; }
}

/// <summary>
/// Auto-schedule result — how many games were placed vs failed to find slots.
/// </summary>
public record AutoScheduleResponse
{
    public required int TotalPairings { get; init; }
    public required int ScheduledCount { get; init; }
    public required int FailedCount { get; init; }
}

/// <summary>
/// Field address/directions for the public-facing field directions link.
/// </summary>
public record FieldDirectionsDto
{
    public required string Address { get; init; }
    public required string City { get; init; }
    public required string State { get; init; }
    public required string Zip { get; init; }
}
