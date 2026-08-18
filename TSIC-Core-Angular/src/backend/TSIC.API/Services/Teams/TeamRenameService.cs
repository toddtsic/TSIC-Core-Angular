using TSIC.Contracts.Repositories;
using TSIC.Domain.Entities;

namespace TSIC.API.Services.Teams;

/// <summary>
/// See <see cref="ITeamRenameService"/>. Mirrors <c>ClubService.AdminRenameClubAsync</c> one level down:
/// <c>ClubTeams.ClubTeamName</c> is the seed, <c>Teams.TeamName</c> + the schedule columns are the
/// per-event copies — and a copy renamed for its event is left alone by the library fan-out.
/// Reads committed state and writes explicitly — no <c>SaveChanges</c> hook.
/// </summary>
public sealed class TeamRenameService : ITeamRenameService
{
    private readonly ITeamRepository _teamRepo;
    private readonly IClubTeamRepository _clubTeamRepo;
    private readonly IScheduleRepository _scheduleRepo;

    public TeamRenameService(
        ITeamRepository teamRepo,
        IClubTeamRepository clubTeamRepo,
        IScheduleRepository scheduleRepo)
    {
        _teamRepo = teamRepo;
        _clubTeamRepo = clubTeamRepo;
        _scheduleRepo = scheduleRepo;
    }

    public async Task RenameClubTeamAsync(int clubTeamId, string newName, string userId, CancellationToken ct = default)
    {
        var lib = await _clubTeamRepo.GetByIdAsync(clubTeamId, ct);
        await ApplyLibraryRenameAsync(clubTeamId, lib, newName, userId, ct);
    }

    public async Task RenameTeamAsync(
        Guid teamId, Guid jobId, string newName, string userId, CancellationToken ct = default)
    {
        var team = await _teamRepo.GetTeamFromTeamId(teamId, ct);
        if (team == null) return;

        // This event only — own row, this job's WAITLIST twin, this job's schedule. There is
        // deliberately no path from a per-job team id to the library: no admin role gets one.
        var oldName = team.TeamName ?? string.Empty;
        Stamp(team, newName, userId);
        await RenameTwinInJobAsync(jobId, oldName, newName, userId, ct);
        await _scheduleRepo.RecomposeScheduleNamesForJobAsync(jobId, team: (teamId, newName), ct: ct);
        await _teamRepo.SaveChangesAsync(ct);
    }

    /// <summary>
    /// The three beats for a library-owned team: (1) the library source, (2) every job's event copy that
    /// still mirrors the library and its WAITLIST twin, (3) each such job's schedule. The per-job canonical
    /// writer keys on the job's own TeamId — which is why this loops rows rather than passing a single
    /// pair to RecomposeAcrossJobs. The OLD name is the library's, never a copy's — a copy may already
    /// carry a director's this-event name, and that copy is skipped: their decision stands until they
    /// reset it.
    /// </summary>
    private async Task ApplyLibraryRenameAsync(
        int clubTeamId, ClubTeams? lib, string newName, string userId, CancellationToken ct)
    {
        var oldName = lib?.ClubTeamName ?? string.Empty;
        if (oldName == newName) return;

        // Library identity guard (club + name + grad year) at the chokepoint, so every door — library
        // modal, admin search, LADT, pairings — refuses to rename onto a sibling. Renaming never merges.
        if (lib != null)
        {
            var collision = await _clubTeamRepo.FindByIdentityAsync(lib.ClubId, newName, lib.ClubTeamGradYear, ct);
            if (collision != null && collision.ClubTeamId != clubTeamId)
                throw new InvalidOperationException(
                    $"'{newName}' ({lib.ClubTeamGradYear}) is already in this club's library. Renaming does not merge teams.");
        }

        // Beat 1 — library source (identity of record).
        if (lib != null)
        {
            lib.ClubTeamName = newName;
            lib.LebUserId = userId;
            lib.Modified = DateTime.Now;
        }

        // Beats 2 + 3 — every event copy still holding the library name, its twin, then that job's schedule.
        var copies = await _teamRepo.GetTrackedTeamsByClubTeamIdAsync(clubTeamId, ct);
        foreach (var t in copies)
        {
            if (!string.Equals(t.TeamName, oldName, StringComparison.Ordinal))
                continue; // diverged for its event — the director's name stands

            Stamp(t, newName, userId);
            await RenameTwinInJobAsync(t.JobId, oldName, newName, userId, ct);
            await _scheduleRepo.RecomposeScheduleNamesForJobAsync(t.JobId, team: (t.TeamId, newName), ct: ct);
        }

        // Flush the library row + any copy whose job had no schedule rows (canonical skips those).
        await _teamRepo.SaveChangesAsync(ct);
    }

    /// <summary>
    /// The player-overflow twin carries no ClubTeamId (so the fan-out above misses it) and is matched by
    /// its derived name. Carry the WAITLIST prefix onto the new name. No twin → nothing to do.
    /// </summary>
    private async Task RenameTwinInJobAsync(Guid jobId, string oldName, string newName, string userId, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(oldName)) return;

        var twin = await _teamRepo.GetTrackedTeamByNameInJobAsync(jobId, $"WAITLIST - {oldName}", ct);
        if (twin != null)
            Stamp(twin, $"WAITLIST - {newName}", userId);
    }

    private static void Stamp(TSIC.Domain.Entities.Teams team, string name, string userId)
    {
        if (team.TeamName == name) return;
        team.TeamName = name;
        team.LebUserId = userId;
        team.Modified = DateTime.Now;
    }
}
