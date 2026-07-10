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
    private readonly IBracketSeedResolutionService _bracketResolution;
    private readonly IViewScheduleService _viewSchedule;
    private readonly IJobLookupService _jobLookupService;

    public SchedulingDashboardController(
        ISchedulingDashboardService service,
        IBracketSeedResolutionService bracketResolution,
        IViewScheduleService viewSchedule,
        IJobLookupService jobLookupService)
    {
        _service = service;
        _bracketResolution = bracketResolution;
        _viewSchedule = viewSchedule;
        _jobLookupService = jobLookupService;
    }

    [HttpGet("status")]
    [ProducesResponseType<SchedulingDashboardStatusDto>(200)]
    public async Task<IActionResult> GetStatus(CancellationToken ct)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null)
            return BadRequest(new { message = "Scheduling context required" });

        // Admin scheduling entry. Resolution materializes any missing bracket wiring
        // first, then drops the ranked teams of every already-final pool into their
        // seed slots — a completed tournament won't be re-scored, so nothing else
        // would ever trigger it. Non-destructive, cheap-skip once done.
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!string.IsNullOrEmpty(userId))
        {
            await _bracketResolution.ResolveJobAsync(
                jobId.Value, userId,
                c => _viewSchedule.GetStandingsAsync(jobId.Value, new ScheduleFilterRequest(), c), ct);
        }

        var result = await _service.GetStatusAsync(jobId.Value, ct);
        return Ok(result);
    }
}
