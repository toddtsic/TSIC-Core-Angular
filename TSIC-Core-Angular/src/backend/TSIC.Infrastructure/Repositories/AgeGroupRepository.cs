using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Dtos.Rankings;
using TSIC.Contracts.Repositories;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Data.SqlDbContext;

namespace TSIC.Infrastructure.Repositories;

/// <summary>
/// Repository for Agegroups entity using Entity Framework Core.
/// </summary>
public class AgeGroupRepository : IAgeGroupRepository
{
    private readonly SqlDbContext _context;

    public AgeGroupRepository(SqlDbContext context)
    {
        _context = context;
    }

    public async Task<(decimal? TeamFee, decimal? RosterFee)?> GetFeeInfoAsync(Guid ageGroupId, CancellationToken cancellationToken = default)
    {
        var result = await _context.Agegroups
            .AsNoTracking()
            .Where(a => a.AgegroupId == ageGroupId)
            .Select(a => new { a.TeamFee, a.RosterFee })
            .FirstOrDefaultAsync(cancellationToken);

        return result != null ? (result.TeamFee, result.RosterFee) : null;
    }

    public async Task<List<AgeGroupForRegistration>> GetByLeagueAndSeasonAsync(
        Guid leagueId,
        string season,
        CancellationToken cancellationToken = default)
    {
        return await _context.Agegroups
            .AsNoTracking()
            .Where(ag => ag.LeagueId == leagueId && ag.Season == season && ag.MaxTeams > 0)
            .OrderBy(ag => ag.AgegroupName)
            .Select(ag => new AgeGroupForRegistration
            {
                AgegroupId = ag.AgegroupId,
                AgegroupName = ag.AgegroupName ?? string.Empty,
                MaxTeams = ag.MaxTeams,
                TeamFee = ag.TeamFee,
                RosterFee = ag.RosterFee
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<AgeGroupValidationInfo?> GetForValidationAsync(
        Guid ageGroupId,
        CancellationToken cancellationToken = default)
    {
        return await _context.Agegroups
            .AsNoTracking()
            .Where(ag => ag.AgegroupId == ageGroupId)
            .Select(ag => new AgeGroupValidationInfo
            {
                AgegroupId = ag.AgegroupId,
                AgegroupName = ag.AgegroupName,
                MaxTeams = ag.MaxTeams,
                TeamFee = ag.TeamFee,
                RosterFee = ag.RosterFee
            })
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<Agegroups?> GetByIdAsync(Guid ageGroupId, CancellationToken cancellationToken = default)
    {
        return await _context.Agegroups.FindAsync(new object[] { ageGroupId }, cancellationToken);
    }

    // ── LADT Admin methods ──

    public async Task<List<Agegroups>> GetByLeagueIdAsync(Guid leagueId, CancellationToken cancellationToken = default)
    {
        return await _context.Agegroups
            .AsNoTracking()
            .Where(a => a.LeagueId == leagueId)
            .OrderBy(a => a.SortAge)
            .ThenBy(a => a.AgegroupName)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> HasTeamsAsync(Guid agegroupId, CancellationToken cancellationToken = default)
    {
        return await _context.Teams
            .AsNoTracking()
            .AnyAsync(t => t.AgegroupId == agegroupId, cancellationToken);
    }

    public async Task<bool> BelongsToJobAsync(Guid agegroupId, Guid jobId, CancellationToken cancellationToken = default)
    {
        return await _context.Agegroups
            .AsNoTracking()
            .Where(a => a.AgegroupId == agegroupId)
            .Join(_context.JobLeagues.Where(jl => jl.JobId == jobId),
                a => a.LeagueId, jl => jl.LeagueId, (a, jl) => true)
            .AnyAsync(cancellationToken);
    }

    // ── US Lacrosse Rankings ──

    public async Task<List<AgeGroupOptionDto>> GetActiveAgeGroupsForJobAsync(
        Guid jobId, CancellationToken ct = default)
    {
        return await _context.Teams
            .AsNoTracking()
            .Where(t => t.JobId == jobId
                        && t.Active == true
                        && !t.Agegroup.AgegroupName.Contains("DROPPED")
                        && !t.Agegroup.AgegroupName.Contains("WAITLIST"))
            .GroupBy(t => new { t.Agegroup.AgegroupId, t.Agegroup.AgegroupName })
            .OrderBy(g => g.Key.AgegroupName)
            .Select(g => new AgeGroupOptionDto
            {
                Value = g.Key.AgegroupId.ToString(),
                Text = $"{g.Key.AgegroupName ?? string.Empty} ({g.Count()} Teams)"
            })
            .ToListAsync(ct);
    }

    public async Task<Dictionary<Guid, int?>> GetGameGuaranteesForLeagueAsync(
        Guid leagueId, CancellationToken cancellationToken = default)
    {
        return await _context.Agegroups
            .AsNoTracking()
            .Where(a => a.LeagueId == leagueId)
            .Select(a => new { a.AgegroupId, a.GameGuarantee })
            .ToDictionaryAsync(a => a.AgegroupId, a => a.GameGuarantee, cancellationToken);
    }

    public async Task<int> UpdateGameGuaranteesAsync(
        Dictionary<Guid, int?> agegroupGuarantees, CancellationToken cancellationToken = default)
    {
        if (agegroupGuarantees.Count == 0) return 0;

        var ids = agegroupGuarantees.Keys.ToList();
        var agegroups = await _context.Agegroups
            .Where(a => ids.Contains(a.AgegroupId))
            .ToListAsync(cancellationToken);

        foreach (var ag in agegroups)
        {
            if (agegroupGuarantees.TryGetValue(ag.AgegroupId, out var guarantee))
                ag.GameGuarantee = guarantee;
        }

        return await _context.SaveChangesAsync(cancellationToken);
    }

    public void Add(Agegroups agegroup) => _context.Agegroups.Add(agegroup);

    public void Remove(Agegroups agegroup) => _context.Agegroups.Remove(agegroup);

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        => await _context.SaveChangesAsync(cancellationToken);
}
