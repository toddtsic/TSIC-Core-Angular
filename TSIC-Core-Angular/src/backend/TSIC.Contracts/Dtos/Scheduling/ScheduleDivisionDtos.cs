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
    /// <summary>True when multiple games occupy the same (time, field) slot.</summary>
    public bool IsSlotCollision { get; init; }
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
    /// <summary>When set, only delete games on this specific date.</summary>
    public DateTime? GameDate { get; init; }
}

/// <summary>
/// Delete all games for every division in an agegroup.
/// </summary>
public record DeleteAgegroupGamesRequest
{
    public required Guid AgegroupId { get; init; }
    /// <summary>When set, only delete games on this specific date.</summary>
    public DateTime? GameDate { get; init; }
}

/// <summary>
/// Optional body for event-level undo (delete all games).
/// When GameDate is set, only games on that date are deleted.
/// </summary>
public record UndoGamesRequest
{
    public DateTime? GameDate { get; init; }
}

/// <summary>Distinct game date with count for the day picker UI.</summary>
public record GameDateInfoDto
{
    public required DateTime Date { get; init; }
    public required int GameCount { get; init; }
}

// ── Parking DTOs ──

/// <summary>
/// Park specific games into the 23:45–23:59 parking zone on their current day/field.
/// </summary>
public record BatchParkRequest
{
    public required List<int> Gids { get; init; }
}

/// <summary>
/// Park all championship/bracket games (non-T type) on a specific date.
/// </summary>
public record ParkAllChampionshipRequest
{
    public required DateTime GameDate { get; init; }
    /// <summary>Optional field filter. When null, parks on all fields.</summary>
    public List<Guid>? FieldIds { get; init; }
}

/// <summary>Result of a batch park operation.</summary>
public record BatchParkResult
{
    public required int ParkedCount { get; init; }
    public required List<ParkedGameInfo> ParkedGames { get; init; }
}

/// <summary>Info about a single parked game.</summary>
public record ParkedGameInfo
{
    public required int Gid { get; init; }
    public required DateTime OriginalGDate { get; init; }
    public required DateTime ParkedGDate { get; init; }
}

// ── Block Shift DTOs ──

/// <summary>
/// Shift a set of games by N rows within the grid's timeslot sequence.
/// </summary>
public record BatchShiftRequest
{
    /// <summary>Game IDs to shift.</summary>
    public required List<int> Gids { get; init; }
    /// <summary>Row offset: +N = down (later), -N = up (earlier).</summary>
    public required int RowOffset { get; init; }
    /// <summary>Ordered ISO datetime strings representing every timeslot row in the grid.</summary>
    public required List<DateTime> TimeslotSequence { get; init; }
    /// <summary>When true, returns preview without committing changes.</summary>
    public bool DryRun { get; init; }
}

/// <summary>Preview/result of a batch shift operation.</summary>
public record BatchShiftPreview
{
    public required List<GameShiftTarget> Moves { get; init; }
    public required List<ShiftConflict> Conflicts { get; init; }
    public required bool CanApply { get; init; }
    public required bool Applied { get; init; }
}

/// <summary>Where a game would move to.</summary>
public record GameShiftTarget
{
    public required int Gid { get; init; }
    public required DateTime OriginalGDate { get; init; }
    public required DateTime TargetGDate { get; init; }
    public required Guid FieldId { get; init; }
}

/// <summary>A conflict preventing a game from being shifted.</summary>
public record ShiftConflict
{
    public required int Gid { get; init; }
    public required DateTime TargetGDate { get; init; }
    public required Guid FieldId { get; init; }
    /// <summary>Human-readable reason, e.g. "Occupied by G1234 (U14 Boys)".</summary>
    public required string Reason { get; init; }
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
