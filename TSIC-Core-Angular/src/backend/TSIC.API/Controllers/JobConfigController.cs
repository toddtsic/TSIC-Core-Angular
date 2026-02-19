using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using TSIC.API.Extensions;
using TSIC.API.Services.Shared.Jobs;
using TSIC.Contracts.Dtos.JobConfig;
using TSIC.Contracts.Services;

namespace TSIC.API.Controllers;

/// <summary>
/// SuperUser-only editor for full job configuration.
/// </summary>
[ApiController]
[Route("api/job-config")]
[Authorize(Policy = "SuperUserOnly")]
public class JobConfigController : ControllerBase
{
    private readonly IJobConfigService _configService;
    private readonly IJobLookupService _jobLookupService;

    public JobConfigController(
        IJobConfigService configService,
        IJobLookupService jobLookupService)
    {
        _configService = configService;
        _jobLookupService = jobLookupService;
    }

    /// <summary>
    /// Get the full job configuration for the editor.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<JobConfigDto>> GetConfig(CancellationToken ct)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId is null)
            return NotFound(new { message = "Job not found for current user." });

        var result = await _configService.GetConfigAsync(jobId.Value, ct);
        if (result is null)
            return NotFound(new { message = "Job configuration not found." });

        return Ok(result);
    }

    /// <summary>
    /// Get lookup data (JobTypes, Sports, BillingTypes) for the editor dropdowns.
    /// </summary>
    [HttpGet("lookups")]
    public async Task<ActionResult<JobConfigLookupsDto>> GetLookups(CancellationToken ct)
    {
        var result = await _configService.GetLookupsAsync(ct);
        return Ok(result);
    }

    /// <summary>
    /// Update job configuration. Returns 409 on concurrency conflict.
    /// </summary>
    [HttpPut]
    public async Task<ActionResult<JobConfigDto>> UpdateConfig(
        [FromBody] UpdateJobConfigRequest request, CancellationToken ct)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId is null)
            return NotFound(new { message = "Job not found for current user." });

        if (request.JobId != jobId.Value)
            return BadRequest(new { message = "Job ID mismatch." });

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown";

        var result = await _configService.UpdateConfigAsync(jobId.Value, request, userId, ct);
        if (result is null)
            return Conflict(new { message = "Configuration was modified by another user. Please reload." });

        return Ok(result);
    }
}
