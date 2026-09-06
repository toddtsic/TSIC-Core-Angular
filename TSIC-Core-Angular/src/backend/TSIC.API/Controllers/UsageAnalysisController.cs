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

    public UsageAnalysisController(IUsageScopeResolver scopeResolver, IUsageStatsRepository usageRepo)
    {
        _scopeResolver = scopeResolver;
        _usageRepo = usageRepo;
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
}
