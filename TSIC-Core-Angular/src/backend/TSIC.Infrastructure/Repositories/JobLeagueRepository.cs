using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Repositories;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Data.SqlDbContext;

namespace TSIC.Infrastructure.Repositories;

/// <summary>
/// Repository for JobLeagues entity using Entity Framework Core.
/// </summary>
public class JobLeagueRepository : IJobLeagueRepository
{
    private readonly SqlDbContext _context;

    public JobLeagueRepository(SqlDbContext context)
    {
        _context = context;
    }

    public async Task<Guid?> GetPrimaryLeagueForJobAsync(
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        // Get all leagues for this job
        var jobLeagues = await _context.JobLeagues
            .Where(jl => jl.JobId == jobId)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        // If only one league exists, return its ID
        if (jobLeagues.Count == 1)
            return jobLeagues[0].LeagueId;

        // If multiple leagues, return the primary one's ID
        return jobLeagues.SingleOrDefault(jl => jl.BIsPrimary)?.LeagueId;
    }
}
