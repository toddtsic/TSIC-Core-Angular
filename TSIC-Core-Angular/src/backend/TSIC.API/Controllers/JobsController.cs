using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.API.Services.Players;
using TSIC.API.Services.Teams;
using TSIC.API.Services.Families;
using TSIC.API.Services.Clubs;
using TSIC.API.Services.Payments;
using TSIC.API.Services.Metadata;
using TSIC.API.Services.Shared;
using TSIC.API.Services.Shared.Adn;
using TSIC.API.Services.Shared.Jobs;
using TSIC.API.Services.Shared.TextSubstitution;
using TSIC.API.Services.Auth;
using TSIC.API.Services.Shared.UsLax;

namespace TSIC.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class JobsController : ControllerBase
{
    private readonly ILogger<JobsController> _logger;
    private readonly IJobLookupService _jobLookupService;
    private readonly ITeamLookupService _teamLookupService;
    private readonly IBulletinRepository _bulletinRepository;
    private readonly IMenuRepository _menuRepository;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public JobsController(
        ILogger<JobsController> logger,
        IJobLookupService jobLookupService,
        ITeamLookupService teamLookupService,
        IBulletinRepository bulletinRepository,
        IMenuRepository menuRepository,
        IHttpContextAccessor httpContextAccessor)
    {
        _logger = logger;
        _jobLookupService = jobLookupService;
        _teamLookupService = teamLookupService;
        _bulletinRepository = bulletinRepository;
        _menuRepository = menuRepository;
        _httpContextAccessor = httpContextAccessor;
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

    /// <summary>
    /// Get active bulletins for a job, sorted by start date (newest first).
    /// Public endpoint for anonymous users on job landing page.
    /// </summary>
    /// <param name="jobPath">Job path segment (e.g. summer-showcase-2025)</param>
    /// <returns>Collection of active bulletins</returns>
    [AllowAnonymous]
    [HttpGet("{jobPath}/bulletins")]
    [ProducesResponseType(typeof(IEnumerable<BulletinDto>), 200)]
    [ProducesResponseType(404)]
    public async Task<ActionResult<IEnumerable<BulletinDto>>> GetJobBulletins(string jobPath)
    {
        _logger.LogInformation("Fetching active bulletins for job: {JobPath}", jobPath);

        var jobMetadata = await _jobLookupService.GetJobMetadataAsync(jobPath);
        if (jobMetadata == null)
        {
            return NotFound(new { message = $"Job not found: {jobPath}" });
        }

        var bulletins = await _bulletinRepository.GetActiveBulletinsForJobAsync(jobMetadata.JobId);

        // Process bulletin title and text with job-level token replacement (in-memory, no DB calls)
        var jobName = jobMetadata.JobName;
        var uslaxDate = jobMetadata.USLaxNumberValidThroughDate?.ToString("M/d/yy") ?? string.Empty;

        var processedBulletins = new List<BulletinDto>();
        foreach (var bulletin in bulletins)
        {
            var processedTitle = (bulletin.Title ?? string.Empty)
                .Replace("!JOBNAME", jobName, StringComparison.OrdinalIgnoreCase)
                .Replace("!USLAXVALIDTHROUGHDATE", uslaxDate, StringComparison.OrdinalIgnoreCase);

            var processedText = (bulletin.Text ?? string.Empty)
                .Replace("!JOBNAME", jobName, StringComparison.OrdinalIgnoreCase)
                .Replace("!USLAXVALIDTHROUGHDATE", uslaxDate, StringComparison.OrdinalIgnoreCase);

            processedBulletins.Add(new BulletinDto
            {
                BulletinId = bulletin.BulletinId,
                Title = processedTitle,
                Text = processedText,
                StartDate = bulletin.StartDate,
                EndDate = bulletin.EndDate,
                CreateDate = bulletin.CreateDate
            });
        }

        // Generate ETag based on job path and bulletin IDs (changes when bulletins added/removed)
        var bulletinIds = string.Join("-", processedBulletins.Select(b => b.BulletinId));
        var etag = $"\"{jobPath}-bulletins-{bulletinIds}\"";

        // Check If-None-Match header
        var requestEtag = Request.Headers.IfNoneMatch.ToString();
        if (!string.IsNullOrEmpty(requestEtag) && requestEtag == etag)
        {
            return StatusCode(304); // Not Modified
        }

        // Set caching headers (public, anonymous content - 5 min fresh + 30 min stale)
        Response.Headers.Append("ETag", etag);
        Response.Headers.Append("Cache-Control", "public, max-age=300, stale-while-revalidate=1800");

        return Ok(processedBulletins);
    }

    /// <summary>
    /// Get role-specific menu for a job with hierarchical structure.
    /// Returns best-fit menu based on JWT roleId claim (role-specific → anonymous fallback).
    /// Public endpoint supports anonymous users (returns menu with roleId NULL).
    /// Implements caching with ETag for efficient bandwidth usage.
    /// </summary>
    /// <param name="jobPath">Job path segment (e.g. summer-showcase-2025)</param>
    /// <returns>Menu with hierarchical items, or 404 if no menu found</returns>
    [AllowAnonymous]
    [HttpGet("{jobPath}/menus")]
    [ProducesResponseType(typeof(MenuDto), 200)]
    [ProducesResponseType(304)]
    [ProducesResponseType(404)]
    public async Task<ActionResult<MenuDto>> GetJobMenus(string jobPath)
    {
        _logger.LogInformation("Fetching menus for job: {JobPath}", jobPath);

        var jobId = await _jobLookupService.GetJobIdByPathAsync(jobPath);
        if (jobId == null)
        {
            return NotFound(new { message = $"Job not found: {jobPath}" });
        }

        // Extract role name from JWT claims (null for anonymous users)
        string? roleName = null;
        if (_httpContextAccessor.HttpContext?.User.Identity?.IsAuthenticated == true)
        {
            roleName = _httpContextAccessor.HttpContext.User.FindFirst(ClaimTypes.Role)?.Value;
        }

        var menu = await _menuRepository.GetMenuForJobAndRoleAsync(jobId.Value, roleName);

        if (menu == null)
        {
            return NotFound(new { message = $"No menu found for job: {jobPath}" });
        }

        // Load job metadata once for token replacement (in-memory, no repeated DB calls)
        var jobMetadata = await _jobLookupService.GetJobMetadataAsync(jobPath);
        if (jobMetadata != null)
        {
            var jobName = jobMetadata.JobName;
            var uslaxDate = jobMetadata.USLaxNumberValidThroughDate?.ToString("M/d/yy") ?? string.Empty;

            // Process menu item text with job-level token replacement
            foreach (var item in menu.Items)
            {
                if (!string.IsNullOrEmpty(item.Text))
                {
                    item.Text = item.Text
                        .Replace("!JOBNAME", jobName, StringComparison.OrdinalIgnoreCase)
                        .Replace("!USLAXVALIDTHROUGHDATE", uslaxDate, StringComparison.OrdinalIgnoreCase);
                }

                // Process child items
                foreach (var child in item.Children)
                {
                    if (!string.IsNullOrEmpty(child.Text))
                    {
                        child.Text = child.Text
                            .Replace("!JOBNAME", jobName, StringComparison.OrdinalIgnoreCase)
                            .Replace("!USLAXVALIDTHROUGHDATE", uslaxDate, StringComparison.OrdinalIgnoreCase);
                    }
                }
            }
        }

        // Generate ETag based on menu ID and role (for 304 Not Modified support)
        var etag = $"\"{menu.MenuId}-{roleName ?? "anonymous"}\"";

        // Check If-None-Match header
        var requestEtag = Request.Headers.IfNoneMatch.ToString();
        if (!string.IsNullOrEmpty(requestEtag) && requestEtag == etag)
        {
            return StatusCode(304); // Not Modified
        }

        // Set caching headers
        Response.Headers.Append("ETag", etag);
        if (roleName == null)
        {
            // Anonymous menus: aggressive caching (5 minutes)
            Response.Headers.Append("Cache-Control", "public, max-age=300, stale-while-revalidate=1800");
        }
        else
        {
            // Role-specific menus: shorter cache (1 minute)
            Response.Headers.Append("Cache-Control", "private, max-age=60, stale-while-revalidate=300");
        }

        return Ok(menu);
    }
}
