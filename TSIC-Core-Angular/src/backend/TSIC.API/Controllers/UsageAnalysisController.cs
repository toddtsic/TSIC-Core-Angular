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
    /// and on every segment change; tabs then query with the same scope word.
    /// </summary>
    [HttpGet("scope")]
    public async Task<ActionResult<UsageAnalysisScopeDto>> GetScope(
        [FromQuery] string? scope,
        CancellationToken ct)
    {
        var requested = _scopeResolver.Parse(scope);
        var result = await _scopeResolver.ResolveAsync(User, requested, ct);

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
    /// Report 01 -- Users by Role. Distinct registrations that used the scoped live events
    /// in the window, grouped by role, admin tier flagged. People only; anonymous traffic
    /// is not a person and is not here.
    /// </summary>
    [HttpGet("users-by-role")]
    public async Task<ActionResult<UsersByRoleDto>> GetUsersByRole(
        [FromQuery] string? scope,
        [FromQuery] int windowDays = 7,
        [FromQuery] bool excludeBots = true,
        CancellationToken ct = default)
    {
        var result = await _scopeResolver.ResolveAsync(User, _scopeResolver.Parse(scope), ct);
        switch (result.Failure)
        {
            case UsageScopeFailure.NoJobContext:
                return BadRequest(new { message = "Job context required" });
            case UsageScopeFailure.AboveCeiling:
                return Forbid();
        }

        var days = Math.Clamp(windowDays, 1, 365);
        return Ok(await _reports.GetUsersByRoleAsync(result.Resolution!, days, excludeBots, ct));
    }
}
