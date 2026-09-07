using System.Security.Claims;
using TSIC.API.Extensions;
using TSIC.API.Services.Shared.Jobs;
using TSIC.Contracts.Dtos.Usage;
using TSIC.Contracts.Repositories;
using TSIC.Domain.Constants;

namespace TSIC.API.Services.Usage;

/// <summary>Scope tiers, in ceiling order. Comparable: a role's ceiling is the widest it may hold.</summary>
public enum UsageScope
{
    Job = 0,
    Customer = 1,
    Tsic = 2,
}

/// <summary>Why a scope request could not be resolved.</summary>
public enum UsageScopeFailure
{
    None = 0,

    /// <summary>The token carries no registration, so there is no job to stand in.</summary>
    NoJobContext,

    /// <summary>The requested scope is wider than the caller's role allows.</summary>
    AboveCeiling,

    /// <summary>An event lens was given that is not one of the live events the scope resolved to.</summary>
    EventNotInScope,
}

/// <summary>
/// The job set a usage-analysis query runs against. <see cref="GetJobIds"/> is what the
/// TSICLogs query filters on; <see cref="Jobs"/> is the same set with names, for the page.
/// </summary>
public sealed record UsageScopeResolution
{
    public required UsageScope Scope { get; init; }

    public required UsageScope Ceiling { get; init; }

    public required Guid CurrentJobId { get; init; }

    public required bool CurrentJobIsLive { get; init; }

    public required IReadOnlyList<UsageAnalysisJobDto> Jobs { get; init; }

    /// <summary>The <see cref="Jobs"/> ids -- what the TSICLogs query filters on.</summary>
    public IReadOnlyList<Guid> GetJobIds() => [.. Jobs.Select(j => j.JobId)];
}

public sealed record UsageScopeResult
{
    public UsageScopeFailure Failure { get; init; }

    public UsageScopeResolution? Resolution { get; init; }
}

/// <summary>
/// The ONE place that turns "scope=job|customer|tsic" plus a token into a job-id set.
/// Every usage-analysis endpoint calls this and filters on the result; none accepts a
/// job or customer id from the client.
/// </summary>
public interface IUsageScopeResolver
{
    /// <summary>Parses a scope word. Unknown or absent words fail closed to Job.</summary>
    UsageScope Parse(string? scope);

    /// <summary>The widest scope this caller's role may request.</summary>
    UsageScope CeilingFor(ClaimsPrincipal user);

    /// <summary>
    /// Resolves the live job set for <paramref name="requested"/>. Above-ceiling requests
    /// are refused, never quietly narrowed -- a caller that asked for more than it may
    /// have gets a 403, not a smaller answer that looks like the one it asked for.
    ///
    /// <paramref name="eventId"/> is the page's event lens: narrows the resolved set to that
    /// one live event. It can only ever NARROW -- an id outside the resolved set is refused,
    /// so the lens never widens what the scope word and ceiling allowed.
    /// </summary>
    Task<UsageScopeResult> ResolveAsync(ClaimsPrincipal user, UsageScope requested, Guid? eventId = null, CancellationToken ct = default);
}

public sealed class UsageScopeResolver : IUsageScopeResolver
{
    private readonly IJobLookupService _jobLookup;
    private readonly IJobRepository _jobRepo;

    public UsageScopeResolver(IJobLookupService jobLookup, IJobRepository jobRepo)
    {
        _jobLookup = jobLookup;
        _jobRepo = jobRepo;
    }

    public UsageScope Parse(string? scope) => scope?.Trim().ToLowerInvariant() switch
    {
        UsageAnalysisScopes.Customer => UsageScope.Customer,
        UsageAnalysisScopes.Tsic => UsageScope.Tsic,
        _ => UsageScope.Job,
    };

    public static string ToWord(UsageScope scope) => scope switch
    {
        UsageScope.Customer => UsageAnalysisScopes.Customer,
        UsageScope.Tsic => UsageAnalysisScopes.Tsic,
        _ => UsageAnalysisScopes.Job,
    };

    // Ceiling is the ROLE tier, matching the existing policies: CanCrossCustomerJobs is
    // the customer tier (Superuser + SuperDirector), SuperUserOnly is the platform tier.
    // Any other admin role stands where it is.
    public UsageScope CeilingFor(ClaimsPrincipal user)
    {
        var role = user.FindFirst(ClaimTypes.Role)?.Value;
        return role switch
        {
            RoleConstants.Names.SuperuserName => UsageScope.Tsic,
            RoleConstants.Names.SuperDirectorName => UsageScope.Customer,
            _ => UsageScope.Job,
        };
    }

    public async Task<UsageScopeResult> ResolveAsync(ClaimsPrincipal user, UsageScope requested, Guid? eventId = null, CancellationToken ct = default)
    {
        var ceiling = CeilingFor(user);
        if (requested > ceiling)
            return new UsageScopeResult { Failure = UsageScopeFailure.AboveCeiling };

        var currentJobId = await user.GetJobIdFromRegistrationAsync(_jobLookup);
        if (currentJobId == null)
            return new UsageScopeResult { Failure = UsageScopeFailure.NoJobContext };

        // LIVE = ExpiryUsers > now, every scope, no exceptions. The customer's live list
        // serves both the job and customer scopes: for job it is filtered to the one job,
        // which also answers "is the event I am standing in still live?" in the same
        // round-trip.
        var live = requested == UsageScope.Tsic
            ? await _jobRepo.GetLiveJobsAsync(sameCustomerAsJobId: null, ct)
            : await _jobRepo.GetLiveJobsAsync(sameCustomerAsJobId: currentJobId.Value, ct);

        var currentJobIsLive = live.Any(j => j.JobId == currentJobId.Value);

        var jobs = requested == UsageScope.Job
            ? live.Where(j => j.JobId == currentJobId.Value).ToList()
            : live;

        if (eventId is Guid lens)
        {
            var one = jobs.Where(j => j.JobId == lens).ToList();
            if (one.Count == 0)
                return new UsageScopeResult { Failure = UsageScopeFailure.EventNotInScope };
            jobs = one;
        }

        return new UsageScopeResult
        {
            Failure = UsageScopeFailure.None,
            Resolution = new UsageScopeResolution
            {
                Scope = requested,
                Ceiling = ceiling,
                CurrentJobId = currentJobId.Value,
                CurrentJobIsLive = currentJobIsLive,
                Jobs = jobs,
            },
        };
    }
}
