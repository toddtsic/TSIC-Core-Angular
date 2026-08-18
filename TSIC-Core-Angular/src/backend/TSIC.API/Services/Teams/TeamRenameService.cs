using TSIC.Contracts.Repositories;
using TSIC.Domain.Entities;

namespace TSIC.API.Services.Teams;

/// <summary>
/// See <see cref="ITeamRenameService"/>. <c>ClubTeams.ClubTeamName</c> is the seed a future
/// registration copies; <c>Teams.TeamName</c> + the schedule columns are that event's own name.
/// The two are independent after registration — neither write sweeps into the other.
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
    /// ONE beat: the library row. A library rename reaches NO event, ever (Todd's ruling, 2026-08-18) —
    /// the list seeds FUTURE registrations, so rewriting a live event's schedule from a pick-list field
    /// is a side effect nobody standing in that field would predict. An event copy changes only when
    /// someone asks for it by name, through <see cref="RenameTeamAsync"/>.
    /// </summary>
    private async Task ApplyLibraryRenameAsync(
        int clubTeamId, ClubTeams? lib, string newName, string userId, CancellationToken ct)
    {
        if (lib == null) return;
        if (lib.ClubTeamName == newName) return;

        // Library identity guard (club + name + grad year). The list IS the product now, so two
        // entries with one identity is the fragmentation the library exists to prevent.
        var collision = await _clubTeamRepo.FindByIdentityAsync(lib.ClubId, newName, lib.ClubTeamGradYear, ct);
        if (collision != null && collision.ClubTeamId != clubTeamId)
            throw new InvalidOperationException(
                $"'{newName}' ({lib.ClubTeamGradYear}) is already in this club's library. Renaming does not merge teams.");

        lib.ClubTeamName = newName;
        lib.LebUserId = userId;
        lib.Modified = DateTime.Now;

        await _clubTeamRepo.SaveChangesAsync(ct);
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
