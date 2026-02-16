using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Repositories;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Data.SqlDbContext;

namespace TSIC.Infrastructure.Repositories;

public class DivisionRepository : IDivisionRepository
{
    private readonly SqlDbContext _context;

    public DivisionRepository(SqlDbContext context)
    {
        _context = context;
    }

    public async Task<List<Divisions>> GetByAgegroupIdAsync(Guid agegroupId, CancellationToken cancellationToken = default)
    {
        return await _context.Divisions
            .AsNoTracking()
            .Where(d => d.AgegroupId == agegroupId)
            .OrderBy(d => d.DivName)
            .ToListAsync(cancellationToken);
    }

    public async Task<Divisions?> GetByIdAsync(Guid divId, CancellationToken cancellationToken = default)
    {
        return await _context.Divisions.FindAsync(new object[] { divId }, cancellationToken);
    }

    public async Task<Divisions?> GetByIdReadOnlyAsync(Guid divId, CancellationToken cancellationToken = default)
    {
        return await _context.Divisions
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.DivId == divId, cancellationToken);
    }

    public async Task<bool> HasTeamsAsync(Guid divId, CancellationToken cancellationToken = default)
    {
        return await _context.Teams
            .AsNoTracking()
            .AnyAsync(t => t.DivId == divId, cancellationToken);
    }

    public async Task<bool> BelongsToJobAsync(Guid divId, Guid jobId, CancellationToken cancellationToken = default)
    {
        return await _context.Divisions
            .AsNoTracking()
            .Where(d => d.DivId == divId)
            .Join(_context.Agegroups, d => d.AgegroupId, a => a.AgegroupId, (d, a) => a.LeagueId)
            .Join(_context.JobLeagues.Where(jl => jl.JobId == jobId), leagueId => leagueId, jl => jl.LeagueId, (leagueId, jl) => true)
            .AnyAsync(cancellationToken);
    }

    public async Task<List<Contracts.Dtos.PoolAssignment.PoolDivisionOptionDto>> GetPoolAssignmentOptionsAsync(
        Guid jobId, CancellationToken cancellationToken = default)
    {
        // Get league IDs associated with this job
        var leagueIds = await _context.JobLeagues
            .AsNoTracking()
            .Where(jl => jl.JobId == jobId)
            .Select(jl => jl.LeagueId)
            .ToListAsync(cancellationToken);

        // Get divisions with agegroup info for those leagues
        var divisions = await _context.Divisions
            .AsNoTracking()
            .Where(d => leagueIds.Contains(d.Agegroup.LeagueId))
            .Select(d => new
            {
                d.DivId,
                DivName = d.DivName ?? "(Unnamed Division)",
                d.Agegroup.AgegroupId,
                AgegroupName = d.Agegroup.AgegroupName ?? "",
                d.Agegroup.MaxTeams,
                d.Agegroup.TeamFee,
                d.Agegroup.RosterFee,
                TeamCount = _context.Teams.Count(t => t.DivId == d.DivId && t.JobId == jobId)
            })
            .OrderBy(d => d.AgegroupName)
            .ThenBy(d => d.DivName)
            .ToListAsync(cancellationToken);

        return divisions.Select(d => new Contracts.Dtos.PoolAssignment.PoolDivisionOptionDto
        {
            DivId = d.DivId,
            DivName = d.DivName,
            AgegroupId = d.AgegroupId,
            AgegroupName = d.AgegroupName,
            TeamCount = d.TeamCount,
            MaxTeams = d.MaxTeams,
            IsDroppedTeams = d.AgegroupName.Contains("DROPPED", StringComparison.OrdinalIgnoreCase)
                          || d.AgegroupName.Contains("Dropped", StringComparison.OrdinalIgnoreCase),
            TeamFee = d.TeamFee,
            RosterFee = d.RosterFee
        }).ToList();
    }

    public void Add(Divisions division) => _context.Divisions.Add(division);

    public void Remove(Divisions division) => _context.Divisions.Remove(division);

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        => await _context.SaveChangesAsync(cancellationToken);
}
