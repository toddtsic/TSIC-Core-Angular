using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Dtos.Ladt;
using TSIC.Contracts.Repositories;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Data.SqlDbContext;

namespace TSIC.Infrastructure.Repositories;

public class LeagueRepository : ILeagueRepository
{
    private readonly SqlDbContext _context;

    public LeagueRepository(SqlDbContext context)
    {
        _context = context;
    }

    public async Task<List<LeagueDetailDto>> GetLeaguesByJobIdAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        var leagueIds = await _context.JobLeagues
            .AsNoTracking()
            .Where(jl => jl.JobId == jobId)
            .Select(jl => jl.LeagueId)
            .ToListAsync(cancellationToken);

        return await _context.Leagues
            .AsNoTracking()
            .Where(l => leagueIds.Contains(l.LeagueId))
            .OrderBy(l => l.LeagueName)
            .Select(l => new LeagueDetailDto
            {
                LeagueId = l.LeagueId,
                LeagueName = l.LeagueName ?? "",
                SportId = l.SportId,
                SportName = l.Sport != null ? l.Sport.SportName : null,
                BHideContacts = l.BHideContacts,
                BHideStandings = l.BHideStandings,
                RescheduleEmailsToAddon = l.RescheduleEmailsToAddon,
                PlayerFeeOverride = l.PlayerFeeOverride
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<Leagues?> GetByIdAsync(Guid leagueId, CancellationToken cancellationToken = default)
    {
        return await _context.Leagues.FindAsync(new object[] { leagueId }, cancellationToken);
    }

    public async Task<LeagueDetailDto?> GetByIdWithSportAsync(Guid leagueId, CancellationToken cancellationToken = default)
    {
        return await _context.Leagues
            .AsNoTracking()
            .Where(l => l.LeagueId == leagueId)
            .Select(l => new LeagueDetailDto
            {
                LeagueId = l.LeagueId,
                LeagueName = l.LeagueName ?? "",
                SportId = l.SportId,
                SportName = l.Sport != null ? l.Sport.SportName : null,
                BHideContacts = l.BHideContacts,
                BHideStandings = l.BHideStandings,
                RescheduleEmailsToAddon = l.RescheduleEmailsToAddon,
                PlayerFeeOverride = l.PlayerFeeOverride
            })
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<List<JobLeagues>> GetJobLeaguesAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        return await _context.JobLeagues
            .AsNoTracking()
            .Where(jl => jl.JobId == jobId)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> BelongsToJobAsync(Guid leagueId, Guid jobId, CancellationToken cancellationToken = default)
    {
        return await _context.JobLeagues
            .AsNoTracking()
            .AnyAsync(jl => jl.LeagueId == leagueId && jl.JobId == jobId, cancellationToken);
    }

    public async Task<List<Sports>> GetAllSportsAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Sports
            .AsNoTracking()
            .OrderBy(s => s.SportName)
            .ToListAsync(cancellationToken);
    }

    public void Add(Leagues league) => _context.Leagues.Add(league);

    public void Remove(Leagues league) => _context.Leagues.Remove(league);

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        => await _context.SaveChangesAsync(cancellationToken);
}
