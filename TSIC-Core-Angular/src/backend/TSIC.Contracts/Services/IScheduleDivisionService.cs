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
    Task<ScheduleGridResponse> GetScheduleGridAsync(Guid jobId, Guid agegroupId, Guid divId, CancellationToken ct = default);

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
    /// Auto-schedule all round-robin pairings for a division.
    /// Deletes existing games first, then iterates pairings by round/game
    /// and places each into the next available timeslot.
    /// </summary>
    Task<AutoScheduleResponse> AutoScheduleDivAsync(Guid jobId, string userId, Guid divId, CancellationToken ct = default);
}
