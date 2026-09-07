using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TSIC.API.Services.Usage;
using TSIC.Contracts.Dtos.Usage;
using TSIC.Contracts.Repositories;

namespace TSIC.API.Controllers;

/// <summary>
/// Usage analysis page (tools/usage): the drill behind the UsageStatsPerJob widget's glance.
/// Reads logs.AppUsage in TSICLogs, always constrained to LIVE jobs (ExpiryUsers > now).
///
/// Every endpoint takes a scope WORD (job | customer | tsic) and nothing that identifies a
/// job or customer. The job set comes from the token via IUsageScopeResolver, which also
/// enforces the role ceiling: Director = job, SuperDirector = customer, Superuser = tsic.
/// A request above the ceiling is 403, never narrowed.
///
/// Two lenses narrow a report inside the resolved set and can only ever narrow:
///   eventId  -- one live job of the set (an id outside it is refused);
///   clientId -- one logs.AppClients id, offered by GET clients.
///
/// Controller gate is the admin floor; the per-scope check inside is what actually decides.
/// </summary>
[ApiController]
[Route("api/usage-analysis")]
[Authorize(Policy = "AdminOnly")]
public class UsageAnalysisController : ControllerBase
{
    private readonly IUsageScopeResolver _scopeResolver;
    private readonly IUsageStatsRepository _usageRepo;
    private readonly IUsageAnalysisService _reports;

    public UsageAnalysisController(
        IUsageScopeResolver scopeResolver,
        IUsageStatsRepository usageRepo,
        IUsageAnalysisService reports)
    {
        _scopeResolver = scopeResolver;
        _usageRepo = usageRepo;
        _reports = reports;
    }

    /// <summary>
    /// Resolves a scope for the page shell: the caller's ceiling, whether the current
    /// event is live, and the live jobs the scope covers. The page calls this on load
    /// and on every segment change; reports then query with the same scope word.
    /// </summary>
    [HttpGet("scope")]
    public async Task<ActionResult<UsageAnalysisScopeDto>> GetScope(
        [FromQuery] string? scope,
        CancellationToken ct)
    {
        var requested = _scopeResolver.Parse(scope);
        var result = await _scopeResolver.ResolveAsync(User, requested, ct: ct);

        switch (result.Failure)
        {
            case UsageScopeFailure.NoJobContext:
                return BadRequest(new { message = "Job context required" });
            case UsageScopeFailure.AboveCeiling:
                return Forbid();
        }

        var r = result.Resolution!;
        return Ok(new UsageAnalysisScopeDto
        {
            Scope = UsageScopeResolver.ToWord(r.Scope),
            MaxScope = UsageScopeResolver.ToWord(r.Ceiling),
            UsageLoggingAvailable = _usageRepo.IsAvailable,
            CurrentJobIsLive = r.CurrentJobIsLive,
            Jobs = r.Jobs.ToList(),
        });
    }

    /// <summary>
    /// The client facet: which app clients have rows in the scope/window/event lens. The
    /// page's Client dropdown offers exactly these and nothing else.
    /// </summary>
    [HttpGet("clients")]
    public async Task<ActionResult<UsageClientsDto>> GetClients(
        [FromQuery] string? scope,
        [FromQuery] int windowDays = 7,
        [FromQuery] Guid? eventId = null,
        [FromQuery] string? bucket = null,
        CancellationToken ct = default)
    {
        var (failure, resolution) = await ResolveForReportAsync(scope, eventId, ct);
        if (failure is not null) return failure;

        // A bucket word (a bucketed report is on screen) makes the facet cover that bucket's span instead of the window.
        var days = Math.Clamp(windowDays, 1, 365);
        return Ok(await _reports.GetClientsAsync(resolution!, days, UsageBuckets.Parse(bucket), ct));
    }

