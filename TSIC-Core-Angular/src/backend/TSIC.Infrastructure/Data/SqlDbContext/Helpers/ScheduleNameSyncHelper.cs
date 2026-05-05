using Microsoft.EntityFrameworkCore;

namespace TSIC.Infrastructure.Data.SqlDbContext.Helpers;

// Schedule.T1Name/T2Name are denormalized snapshots of "{clubName}:{teamName}"
// (or just teamName when Jobs.BShowTeamNameOnlyInSchedules is true). When a
// team is renamed, those snapshots must be rewritten. This helper applies the
// rewrite to the change tracker without calling SaveChanges, so the caller
// (DbContext.SaveChangesAsync override or ScheduleRepository) commits it as
// part of its own transaction.
internal static class ScheduleNameSyncHelper
{
    public static async Task ApplyTeamRenameToChangeTrackerAsync(
        SqlDbContext context,
        Guid teamId,
        Guid jobId,
        string newTeamName,
        CancellationToken cancellationToken)
    {
        // ClubrepRegistrationid + Job.BShowTeamNameOnlyInSchedules + Registration.ClubName
        // aren't being modified by a rename, so AsNoTracking is safe.
        var clubrepRegId = await context.Teams
            .AsNoTracking()
            .Where(t => t.TeamId == teamId)
            .Select(t => t.ClubrepRegistrationid)
            .FirstOrDefaultAsync(cancellationToken);

        string? clubName = null;
        if (clubrepRegId.HasValue)
        {
            clubName = await context.Registrations
                .AsNoTracking()
                .Where(r => r.RegistrationId == clubrepRegId.Value)
                .Select(r => r.ClubName)
                .FirstOrDefaultAsync(cancellationToken);
        }

        var showTeamNameOnly = await context.Jobs
            .AsNoTracking()
            .Where(j => j.JobId == jobId)
            .Select(j => j.BShowTeamNameOnlyInSchedules)
            .FirstOrDefaultAsync(cancellationToken);

        var displayName = (!string.IsNullOrEmpty(clubName) && !showTeamNameOnly)
            ? $"{clubName}:{newTeamName}"
            : newTeamName;

        var schedules = await context.Schedule
            .Where(s => (s.T1Id == teamId && s.T1Type == "T")
                     || (s.T2Id == teamId && s.T2Type == "T"))
            .ToListAsync(cancellationToken);

        foreach (var s in schedules)
        {
            if (s.T1Id == teamId && s.T1Type == "T") s.T1Name = displayName;
            if (s.T2Id == teamId && s.T2Type == "T") s.T2Name = displayName;
        }
    }
}
