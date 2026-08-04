using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TSIC.API.Extensions;
using TSIC.API.Services.Shared.Jobs;
using TSIC.Contracts.Dtos.Scheduling;
using TSIC.Contracts.Services;

namespace TSIC.API.Controllers;

/// <summary>
/// Scheduling Checklist — the single front door for scheduling. Returns ordered step
/// readiness (pools → dates → fields → rules → build) with per-step reasons and the
/// build-gate verdict. Read-only: no bracket-seed resolution, no cascade self-heal.
/// </summary>
[ApiController]
[Route("api/scheduling-checklist")]
[Authorize(Policy = "AdminOnly")]
public class SchedulingChecklistController : ControllerBase
{
    private readonly ISchedulingChecklistService _service;
    private readonly IJobLookupService _jobLookupService;

    public SchedulingChecklistController(
        ISchedulingChecklistService service,
        IJobLookupService jobLookupService)
    {
        _service = service;
        _jobLookupService = jobLookupService;
    }

    [HttpGet]
    [ProducesResponseType(typeof(SchedulingChecklistDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<SchedulingChecklistDto>> GetChecklist(CancellationToken ct)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null)
            return BadRequest(new { message = "Scheduling context required" });

        return Ok(await _service.GetChecklistAsync(jobId.Value, ct));
    }
}
