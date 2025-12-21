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

    public async Task<JobLeagues?> GetPrimaryLeagueForJobAsync(
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        return await _context.JobLeagues
            .Where(jl => jl.JobId == jobId && jl.BIsPrimary)
            .Include(jl => jl.League)
            .ThenInclude(l => l!.Agegroups)
            .AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken);
    }
}
