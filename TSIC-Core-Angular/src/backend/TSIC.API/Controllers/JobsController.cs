using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TSIC.API.DTOs;
using TSIC.API.Services;

namespace TSIC.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class JobsController : ControllerBase
{
    private readonly ILogger<JobsController> _logger;
    private readonly IJobLookupService _jobLookupService;

    public JobsController(
        ILogger<JobsController> logger,
        IJobLookupService jobLookupService)
    {
        _logger = logger;
        _jobLookupService = jobLookupService;
    }

    [AllowAnonymous]
    [HttpGet("{jobPath}")]
    public async Task<ActionResult<JobMetadataResponse>> GetJobMetadata(string jobPath)
    {
        _logger.LogInformation("Fetching job metadata for: {JobPath}", jobPath);

        var jobMetadata = await _jobLookupService.GetJobMetadataAsync(jobPath);

        if (jobMetadata == null)
        {
            return NotFound(new { message = $"Job not found: {jobPath}" });
        }

        // Map to response DTO
        var response = new JobMetadataResponse
        {
            JobId = jobMetadata.JobId,
            JobName = jobMetadata.JobName,
            JobPath = jobMetadata.JobPath,
            JobLogoPath = jobMetadata.JobLogoPath,
            JobBannerPath = jobMetadata.JobBannerPath,
            CoreRegformPlayer = jobMetadata.CoreRegformPlayer,
            USLaxNumberValidThroughDate = jobMetadata.USLaxNumberValidThroughDate,
            ExpiryUsers = jobMetadata.ExpiryUsers,
            PlayerProfileMetadataJson = jobMetadata.PlayerProfileMetadataJson,
            JsonOptions = jobMetadata.JsonOptions
        };

        return Ok(response);
    }
}
