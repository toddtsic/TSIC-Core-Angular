using TSIC.Contracts.Dtos.CampGroups;
using TSIC.Contracts.Dtos.CheckIn;

namespace TSIC.Contracts.Services;

/// <summary>
/// Live check-in (staff station). Replaces the legacy static check-in report family:
/// staff pull up the roster, see real-time balance and med-form status, take payment,
/// and record arrival. Two modes by job type — team check-in (Tournament / League) and
/// player check-in (Camp / Tryouts).
/// </summary>
public interface ICheckinService
{
    /// <summary>
    /// Active teams in the job with player counts — the team picker for player-mode
    /// (Camp / Tryouts) check-in. Mirrors the camp-groups left pane.
    /// </summary>
    Task<List<TeamRosterCountDto>> GetTeamsAsync(Guid jobId, CancellationToken ct = default);

    /// <summary>
    /// Team check-in roster (Tournament / League): every active team with clubrep
    /// balance and current check-in state.
    /// </summary>
    Task<List<TeamCheckinRowDto>> GetTeamRosterAsync(Guid jobId, CancellationToken ct = default);

    /// <summary>
    /// Player check-in roster (Camp / Tryouts): active players on a team with balance,
    /// med-form flag, and current check-in state. Caller authorizes team ↔ job pairing.
    /// </summary>
    Task<List<PlayerCheckinRowDto>> GetPlayerRosterAsync(Guid teamId, CancellationToken ct = default);

    /// <summary>Check a player in. Returns null when the registration is not in the job.</summary>
    Task<CheckinStateDto?> CheckInPlayerAsync(
        Guid jobId, Guid registrationId, Guid byRegId, string? userId, CancellationToken ct = default);

    /// <summary>Undo a player check-in. False when not in job or not checked in.</summary>
    Task<bool> UndoPlayerCheckInAsync(Guid jobId, Guid registrationId, CancellationToken ct = default);

    /// <summary>Check a team in. Returns null when the team is not in the job.</summary>
    Task<CheckinStateDto?> CheckInTeamAsync(
        Guid jobId, Guid teamId, Guid byRegId, string? userId, CancellationToken ct = default);

    /// <summary>Undo a team check-in. False when not in job or not checked in.</summary>
    Task<bool> UndoTeamCheckInAsync(Guid jobId, Guid teamId, CancellationToken ct = default);
}
