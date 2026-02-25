using TSIC.Contracts.Dtos.Scheduling;

namespace TSIC.Contracts.Repositories;

/// <summary>
/// Read-only repository for tournament parking/teams-on-site reporting data.
/// </summary>
public interface ITournamentParkingRepository
{
    /// <summary>
    /// Get per-team per-game presence records for a job.
    /// Each record identifies a team playing at a field complex at a specific time.
    /// </summary>
    Task<List<TeamGamePresenceDto>> GetTeamGamePresenceAsync(
        Guid jobId, CancellationToken ct = default);

    /// <summary>
    /// Get game-start intervals (minutes) keyed by agegroup ID.
    /// Used to compute departure time = lastGameStart + interval + departureBuffer.
    /// </summary>
    Task<Dictionary<Guid, int>> GetGameStartIntervalsAsync(
        Guid jobId, CancellationToken ct = default);
}
