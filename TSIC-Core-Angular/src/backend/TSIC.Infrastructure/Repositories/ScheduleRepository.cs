using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Repositories;
using TSIC.Infrastructure.Data.SqlDbContext;

namespace TSIC.Infrastructure.Repositories;

/// <summary>
/// Repository for Schedule entity using Entity Framework Core.
/// </summary>
public sealed class ScheduleRepository : IScheduleRepository
{
    private readonly SqlDbContext _context;

    public ScheduleRepository(SqlDbContext context)
    {
        _context = context;
    }

    public async Task SynchronizeScheduleNamesForTeamAsync(Guid teamId, Guid jobId, CancellationToken ct = default)
    {
        // 1. Get team's current TeamName + ClubrepRegistrationid
        var team = await _context.Teams
            .AsNoTracking()
            .Where(t => t.TeamId == teamId)
            .Select(t => new { t.TeamName, t.ClubrepRegistrationid })
            .FirstOrDefaultAsync(ct);
        if (team == null) return;

        // 2. Get club name from Registration (if team has club rep context)
        string? clubName = null;
        if (team.ClubrepRegistrationid.HasValue)
        {
            clubName = await _context.Registrations
                .AsNoTracking()
                .Where(r => r.RegistrationId == team.ClubrepRegistrationid.Value)
                .Select(r => r.ClubName)
                .FirstOrDefaultAsync(ct);
        }

        // 3. Get job's BShowTeamNameOnlyInSchedules setting
        var showTeamNameOnly = await _context.Jobs
            .AsNoTracking()
            .Where(j => j.JobId == jobId)
            .Select(j => j.BShowTeamNameOnlyInSchedules)
            .FirstOrDefaultAsync(ct);

        // 4. Compose the display name (matches write-time format from scheduler)
        var displayName = (!string.IsNullOrEmpty(clubName) && !showTeamNameOnly)
            ? $"{clubName}:{team.TeamName}"
            : team.TeamName ?? "";

        // 5. Update round-robin games only (T1Type/T2Type == "T")
        var schedules = await _context.Schedule
            .Where(s => (s.T1Id == teamId && s.T1Type == "T")
                     || (s.T2Id == teamId && s.T2Type == "T"))
            .ToListAsync(ct);

        foreach (var s in schedules)
        {
            if (s.T1Id == teamId && s.T1Type == "T") s.T1Name = displayName;
            if (s.T2Id == teamId && s.T2Type == "T") s.T2Name = displayName;
        }

        if (schedules.Count > 0)
            await _context.SaveChangesAsync(ct);
    }

    public async Task<int> SynchronizeScheduleDivisionForTeamAsync(
        Guid teamId, Guid jobId, Guid newAgegroupId, string newAgegroupName,
        Guid newDivId, string newDivName, CancellationToken ct = default)
    {
        var schedules = await _context.Schedule
            .Where(s => s.JobId == jobId
                && ((s.T1Id == teamId && s.T1Type == "T")
                 || (s.T2Id == teamId && s.T2Type == "T")))
            .ToListAsync(ct);

        foreach (var s in schedules)
        {
            // Update the game's grouping when this team is T1 (home)
            if (s.T1Id == teamId)
            {
                s.AgegroupId = newAgegroupId;
                s.AgegroupName = newAgegroupName;
                s.DivId = newDivId;
                s.DivName = newDivName;
            }
        }

        if (schedules.Count > 0)
            await _context.SaveChangesAsync(ct);

        return schedules.Count;
    }
}
