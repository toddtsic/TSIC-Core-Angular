using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Repositories;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Data.SqlDbContext;

namespace TSIC.Infrastructure.Repositories;

/// <summary>
/// Repository for ClubTeams entity using Entity Framework Core.
/// </summary>
public class ClubTeamRepository : IClubTeamRepository
{
    private readonly SqlDbContext _context;

    public ClubTeamRepository(SqlDbContext context)
    {
        _context = context;
    }

    public async Task<List<ClubTeams>> GetByClubIdAsync(
        int clubId,
        CancellationToken cancellationToken = default)
    {
        // Deduplicate: same team entered at different LOPs across events creates separate rows.
        // Group by identity (ClubId + Name + GradYear), take the row with the highest LOP.
        return await _context.ClubTeams
            .Where(ct => ct.ClubId == clubId)
            .AsNoTracking()
            .GroupBy(ct => new { ct.ClubId, ct.ClubTeamName, ct.ClubTeamGradYear })
            .Select(g => g.OrderByDescending(ct => ct.ClubTeamLevelOfPlay).First())
            .ToListAsync(cancellationToken);
    }

    public async Task<List<ClubTeams>> GetByClubIdsAsync(
        IEnumerable<int> clubIds,
        CancellationToken cancellationToken = default)
    {
        var idList = clubIds.Distinct().ToList();
        if (idList.Count == 0) return new List<ClubTeams>();

        return await _context.ClubTeams
            .Where(ct => idList.Contains(ct.ClubId))
            .AsNoTracking()
            .GroupBy(ct => new { ct.ClubId, ct.ClubTeamName, ct.ClubTeamGradYear })
            .Select(g => g.OrderByDescending(ct => ct.ClubTeamLevelOfPlay).First())
            .ToListAsync(cancellationToken);
    }

    public async Task<HashSet<int>> GetClubTeamIdsInJobAsync(
        Guid jobId,
        IEnumerable<int> clubTeamIds,
        CancellationToken cancellationToken = default)
    {
        var idList = clubTeamIds.ToList();
        if (idList.Count == 0) return new HashSet<int>();

        var present = await _context.Teams
            .AsNoTracking()
            .Where(t => t.JobId == jobId && t.ClubTeamId != null && idList.Contains(t.ClubTeamId.Value))
            .Select(t => t.ClubTeamId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken);

        return present.ToHashSet();
    }