    /// <summary>
    /// Report 01 -- Users by Role. Distinct registrations that used the scoped live events
    /// in the window, per event, grouped by role, admin tier flagged. People only;
    /// anonymous traffic is not a person and is not here.
    /// </summary>
    [HttpGet("users-by-role")]
    public async Task<ActionResult<UsersByRoleDto>> GetUsersByRole(
        [FromQuery] string? scope,
        [FromQuery] int windowDays = 7,
        [FromQuery] Guid? eventId = null,
        [FromQuery] int? clientId = null,
        CancellationToken ct = default)
    {
        var (failure, resolution) = await ResolveForReportAsync(scope, eventId, ct);
        if (failure is not null) return failure;

        var days = Math.Clamp(windowDays, 1, 365);
        return Ok(await _reports.GetUsersByRoleAsync(resolution!, days, clientId, ct));
    }

    /// <summary>
    /// Report 02 -- Public Requests by Route. Anonymous requests (no registration on the
    /// request) against the scoped live events in the window, per event, grouped by the
    /// API route they hit, split into succeeded and failed. Requests, not people: nothing
    /// in the log can turn an anonymous request into a visitor.
    /// </summary>
    [HttpGet("public-requests-by-route")]
    public async Task<ActionResult<PublicRequestsByRouteDto>> GetPublicRequestsByRoute(
        [FromQuery] string? scope,
        [FromQuery] int windowDays = 7,
        [FromQuery] Guid? eventId = null,
        [FromQuery] int? clientId = null,
        CancellationToken ct = default)
    {
        var (failure, resolution) = await ResolveForReportAsync(scope, eventId, ct);
        if (failure is not null) return failure;

        var days = Math.Clamp(windowDays, 1, 365);
        return Ok(await _reports.GetPublicRequestsByRouteAsync(resolution!, days, clientId, ct));
    }

    /// <summary>
    /// Report 03 -- Users by Role over Time. Report 01's count, once per bucket: daily over
    /// the last 30 days, weekly over the last 12 weeks, monthly over the last 12 months. The
    /// bucket IS the window; there is no windowDays here. A registration counts in every
    /// bucket it was active in.
    /// </summary>
    [HttpGet("users-by-role-over-time")]
    public async Task<ActionResult<UsersByRoleOverTimeDto>> GetUsersByRoleOverTime(
        [FromQuery] string? scope,
        [FromQuery] string? bucket = null,
        [FromQuery] Guid? eventId = null,
        [FromQuery] int? clientId = null,
        CancellationToken ct = default)
    {
        // LIVE as of the START of the span: an event that was open during any of it is in the
        // set, and the report counts it only in the buckets it was live in.
        var unit = UsageBuckets.Parse(bucket) ?? UsageBucket.Day;
        var since = UsageBuckets.SinceFor(unit, DateTime.Now);
        var (failure, resolution) = await ResolveForReportAsync(scope, eventId, ct, liveAsOf: since);
        if (failure is not null) return failure;

        return Ok(await _reports.GetUsersByRoleOverTimeAsync(resolution!, unit, since, clientId, ct));
    }

    /// <summary>Resolve scope + event lens for a report, or the ActionResult that refuses it. <paramref name="liveAsOf"/> null = live now.</summary>
    private async Task<(ActionResult? Failure, UsageScopeResolution? Resolution)> ResolveForReportAsync(
        string? scope, Guid? eventId, CancellationToken ct, DateTime? liveAsOf = null)
    {
        var result = await _scopeResolver.ResolveAsync(User, _scopeResolver.Parse(scope), eventId, liveAsOf, ct);
        return result.Failure switch
        {
            UsageScopeFailure.NoJobContext => (BadRequest(new { message = "Job context required" }), null),
            UsageScopeFailure.AboveCeiling => (Forbid(), null),
            UsageScopeFailure.EventNotInScope => (BadRequest(new { message = "Event is not in scope" }), null),
            _ => (null, result.Resolution),
        };
    }
}
