using Microsoft.EntityFrameworkCore;
using TSIC.Domain.Entities;

namespace TSIC.Infrastructure.Data.SqlDbContext;

// A team's name lives in two columns: ClubTeams.ClubTeamName (library identity)
// and Teams.TeamName (per-event registration). They must agree whenever a Teams
// row links to a ClubTeams row (Teams.ClubTeamId is set). This override mirrors
// any pending rename across both columns inside the same SaveChanges transaction
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
            }
        }
    }
}
