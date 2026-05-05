using Microsoft.EntityFrameworkCore;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Data.SqlDbContext.Helpers;

namespace TSIC.Infrastructure.Data.SqlDbContext;

// A team's name lives in three places:
//   1. ClubTeams.ClubTeamName       — library identity (one row per club team)
//   2. Teams.TeamName               — per-event registration (one row per job)
//   3. Schedule.T1Name / T2Name     — denormalized "{clubName}:{teamName}" for
//                                      round-robin games, one pair per scheduled game
// They must agree whenever Teams.ClubTeamId is set. This override mirrors any
// pending rename across all three layers inside the same SaveChanges transaction
// so every caller — library modal, admin LADT, admin search, pairings — honors
// the rule without needing to know it exists. Orphan Teams (ClubTeamId NULL)
// are untouched.
public partial class SqlDbContext
{
    public override async Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        await PropagateTeamNameChangesAsync(cancellationToken);
        return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private async Task PropagateTeamNameChangesAsync(CancellationToken cancellationToken)
    {
        var renamesByClubTeamId = new Dictionary<int, string>();

        // ClubTeams side wins when both have pending changes — library is the
        // identity-of-record, so a library edit is the authoritative source.
        foreach (var entry in ChangeTracker.Entries<ClubTeams>())
        {
            if (entry.State != EntityState.Modified) continue;
            if (!entry.Property(e => e.ClubTeamName).IsModified) continue;

            renamesByClubTeamId[entry.Entity.ClubTeamId] = entry.Entity.ClubTeamName;
        }

        foreach (var entry in ChangeTracker.Entries<Teams>())
        {
            if (entry.State != EntityState.Modified) continue;
            if (!entry.Entity.ClubTeamId.HasValue) continue;
            if (string.IsNullOrEmpty(entry.Entity.TeamName)) continue;
            if (!entry.Property(e => e.TeamName).IsModified) continue;

            var clubTeamId = entry.Entity.ClubTeamId.Value;
            if (!renamesByClubTeamId.ContainsKey(clubTeamId))
                renamesByClubTeamId[clubTeamId] = entry.Entity.TeamName!;
        }

        if (renamesByClubTeamId.Count == 0) return;

        // Every Teams row whose name landed on a new value, in this transaction,
        // needs its Schedule.T1Name/T2Name denormalization rewritten too.
        var teamsNeedingScheduleSync = new List<(Guid TeamId, Guid JobId, string NewName)>();

        foreach (var (clubTeamId, newName) in renamesByClubTeamId)
        {
            var trackedClubTeam = ChangeTracker.Entries<ClubTeams>()
                .FirstOrDefault(e => e.Entity.ClubTeamId == clubTeamId);
            if (trackedClubTeam != null)
            {
                if (trackedClubTeam.Entity.ClubTeamName != newName)
                    trackedClubTeam.Entity.ClubTeamName = newName;
            }
            else
            {
                var clubTeam = await Set<ClubTeams>()
                    .FirstOrDefaultAsync(c => c.ClubTeamId == clubTeamId, cancellationToken);
                if (clubTeam != null && clubTeam.ClubTeamName != newName)
                    clubTeam.ClubTeamName = newName;
            }

            // Pulls every Teams row across all jobs that link to this library team.
            // EF returns the already-tracked instance for rows already in the change
            // tracker, so the originating Teams entry isn't re-materialized.
            var siblings = await Set<Teams>()
                .Where(t => t.ClubTeamId == clubTeamId)
                .ToListAsync(cancellationToken);
            foreach (var sibling in siblings)
            {
                if (sibling.TeamName != newName)
                    sibling.TeamName = newName;

                teamsNeedingScheduleSync.Add((sibling.TeamId, sibling.JobId, newName));
            }
        }

        foreach (var (teamId, jobId, newName) in teamsNeedingScheduleSync)
        {
            await ScheduleNameSyncHelper.ApplyTeamRenameToChangeTrackerAsync(
                this, teamId, jobId, newName, cancellationToken);
        }
    }
}
