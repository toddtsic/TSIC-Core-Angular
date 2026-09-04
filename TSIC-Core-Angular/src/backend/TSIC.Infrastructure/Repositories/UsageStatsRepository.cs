using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Dtos.Usage;
using TSIC.Contracts.Repositories;
using TSIC.Infrastructure.Data.LogsDbContext;

namespace TSIC.Infrastructure.Repositories;

/// <summary>
/// Queries logs.AppUsage in TSICLogs. Registered only when LogsConnection is present;
/// see UnavailableUsageStatsRepository for the other case.
/// </summary>
public class UsageStatsRepository : IUsageStatsRepository
{
    private readonly LogsDbContext _context;

    public UsageStatsRepository(LogsDbContext context)
    {
        _context = context;
    }

    public bool IsAvailable => true;

    public async Task<IReadOnlyList<JobUsageAggregateDto>> GetUsageByJobAsync(
        DateTime since,
        bool excludeBots,
        CancellationToken cancellationToken = default)
    {
        var query = _context.AppUsage
            .AsNoTracking()
            .Where(u => u.OccurredAt >= since && u.JobId != Guid.Empty);

        if (excludeBots)
            query = query.Where(u => !u.IsBot);

        // One grouped pass. SignedInRequests counts rows carrying a UserId; anonymous is
        // left to the caller as Total - SignedIn so the two cannot disagree.
        //
        // DistinctUsers counts UserId, which SQL's COUNT(DISTINCT) already ignores nulls
        // for -- anonymous rows contribute nothing rather than collapsing into one
        // phantom "null user".
        return await query
            .GroupBy(u => u.JobId)
            .Select(g => new JobUsageAggregateDto
            {
                JobId = g.Key,
                TotalRequests = g.Count(),
                SignedInRequests = g.Count(u => u.UserId != null),
                DistinctUsers = g.Select(u => u.UserId).Distinct().Count(u => u != null),
                LastActivity = g.Max(u => u.OccurredAt),
            })
            .ToListAsync(cancellationToken);
    }
}

/// <summary>
/// Stand-in used when LogsConnection is absent, so LogsDbContext was never registered.
///
/// Exists so the dashboard reports "usage logging is not configured on this server"
/// instead of failing to resolve a dependency and returning 500 on a widget. The
/// distinction matters: an empty result would read as "nobody used anything", which is a
/// worse lie than an honest unavailable.
/// </summary>
public class UnavailableUsageStatsRepository : IUsageStatsRepository
{
    public bool IsAvailable => false;

    public Task<IReadOnlyList<JobUsageAggregateDto>> GetUsageByJobAsync(
        DateTime since,
        bool excludeBots,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<JobUsageAggregateDto>>([]);
}