    public async Task<ClubTeams?> GetByIdAsync(
        int clubTeamId,
        CancellationToken cancellationToken = default)
    {
        return await _context.ClubTeams
            .Where(ct => ct.ClubTeamId == clubTeamId)
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<ClubTeams?> GetByIdReadOnlyAsync(
        int clubTeamId,
        CancellationToken cancellationToken = default)
    {
        return await _context.ClubTeams
            .Where(ct => ct.ClubTeamId == clubTeamId)
            .AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<ClubTeams?> FindByIdentityAsync(
        int clubId, string clubTeamName, string clubTeamGradYear,
        CancellationToken cancellationToken = default)
    {
        return await _context.ClubTeams
            .Where(ct => ct.ClubId == clubId
                && ct.ClubTeamName == clubTeamName
                && ct.ClubTeamGradYear == clubTeamGradYear)
            .OrderByDescending(ct => ct.ClubTeamLevelOfPlay)
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<Dictionary<int, int>> GetClubIdsForClubTeamIdsAsync(
        IEnumerable<int> clubTeamIds,
        CancellationToken cancellationToken = default)
    {
        var idList = clubTeamIds.Distinct().ToList();
        if (idList.Count == 0) return new Dictionary<int, int>();

        return await _context.ClubTeams
            .Where(ct => idList.Contains(ct.ClubTeamId))
            .AsNoTracking()
            .ToDictionaryAsync(ct => ct.ClubTeamId, ct => ct.ClubId, cancellationToken);
    }

    public async Task<Dictionary<int, string>> GetLibraryNamesForClubTeamIdsAsync(
        IEnumerable<int> clubTeamIds,
        CancellationToken cancellationToken = default)
    {
        var idList = clubTeamIds.Distinct().ToList();
        if (idList.Count == 0) return new Dictionary<int, string>();

        return await _context.ClubTeams
            .Where(ct => idList.Contains(ct.ClubTeamId))
            .AsNoTracking()
            .ToDictionaryAsync(ct => ct.ClubTeamId, ct => ct.ClubTeamName, cancellationToken);
    }

    public void Add(ClubTeams clubTeam)
    {
        _context.ClubTeams.Add(clubTeam);
    }

    public void Remove(ClubTeams clubTeam)
    {
        _context.ClubTeams.Remove(clubTeam);
    }

    public async Task<HashSet<int>> GetScheduledClubTeamIdsAsync(
        IEnumerable<int> clubTeamIds,
        CancellationToken cancellationToken = default)
    {
        var idList = clubTeamIds.ToList();
        if (idList.Count == 0) return new HashSet<int>();

        var scheduled = await (
            from t in _context.Teams.AsNoTracking()
            where t.ClubTeamId != null
                && idList.Contains(t.ClubTeamId.Value)
                && _context.Schedule.Any(s => s.T1Id == t.TeamId || s.T2Id == t.TeamId)
            select t.ClubTeamId!.Value
        ).Distinct().ToListAsync(cancellationToken);

        return scheduled.ToHashSet();
    }

    public async Task<HashSet<int>> GetClubTeamIdsWithEventRegistrationsAsync(
        IEnumerable<int> clubTeamIds,
        CancellationToken cancellationToken = default)
    {
        var idList = clubTeamIds.ToList();
        if (idList.Count == 0) return new HashSet<int>();

        var referenced = await _context.Teams
            .AsNoTracking()
            .Where(t => t.ClubTeamId != null && idList.Contains(t.ClubTeamId.Value))
            .Select(t => t.ClubTeamId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken);

        return referenced.ToHashSet();
    }

    public async Task<List<ClubTeamEventHistoryDto>> GetEventHistoryForClubTeamIdsAsync(
        IEnumerable<int> clubTeamIds,
        CancellationToken cancellationToken = default)
    {
        var idList = clubTeamIds.ToList();
        if (idList.Count == 0) return new List<ClubTeamEventHistoryDto>();

        // Agegroup is a required navigation on Teams, so the join is inner — a copy with no
        // age group would be corrupt, not history. "DROPPED" mirrors TeamRepository's drop test.
        return await (
            from t in _context.Teams.AsNoTracking()
            where t.ClubTeamId != null && idList.Contains(t.ClubTeamId.Value)
            orderby t.Job.EventStartDate descending, t.Createdate descending
            select new ClubTeamEventHistoryDto
            {
                ClubTeamId = t.ClubTeamId!.Value,
                TeamId = t.TeamId,
                JobId = t.JobId,
                JobPath = t.Job.JobPath,
                JobName = t.Job.JobName ?? t.Job.JobPath,
                EventTeamName = t.TeamName ?? string.Empty,
                AgeGroupName = t.Agegroup.AgegroupName ?? string.Empty,
                IsDropped = t.Agegroup.AgegroupName != null && t.Agegroup.AgegroupName.Contains("DROPPED"),
                EventStartDate = t.Job.EventStartDate,
                RegisteredOn = t.Createdate,
            }
        ).ToListAsync(cancellationToken);
    }

    public async Task<bool> HasAnyTeamRegistrationsAsync(
        int clubTeamId,
        CancellationToken cancellationToken = default)
    {
        return await _context.Teams
            .AsNoTracking()
            .AnyAsync(t => t.ClubTeamId == clubTeamId, cancellationToken);
    }

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return await _context.SaveChangesAsync(cancellationToken);
    }
}
