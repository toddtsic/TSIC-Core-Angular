namespace TSIC.API.Services.Teams;

/// <summary>
/// THE single chokepoint for a team-name change. A team name lives in three places — the club-team
/// library row (<c>ClubTeams.ClubTeamName</c>, the identity of record), the per-job event copy
/// (<c>Teams.TeamName</c>), and the denormalized schedule columns — and this service keeps all three
/// in step, explicitly, replacing the old implicit <c>SaveChanges</c> trigger. Every rename does the
/// same three beats: write the library source, refresh the event copies (+ the WAITLIST twins), then
/// recompose each affected job's schedule.
/// </summary>
public interface ITeamRenameService
{
    /// <summary>
    /// Library-modal entry: rename by club-team id (the identity of record). Fans the new name out to
    /// every job's <c>Teams</c> copy and their schedules.
    /// </summary>
    Task RenameClubTeamAsync(int clubTeamId, string newName, string userId, CancellationToken ct = default);

    /// <summary>
    /// Event entry (LADT / admin search / pairings): rename by the per-job team id. Resolves the team's
    /// club-team id and fans out identically; an orphan team (no <c>ClubTeamId</c>) renames its own row
    /// and this job's schedule only. <paramref name="jobId"/> scopes the orphan/twin work.
    /// </summary>
    Task RenameTeamAsync(Guid teamId, Guid jobId, string newName, string userId, CancellationToken ct = default);
}
