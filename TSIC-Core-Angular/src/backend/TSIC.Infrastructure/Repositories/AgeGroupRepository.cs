using Microsoft.EntityFrameworkCore;
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

    public void Add(Agegroups agegroup) => _context.Agegroups.Add(agegroup);

    public void Remove(Agegroups agegroup) => _context.Agegroups.Remove(agegroup);

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        => await _context.SaveChangesAsync(cancellationToken);
}
