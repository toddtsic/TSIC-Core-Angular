namespace TSIC.API.Services.Teams;

/// <summary>
/// THE single chokepoint for a team-name change. A team name lives in three places — the club-team
/// library row (<c>ClubTeams.ClubTeamName</c>, the seed every event copies at registration), the
/// per-job event copy (<c>Teams.TeamName</c>), and the denormalized schedule columns — and this
/// service keeps them in step, explicitly, replacing the old implicit <c>SaveChanges</c> trigger.
///
/// Two entries, NEITHER of which reaches the other, by construction (Todd's rulings, 2026-08-17/18):
/// <list type="bullet">
/// <item><see cref="RenameTeamAsync"/> — by per-job team id — is ALWAYS this event only. Every
/// admin door (LADT / Search Teams / Pairings / Schedule Hub) and the club rep's Registered Teams
/// pencil land here. No role reaches the library from an event.</item>
/// <item><see cref="RenameClubTeamAsync"/> — by library id — renames the library row and NOTHING
/// else. No event, no schedule, no other job.</item>
/// </list>
/// There is deliberately no sweep in either direction. The library is the seed a FUTURE registration
/// copies, not a mirror of live events: a rep fixing a typo in their pick list must not rewrite a
/// schedule they are not looking at. A caller that genuinely wants both — the rep's own rename
/// dialog, from either origin — makes two explicit calls, because the human ticked a box saying so.
/// A job's copy therefore diverges freely from the library, and neither side ever chases the other.
/// </summary>
public interface ITeamRenameService
{
    /// <summary>
    /// Library entry: rename by club-team id (the seed for future registrations). Writes
    /// <c>ClubTeams.ClubTeamName</c> and nothing else. Guarded by the club + name + grad year
    /// identity check so a rename can never merge two library entries.
    /// </summary>
    Task RenameClubTeamAsync(int clubTeamId, string newName, string userId, CancellationToken ct = default);

    /// <summary>
    /// Event entry: rename by the per-job team id — this job's row, its WAITLIST twin, and this
    /// job's schedule. The library and every other job are untouched (also the only reach an orphan
    /// team has). <paramref name="jobId"/> scopes the job-local work.
    /// </summary>
    Task RenameTeamAsync(Guid teamId, Guid jobId, string newName, string userId, CancellationToken ct = default);
}
