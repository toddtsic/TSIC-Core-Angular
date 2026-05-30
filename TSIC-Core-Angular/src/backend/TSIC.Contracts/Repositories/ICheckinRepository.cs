using TSIC.Contracts.Dtos.CheckIn;

namespace TSIC.Contracts.Repositories;

/// <summary>
/// Data access for live check-in state (checkin.PlayerCheckIns / checkin.TeamCheckIns).
/// Roster reads LEFT JOIN the check-in row so not-yet-arrived registrants/teams still
/// surface. Writes are idempotent upserts (one row per target) and enforce job scope.
/// </summary>
public interface ICheckinRepository
{
    /// <summary>
    /// All active teams in the job with clubrep balance and current check-in state
    /// (Tournament / League team check-in). Ordered by agegroup then team name.
    /// </summary>
    Task<List<TeamCheckinRowDto>> GetTeamRosterByJobAsync(Guid jobId, CancellationToken ct = default);

    /// <summary>
    /// All active Player registrations on a team with balance, med-form flag, and
    /// current check-in state (Camp / Tryouts player check-in). Caller authorizes
    /// the team ↔ job pairing.
    /// </summary>
    Task<List<PlayerCheckinRowDto>> GetPlayerRosterByTeamAsync(Guid teamId, CancellationToken ct = default);

    /// <summary>
    /// Check a player in (insert or refresh their check-in row). Returns null when the
    /// registration is not in the caller's job; otherwise the resulting state.
    /// </summary>
    Task<CheckinStateDto?> UpsertPlayerCheckinAsync(
        Guid jobId, Guid registrationId, Guid byRegId, string? userId, CancellationToken ct = default);

    /// <summary>
    /// Undo a player check-in (delete the row). Returns false when the registration is
    /// not in the caller's job or was not checked in.
    /// </summary>
    Task<bool> UndoPlayerCheckinAsync(Guid jobId, Guid registrationId, CancellationToken ct = default);

    /// <summary>
    /// Check a team in (insert or refresh its check-in row). Returns null when the team
    /// is not in the caller's job; otherwise the resulting state.
    /// </summary>
    Task<CheckinStateDto?> UpsertTeamCheckinAsync(
        Guid jobId, Guid teamId, Guid byRegId, string? userId, CancellationToken ct = default);

    /// <summary>
    /// Undo a team check-in (delete the row). Returns false when the team is not in the
    /// caller's job or was not checked in.
    /// </summary>
    Task<bool> UndoTeamCheckinAsync(Guid jobId, Guid teamId, CancellationToken ct = default);
}
