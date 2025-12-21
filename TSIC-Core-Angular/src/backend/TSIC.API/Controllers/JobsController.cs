using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Services;
using TSIC.API.Services.Players;
using TSIC.API.Services.Teams;
using TSIC.API.Services.Families;
using TSIC.API.Services.Clubs;
using TSIC.API.Services.Payments;
using TSIC.API.Services.Metadata;
using TSIC.API.Services.Shared;
using TSIC.API.Services.Shared.Adn;
using TSIC.API.Services.Auth;
using TSIC.API.Services.Email;
using TSIC.API.Services.Shared.UsLax;

namespace TSIC.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class JobsController : ControllerBase
{
    private readonly ILogger<JobsController> _logger;
    private readonly IJobLookupService _jobLookupService;
    private readonly ITeamLookupService _teamLookupService;

    public JobsController(
        ILogger<JobsController> logger,
        IJobLookupService jobLookupService,
        ITeamLookupService teamLookupService)
    {
        _logger = logger;
        _jobLookupService = jobLookupService;
        _teamLookupService = teamLookupService;
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
            JsonOptions = jobMetadata.JsonOptions,
            MomLabel = jobMetadata.MomLabel,
            DadLabel = jobMetadata.DadLabel,
            PlayerRegReleaseOfLiability = jobMetadata.PlayerRegReleaseOfLiability,
            PlayerRegCodeOfConduct = jobMetadata.PlayerRegCodeOfConduct,
            PlayerRegCovid19Waiver = jobMetadata.PlayerRegCovid19Waiver,
            PlayerRegRefundPolicy = jobMetadata.PlayerRegRefundPolicy,
            OfferPlayerRegsaverInsurance = jobMetadata.OfferPlayerRegsaverInsurance,
            AdnArb = jobMetadata.AdnArb,
            AdnArbBillingOccurences = jobMetadata.AdnArbBillingOccurences,
            AdnArbIntervalLength = jobMetadata.AdnArbIntervalLength,
            AdnArbStartDate = jobMetadata.AdnArbStartDate
        };

        return Ok(response);
    }

    /// <summary>
    /// Lists teams available for player self-rostering within the given job.
    /// Mirrors core legacy filtering rules (active, self-rostering flags, date windows, roster capacity).
    /// NOTE: Waitlist substitution logic from legacy has not yet been ported (placeholder fields included for future work).
    /// </summary>
    /// <param name="jobPath">Job path segment (e.g. summer-showcase-2025)</param>
    /// <returns>Collection of available teams with capacity metadata.</returns>
    [AllowAnonymous]
    [HttpGet("{jobPath}/available-teams")]
    public async Task<ActionResult<IEnumerable<AvailableTeamDto>>> GetAvailableTeams(string jobPath)
    {
        _logger.LogInformation("Fetching available teams for job: {JobPath}", jobPath);

        var jobId = await _jobLookupService.GetJobIdByPathAsync(jobPath);
        if (jobId == null)
        {
            return NotFound(new { message = $"Job not found: {jobPath}" });
        }

        var teams = await _teamLookupService.GetAvailableTeamsForJobAsync(jobId.Value);
        return Ok(teams);
    }
}
