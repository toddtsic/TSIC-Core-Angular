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

    public async Task SynchronizeScheduleAgegroupNameAsync(Guid agegroupId, Guid jobId, string newName, CancellationToken ct = default)
    {
        var schedules = await _context.Schedule
            .Where(s => s.JobId == jobId && s.AgegroupId == agegroupId)
            .ToListAsync(ct);

        foreach (var s in schedules)
            s.AgegroupName = newName;

        if (schedules.Count > 0)
            await _context.SaveChangesAsync(ct);
    }

    public async Task SynchronizeScheduleDivisionNameAsync(Guid divId, Guid jobId, string newName, CancellationToken ct = default)
    {
        var schedules = await _context.Schedule
            .Where(s => s.JobId == jobId && (s.DivId == divId || s.Div2Id == divId))
            .ToListAsync(ct);

        foreach (var s in schedules)
        {
            if (s.DivId == divId) s.DivName = newName;
            if (s.Div2Id == divId) s.Div2Name = newName;
        }

        if (schedules.Count > 0)
            await _context.SaveChangesAsync(ct);
    }

    public async Task SynchronizeScheduleTeamAssignmentsForDivisionAsync(Guid divId, Guid jobId, CancellationToken ct = default)
    {
        // 1. Get active teams in the division with their DivRank and club-rep link
        var teams = await _context.Teams
            .AsNoTracking()
            .Where(t => t.DivId == divId && t.Active == true)
            .Select(t => new { t.TeamId, t.DivRank, t.TeamName, t.ClubrepRegistrationid })
            .ToListAsync(ct);

        // 2. Get club names for teams that have a club rep
        var clubRepIds = teams
            .Where(t => t.ClubrepRegistrationid.HasValue)
            .Select(t => t.ClubrepRegistrationid!.Value)
            .Distinct()
            .ToList();

        var clubNames = clubRepIds.Count > 0
            ? await _context.Registrations
                .AsNoTracking()
                .Where(r => clubRepIds.Contains(r.RegistrationId))
                .ToDictionaryAsync(r => r.RegistrationId, r => r.ClubName, ct)
            : new Dictionary<Guid, string?>();

        // 3. Get the job's BShowTeamNameOnlyInSchedules flag
        var showTeamNameOnly = await _context.Jobs
            .AsNoTracking()
            .Where(j => j.JobId == jobId)
            .Select(j => j.BShowTeamNameOnlyInSchedules)
            .FirstOrDefaultAsync(ct);

        // 4. Build rank → (teamId, displayName) map
        var rankMap = new Dictionary<int, (Guid teamId, string displayName)>();
        foreach (var t in teams)
        {
            string? clubName = t.ClubrepRegistrationid.HasValue
                && clubNames.TryGetValue(t.ClubrepRegistrationid.Value, out var cn)
                ? cn : null;

            var displayName = (!string.IsNullOrEmpty(clubName) && !showTeamNameOnly)
                ? $"{clubName}:{t.TeamName}"
                : t.TeamName ?? "";

            rankMap[t.DivRank] = (t.TeamId, displayName);
        }

        // 5. Load all round-robin schedule records for this division
        var schedules = await _context.Schedule
            .Where(s => s.JobId == jobId
                && (s.DivId == divId || s.Div2Id == divId))
            .ToListAsync(ct);

        // 6. Re-resolve T1Id/T1Name and T2Id/T2Name from T1No/T2No
        foreach (var s in schedules)
        {
            if (s.T1Type == "T" && s.T1No.HasValue && s.DivId == divId)
            {
                if (rankMap.TryGetValue(s.T1No.Value, out var t1))
                {
                    s.T1Id = t1.teamId;
                    s.T1Name = t1.displayName;
                }
                else
                {
                    s.T1Id = null;
                    s.T1Name = "";
                }
            }

            if (s.T2Type == "T" && s.T2No.HasValue)
            {
                // T2 uses Div2Id for cross-division games, DivId for same-division
                var t2DivId = s.Div2Id ?? s.DivId;
                if (t2DivId == divId)
                {
                    if (rankMap.TryGetValue(s.T2No.Value, out var t2))
                    {
                        s.T2Id = t2.teamId;
                        s.T2Name = t2.displayName;
                    }
                    else
                    {
                        s.T2Id = null;
                        s.T2Name = "";
                    }
                }
            }
        }

        if (schedules.Count > 0)
            await _context.SaveChangesAsync(ct);
    }
}
