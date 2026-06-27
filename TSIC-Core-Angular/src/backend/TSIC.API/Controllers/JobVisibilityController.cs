using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TSIC.API.Extensions;
using TSIC.API.Services.Shared.Jobs;
using TSIC.Contracts.Dtos.JobConfig;
using TSIC.Contracts.Services;
using TSIC.Domain.Constants;

namespace TSIC.API.Controllers;

/// <summary>
/// "Quick Links" editor — a focused editor for the public landing-hero CTA
/// visibility flags of the CURRENT (logged-in) job. AdminOnly (Director,
/// SuperDirector, SuperUser); the insurance/store flags are gated SuperUser-only
/// in the service layer. Writes the same Jobs.Jobs columns Configure Job edits,
/// scoped to the JWT's job (no cross-job picker).
/// </summary>
[ApiController]
[Route("api/job-visibility")]
[Authorize(Policy = "AdminOnly")]
public class JobVisibilityController : ControllerBase
{
    private readonly IJobVisibilityService _service;
    private readonly IJobLookupService _jobLookupService;

    public JobVisibilityController(IJobVisibilityService service, IJobLookupService jobLookupService)
    {
        _service = service;
        _jobLookupService = jobLookupService;
    }

    private bool IsSuperUser =>
        User.IsInRole(RoleConstants.Names.SuperuserName);

    private async Task<Guid?> GetJobIdAsync() =>
        await User.GetJobIdFromRegistrationAsync(_jobLookupService);

    /// <summary>Current job's landing-hero visibility flags.</summary>
    [HttpGet]
    public async Task<ActionResult<JobVisibilityDto>> Get(CancellationToken ct)
    {
        var jobId = await GetJobIdAsync();
        if (jobId is null)
            return NotFound(new { message = "Job not found for current user." });

        return Ok(await _service.GetAsync(jobId.Value, ct));
    }

    /// <summary>Apply a partial update (one or more flags) to the current job.</summary>
    [HttpPut]
    public async Task<IActionResult> Update([FromBody] UpdateJobVisibilityRequest request, CancellationToken ct)
    {
        var jobId = await GetJobIdAsync();
        if (jobId is null)
            return NotFound(new { message = "Job not found for current user." });

        await _service.UpdateAsync(jobId.Value, request, IsSuperUser, ct);
        return NoContent();
    }
}
