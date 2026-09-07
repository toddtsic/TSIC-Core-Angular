using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Dtos.Usage;
using TSIC.Contracts.Repositories;
using TSIC.Domain.LogEntities;
using TSIC.Infrastructure.Data.LogsDbContext;

namespace TSIC.Infrastructure.Repositories;

/// <summary>
/// Queries logs.AppUsage in TSICLogs. Registered only when LogsConnection is present;
/// see UnavailableUsageStatsRepository for the other case.
///
/// Every query starts from <see cref="Admitted"/>: rows with a recognised client tag whose
/// User-Agent did not declare itself a machine. The writer applies the same test before a
/// row exists (Todd, 2026-09-07: the table records how our clients use the system, not how
/// machines interrogate it); rows written before that rule stay as evidence and fall out here.
/// </summary>
public class UsageStatsRepository : IUsageStatsRepository
{
    /// <summary>logs.AppClients id 0: no recognised client tag on the request.</summary>
    private const int AppClientUnknown = 0;

    private readonly LogsDbContext _context;

    public UsageStatsRepository(LogsDbContext context)
    {
        _context = context;
    }

    public bool IsAvailable => true;

    /// <summary>The admission rule as a query: our clients, driven by people.</summary>
    private IQueryable<AppUsage> Admitted() =>
        _context.AppUsage
            .AsNoTracking()
            .Where(u => u.AppClientId != AppClientUnknown && !u.IsBot);

    public async Task<IReadOnlyList<JobUsageAggregateDto>> GetUsageByJobAsync(
        DateTime since,
        CancellationToken cancellationToken = default)
    {
        var query = Admitted()
            .Where(u => u.OccurredAt >= since && u.JobId != Guid.Empty);

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

    public async Task<IReadOnlyList<UsageRegistrationByJobDto>> GetDistinctRegistrationsByJobAsync(
        IReadOnlyList<Guid> jobIds,
        DateTime since,
        int? appClientId,
        CancellationToken cancellationToken = default)
    {
        if (jobIds.Count == 0)
            return [];

        var query = Admitted()
            .Where(u => u.OccurredAt >= since && u.RegId != null && jobIds.Contains(u.JobId));

        if (appClientId is int client)
            query = query.Where(u => u.AppClientId == client);

        return await query
            .Select(u => new { u.JobId, RegId = u.RegId!.Value })
            .Distinct()
            .Select(x => new UsageRegistrationByJobDto { JobId = x.JobId, RegistrationId = x.RegId })
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<UsageClientFacetDto>> GetClientsPresentAsync(
        IReadOnlyList<Guid> jobIds,
        DateTime since,
        CancellationToken cancellationToken = default)
    {
        if (jobIds.Count == 0)
            return [];

        return await Admitted()
            .Where(u => u.OccurredAt >= since && jobIds.Contains(u.JobId))
            .GroupBy(u => new { u.AppClientId, u.AppClient.AppClientName })
            .Select(g => new UsageClientFacetDto
            {
                AppClientId = g.Key.AppClientId,
                AppClientName = g.Key.AppClientName,
                Requests = g.Count(),
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<UsageRouteCountDto>> GetAnonymousRequestsByRouteAsync(
        IReadOnlyList<Guid> jobIds,
        DateTime since,
        int? appClientId,
        CancellationToken cancellationToken = default)
    {
        if (jobIds.Count == 0)
            return [];

        // Anonymous = no registration on the request. A registered user browsing before
        // sign-in is anonymous here too; that is the definition, not a gap.
        var query = Admitted()
            .Where(u => u.OccurredAt >= since && jobIds.Contains(u.JobId) && u.RegId == null);

        if (appClientId is not null)
            query = query.Where(u => u.AppClientId == appClientId.Value);

        return await query
            .GroupBy(u => new { u.JobId, u.Controller, u.Action })
            .Select(g => new UsageRouteCountDto
            {
                JobId = g.Key.JobId,
                Controller = g.Key.Controller,
                Action = g.Key.Action,
                Requests = g.Sum(u => u.StatusCode < 400 ? 1 : 0),
                FailedRequests = g.Sum(u => u.StatusCode >= 400 ? 1 : 0),
            })
            .ToListAsync(cancellationToken);
    }

    public Task<DateTime?> GetFirstRecordedAtAsync(CancellationToken cancellationToken = default) =>
        Admitted().MinAsync(u => (DateTime?)u.OccurredAt, cancellationToken);

    public async Task<IReadOnlyList<UsageRegistrationByBucketDto>> GetDistinctRegistrationsByBucketAsync(
        IReadOnlyList<Guid> jobIds,
        DateTime since,
        UsageBucket bucket,
        int? appClientId,
        CancellationToken cancellationToken = default)
    {
        if (jobIds.Count == 0)
            return [];

        var query = Admitted()
            .Where(u => u.OccurredAt >= since && u.RegId != null && jobIds.Contains(u.JobId));

        if (appClientId is int client)
            query = query.Where(u => u.AppClientId == client);

        // DATEDIFF counts unit BOUNDARIES crossed from `since`, so with `since` aligned to
        // the unit the result is the whole-bucket index. Weeks divide days by 7 rather than
        // use DATEDIFF(week), whose boundary is Sunday regardless of DATEFIRST.
        var indexed = bucket switch
        {
            UsageBucket.Day => query.Select(u => new { Index = EF.Functions.DateDiffDay(since, u.OccurredAt), u.JobId, RegId = u.RegId!.Value }),
            UsageBucket.Week => query.Select(u => new { Index = EF.Functions.DateDiffDay(since, u.OccurredAt) / 7, u.JobId, RegId = u.RegId!.Value }),
            UsageBucket.Month => query.Select(u => new { Index = EF.Functions.DateDiffMonth(since, u.OccurredAt), u.JobId, RegId = u.RegId!.Value }),
            _ => throw new ArgumentOutOfRangeException(nameof(bucket)),
        };

        return await indexed
            .Distinct()
            .Select(x => new UsageRegistrationByBucketDto { BucketIndex = x.Index, JobId = x.JobId, RegistrationId = x.RegId })
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
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<JobUsageAggregateDto>>([]);

    public Task<IReadOnlyList<UsageRegistrationByJobDto>> GetDistinctRegistrationsByJobAsync(
        IReadOnlyList<Guid> jobIds,
        DateTime since,
        int? appClientId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<UsageRegistrationByJobDto>>([]);

    public Task<IReadOnlyList<UsageClientFacetDto>> GetClientsPresentAsync(
        IReadOnlyList<Guid> jobIds,
        DateTime since,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<UsageClientFacetDto>>([]);

    public Task<IReadOnlyList<UsageRouteCountDto>> GetAnonymousRequestsByRouteAsync(
        IReadOnlyList<Guid> jobIds,
        DateTime since,
        int? appClientId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<UsageRouteCountDto>>([]);

    public Task<DateTime?> GetFirstRecordedAtAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<DateTime?>(null);

    public Task<IReadOnlyList<UsageRegistrationByBucketDto>> GetDistinctRegistrationsByBucketAsync(
        IReadOnlyList<Guid> jobIds,
        DateTime since,
        UsageBucket bucket,
        int? appClientId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<UsageRegistrationByBucketDto>>([]);
}
