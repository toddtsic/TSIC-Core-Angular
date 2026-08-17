namespace TSIC.API.Services.Teams;

/// <summary>
/// How far a rename reaches. <see cref="ThisJob"/> writes this job's <c>Teams</c> row only — the
/// per-event name a director controls; the club-team library and every other job keep their name.
/// <see cref="Library"/> writes the library row and fans out to every job's copy that still mirrors it.
/// </summary>
public enum TeamRenameScope
{
    /// <summary>This event only: own row, this job's WAITLIST twin, this job's schedule.</summary>
    ThisJob,

    /// <summary>The club-team library row + every job's copy still holding the library name.</summary>
    Library,
}

/// <summary>
/// THE single chokepoint for a team-name change. A team name lives in three places — the club-team
/// library row (<c>ClubTeams.ClubTeamName</c>, the identity of record), the per-job event copy
/// (<c>Teams.TeamName</c>), and the denormalized schedule columns — and this service keeps them in
/// step, explicitly, replacing the old implicit <c>SaveChanges</c> trigger.
///
/// A job's copy may deliberately diverge from the library: a director can rename a club-linked team
/// <b>for their event only</b> (<see cref="TeamRenameScope.ThisJob"/>). A later library rename leaves
/// such a copy alone — the same "a copy naming something else is the record of intent" rule the
/// club-rename path applies to <c>Registrations.ClubName</c>. Reset = a ThisJob rename back to the
/// library name.
/// </summary>
public interface ITeamRenameService
{
    /// <summary>
    /// Library-modal entry: rename by club-team id (the identity of record). Fans the new name out to
    /// every job's <c>Teams</c> copy that still mirrors the library, and their schedules.
    /// </summary>
    Task RenameClubTeamAsync(int clubTeamId, string newName, string userId, CancellationToken ct = default);

    /// <summary>
    /// Event entry (LADT / admin search / pairings): rename by the per-job team id.
    /// <see cref="TeamRenameScope.ThisJob"/> renames this job's row, its WAITLIST twin, and this job's
    /// schedule — the library and other jobs are untouched (also the only reach an orphan team has).
    /// <see cref="TeamRenameScope.Library"/> on a club-linked team resolves the library row and fans out.
    /// <paramref name="jobId"/> scopes the job-local work.
    /// </summary>
    Task RenameTeamAsync(Guid teamId, Guid jobId, string newName, string userId, TeamRenameScope scope, CancellationToken ct = default);
}
