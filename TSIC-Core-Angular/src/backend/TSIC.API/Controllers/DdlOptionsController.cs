using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TSIC.API.Extensions;
using TSIC.API.Services.Shared.Jobs;
using TSIC.Contracts.Dtos.DdlOptions;
using TSIC.Contracts.Services;

namespace TSIC.API.Controllers;

/// <summary>
/// SuperUser-only editor for per-job dropdown list options (Jobs.JsonOptions).
/// These dropdowns appear on player and team registration forms.
/// </summary>
[ApiController]
[Route("api/job-ddl-options")]
[Authorize(Policy = "SuperUserOnly")]
public class DdlOptionsController : ControllerBase
{
    private readonly IDdlOptionsService _service;
    private readonly IJobLookupService _jobLookupService;

    public DdlOptionsController(IDdlOptionsService service, IJobLookupService jobLookupService)
    {
        _service = service;
        _jobLookupService = jobLookupService;
    }

    /// <summary>
    /// Get all 20 dropdown categories for the current job.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<JobDdlOptionsDto>> Get(CancellationToken ct)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId is null)
            return NotFound(new { message = "Job not found for current user." });

        var dto = await _service.GetOptionsAsync(jobId.Value, ct);
        return Ok(dto);
    }

    /// <summary>
    /// Save all 20 dropdown categories for the current job.
    /// </summary>
    [HttpPut]
    public async Task<IActionResult> Save([FromBody] JobDdlOptionsDto dto, CancellationToken ct)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId is null)
            return NotFound(new { message = "Job not found for current user." });

        try
        {
            await _service.SaveOptionsAsync(jobId.Value, dto, ct);
            return NoContent();
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }
}
