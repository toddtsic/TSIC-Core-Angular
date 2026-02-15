using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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
    private readonly IJobLookupService _jobLookupService;

    public SchedulingDashboardController(
        ISchedulingDashboardService service,
        IJobLookupService jobLookupService)
    {
        _service = service;
        _jobLookupService = jobLookupService;
    }

    [HttpGet("status")]
    [ProducesResponseType<SchedulingDashboardStatusDto>(200)]
    public async Task<IActionResult> GetStatus(CancellationToken ct)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null)
            return BadRequest(new { message = "Scheduling context required" });

        var result = await _service.GetStatusAsync(jobId.Value, ct);
        return Ok(result);
    }
}
