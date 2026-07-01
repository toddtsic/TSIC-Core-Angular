using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using TSIC.API.Extensions;
using TSIC.API.Services.Shared.Jobs;
using TSIC.Contracts.Dtos.Scheduling;
using TSIC.Contracts.Services;

namespace TSIC.API.Controllers;

[ApiController]
[Route("api/scheduling-dashboard")]
[Authorize(Policy = "AdminOnly")]
public class SchedulingDashboardController : ControllerBase
{
    private readonly ISchedulingDashboardService _service;
    private readonly IBracketGenerationService _bracketGen;
    private readonly IJobLookupService _jobLookupService;

    public SchedulingDashboardController(
        ISchedulingDashboardService service,
        IBracketGenerationService bracketGen,
        IJobLookupService jobLookupService)
    {
        _service = service;
        _bracketGen = bracketGen;
        _jobLookupService = jobLookupService;
    }

    [HttpGet("status")]
    [ProducesResponseType<SchedulingDashboardStatusDto>(200)]
    public async Task<IActionResult> GetStatus(CancellationToken ct)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null)
            return BadRequest(new { message = "Scheduling context required" });

        // Admin scheduling entry: backfill any pre-existing bracket data into the
        // new model. Env-agnostic, non-destructive, cheap-skip once materialized.
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!string.IsNullOrEmpty(userId))
            await _bracketGen.BackfillJobAsync(jobId.Value, userId, ct);

        var result = await _service.GetStatusAsync(jobId.Value, ct);
        return Ok(result);
    }
}
