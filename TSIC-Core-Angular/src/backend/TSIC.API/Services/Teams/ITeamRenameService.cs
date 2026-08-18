namespace TSIC.API.Services.Teams;

/// <summary>
/// THE single chokepoint for a team-name change. A team name lives in three places — the club-team
/// library row (<c>ClubTeams.ClubTeamName</c>, the seed every event copies at registration), the
/// per-job event copy (<c>Teams.TeamName</c>), and the denormalized schedule columns — and this
/// service keeps them in step, explicitly, replacing the old implicit <c>SaveChanges</c> trigger.
///
/// Two entries, two reaches, by construction (Todd's ruling, 2026-08-17):
/// <list type="bullet">
/// <item><see cref="RenameTeamAsync"/> — by per-job team id — is ALWAYS this event only. Every
/// admin door (LADT / Search Teams / Pairings / Schedule Hub) and the club rep's Registered Teams
/// pencil land here. No role reaches the library from an event.</item>
/// <item><see cref="RenameClubTeamAsync"/> — by library id — renames the library row and fans out
/// to every job's copy that still mirrors it. Reached only from the rep's own library edit, which
/// is closed once the team has event history.</item>
/// </list>
/// A job's copy may therefore diverge from the library, and a later library rename leaves such a
/// copy alone — the same "a copy naming something else is the record of intent" rule the
/// club-rename path applies to <c>Registrations.ClubName</c>. Reset = a rename back to the library name.
/// </summary>
public interface ITeamRenameService
{
    /// <summary>
    /// Library entry: rename by club-team id (the seed). Fans the new name out to every job's
    /// <c>Teams</c> copy that still mirrors the library, and their schedules.
    /// </summary>
    Task RenameClubTeamAsync(int clubTeamId, string newName, string userId, CancellationToken ct = default);

    /// <summary>
    /// Event entry: rename by the per-job team id — this job's row, its WAITLIST twin, and this
    /// job's schedule. The library and every other job are untouched (also the only reach an orphan
    /// team has). <paramref name="jobId"/> scopes the job-local work.
    /// </summary>
    Task RenameTeamAsync(Guid teamId, Guid jobId, string newName, string userId, CancellationToken ct = default);
}
