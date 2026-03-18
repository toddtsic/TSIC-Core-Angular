using TSIC.Contracts.Dtos.Scheduling;

namespace TSIC.Contracts.Services;

/// <summary>
/// Service for the Schedule by Division tool (009-4).
/// Handles manual game placement, move/swap, and grid assembly.
/// </summary>
public interface IScheduleDivisionService
{
    /// <summary>
    /// Build the schedule grid for a division: timeslot rows × field columns.
    /// Shows all games across all divisions that fall on the agegroup's configured dates/fields.
    /// </summary>
    Task<ScheduleGridResponse> GetScheduleGridAsync(Guid jobId, Guid agegroupId, Guid divId,
        DateTime? additionalTimeslot = null, CancellationToken ct = default);

    /// <summary>
    /// Build the event-level schedule grid: all fields × all timeslots across all agegroups.
    /// Shows every configured slot (empty + filled) for the entire event.
    /// </summary>
    Task<ScheduleGridResponse> GetEventGridAsync(Guid jobId, CancellationToken ct = default);

    /// <summary>
    /// Place a game from a pairing into a specific date/field slot.
    /// Creates a Schedule record and runs RecalcValues (UpdateGameIds).
    /// </summary>
    Task<ScheduleGameDto> PlaceGameAsync(Guid jobId, string userId, PlaceGameRequest request, CancellationToken ct = default);

    /// <summary>
    /// Move a game to a new date/field. If the target slot is occupied, the two games are swapped.
    /// </summary>
    Task MoveGameAsync(string userId, MoveGameRequest request, CancellationToken ct = default);

    /// <summary>
    /// Delete a single game with cascade cleanup (DeviceGids → BracketSeeds → Schedule).
    /// </summary>
    Task DeleteGameAsync(int gid, CancellationToken ct = default);

    /// <summary>
    /// Delete all games for a division with cascade cleanup.
    /// </summary>
    Task DeleteDivisionGamesAsync(Guid jobId, DeleteDivGamesRequest request, CancellationToken ct = default);

    /// <summary>
    /// Delete all games for every division in an agegroup with cascade cleanup.
    /// </summary>
    Task DeleteAgegroupGamesAsync(Guid jobId, DeleteAgegroupGamesRequest request, CancellationToken ct = default);

    /// <summary>
    /// Get distinct game dates with counts for the day picker, scoped by agegroup or division.
    /// </summary>
    Task<List<GameDateInfoDto>> GetGameDatesAsync(Guid jobId, Guid? agegroupId, Guid? divId, CancellationToken ct = default);

    /// <summary>
    /// Auto-schedule all round-robin pairings for a division.
    /// Deletes existing games first, then iterates pairings by round/game
    /// and places each into the next available timeslot.
    /// </summary>
    Task<AutoScheduleResponse> AutoScheduleDivAsync(Guid jobId, string userId, Guid divId, CancellationToken ct = default);

    // ── Batch Operations ──

    /// <summary>
    /// Park specific games into the 23:45–23:59 parking zone on their current day/field.
    /// Each game's GDate time is moved to the first free minute >= 23:45 on the same day and field.
    /// </summary>
    Task<BatchParkResult> ParkGamesAsync(Guid jobId, string userId, BatchParkRequest request, CancellationToken ct = default);

    /// <summary>
    /// Park all championship/bracket games (T1Type != "T" or T2Type != "T") on a specific date.
    /// </summary>
    Task<BatchParkResult> ParkAllChampionshipAsync(Guid jobId, string userId, ParkAllChampionshipRequest request, CancellationToken ct = default);

    /// <summary>
    /// Shift a block of games by N rows within the grid's timeslot sequence.
    /// Supports dry-run mode for preview without committing.
    /// Rejects if any selected game has non-T team types.
    /// Detects collisions with ALL games (cross-agegroup safe).
    /// </summary>
    Task<BatchShiftPreview> BatchShiftAsync(Guid jobId, string userId, BatchShiftRequest request, CancellationToken ct = default);
}
