using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TSIC.API.Extensions;
using TSIC.API.Services.Shared.Bulletins;
using TSIC.API.Services.Shared.Bulletins.TokenResolution;
using TSIC.API.Services.Shared.Jobs;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Dtos.Bulletin;

namespace TSIC.API.Controllers;

/// <summary>
/// Controller for managing bulletins (announcements) for jobs.
/// Provides public endpoints for viewing bulletins and admin endpoints for CRUD operations.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class BulletinsController : ControllerBase
{
    private readonly IBulletinService _bulletinService;
    private readonly IJobLookupService _jobLookupService;
    private readonly IJobRepository _jobRepository;
    private readonly BulletinTokenRegistry _tokenRegistry;
    private readonly ILogger<BulletinsController> _logger;

    public BulletinsController(
        IBulletinService bulletinService,
        IJobLookupService jobLookupService,
        IJobRepository jobRepository,
        BulletinTokenRegistry tokenRegistry,
        ILogger<BulletinsController> logger)
    {
        _bulletinService = bulletinService;
        _jobLookupService = jobLookupService;
        _jobRepository = jobRepository;
        _tokenRegistry = tokenRegistry;
        _logger = logger;
    }

    /// <summary>
    /// Get the author-facing catalog of all registered bulletin tokens
    /// with descriptions, gates, and sample resolved HTML. SuperUser-only.
    /// </summary>
    [Authorize(Policy = "SuperUserOnly")]
    [HttpGet("token-catalog")]
    [ProducesResponseType(typeof(List<BulletinTokenCatalogEntryDto>), StatusCodes.Status200OK)]
    public ActionResult<List<BulletinTokenCatalogEntryDto>> GetTokenCatalog()
    {
        var demoCtx = BuildDemoContext();
        var entries = _tokenRegistry.All
            .Select(r => new BulletinTokenCatalogEntryDto
            {
                TokenName = r.TokenName,
                Description = r.Description,
                GatingConditions = r.GatingConditions,
                SampleHtml = r.Resolve(demoCtx)
            })
            .ToList();
        return Ok(entries);
    }

    /// <summary>
    /// Resolve !TOKEN markers in the posted HTML using either the real job
    /// pulse or a SuperUser-supplied override. Used by the editor live-preview.
    /// SuperUser-only.
    /// </summary>
    [Authorize(Policy = "SuperUserOnly")]
    [HttpPost("preview")]
    [ProducesResponseType(typeof(BulletinPreviewResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BulletinPreviewResponse>> PreviewBulletin(
        [FromBody] BulletinPreviewRequest request,
        CancellationToken cancellationToken = default)
    {
        var jobMetadata = await _jobLookupService.GetJobMetadataAsync(request.JobPath);
        if (jobMetadata == null)
        {
            return NotFound($"Job not found: {request.JobPath}");
        }

        var pulse = request.PulseOverride
            ?? await _jobRepository.GetJobPulseAsync(request.JobPath, cancellationToken);
        if (pulse == null)
        {
            return NotFound($"Job pulse unavailable for: {request.JobPath}");
        }

        var ctx = new TokenContext
        {
            JobPath = request.JobPath,
            Job = new TokenJobInfo
            {
                JobName = jobMetadata.JobName,
                USLaxNumberValidThroughDate = jobMetadata.USLaxNumberValidThroughDate
            },
            Pulse = pulse
        };

        var resolved = _tokenRegistry.ResolveTokens(request.Html ?? string.Empty, ctx);
        return Ok(new BulletinPreviewResponse { Html = resolved });
    }

    /// <summary>
    /// Synthetic context with all-flags-true pulse, used to render sample HTML
    /// for the catalog. Job metadata placeholders keep samples context-independent.
    /// </summary>
    private static TokenContext BuildDemoContext()
    {
        return new TokenContext
        {
            JobPath = "your-job",
            Job = new TokenJobInfo
            {
                JobName = "Your Event"
            },
            Pulse = new JobPulseDto
            {
                PlayerRegistrationOpen = true,
                PlayerTeamsAvailableForRegistration = true,
                PlayerRegRequiresToken = false,
                TeamRegistrationOpen = true,
                TeamRegRequiresToken = false,
                ClubRepAllowAdd = true,
                ClubRepAllowEdit = true,
                ClubRepAllowDelete = true,
                AllowRosterViewPlayer = true,
                AllowRosterViewAdult = true,
                OfferPlayerRegsaverInsurance = true,
                OfferTeamRegsaverInsurance = true,
                StoreEnabled = true,
                StoreHasActiveItems = true,
                AllowStoreWalkup = true,
                EnableStayToPlay = true,
                SchedulePublished = true,
                PlayerRegistrationPlanned = true,
                AdultRegistrationPlanned = true,
                PublicSuspended = false,
                RegistrationExpiry = null
            }
        };
    }

    /// <summary>
    /// Get active bulletins for a job (public, anonymous).
    /// </summary>
    [AllowAnonymous]
    [HttpGet("job/{jobPath}")]
    [ProducesResponseType(typeof(IEnumerable<BulletinDto>), 200)]
    public async Task<ActionResult<IEnumerable<BulletinDto>>> GetJobBulletins(
        string jobPath,
        CancellationToken cancellationToken = default)
    {
        var bulletins = await _bulletinService.GetActiveBulletinsForJobAsync(jobPath, cancellationToken);
        return Ok(bulletins ?? new List<BulletinDto>());
    }

    /// <summary>
    /// Get ALL bulletins for the admin editor (includes inactive, no date filter).
    /// </summary>
    [Authorize(Policy = "AdminOnly")]
    [HttpGet("admin")]
    [ProducesResponseType(typeof(List<BulletinAdminDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<List<BulletinAdminDto>>> GetAllBulletins(
        CancellationToken cancellationToken)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null)
        {
            return BadRequest(new { message = "Registration context required" });
        }

        var bulletins = await _bulletinService.GetAllBulletinsForJobAsync(jobId.Value, cancellationToken);
        return Ok(bulletins);
    }

    /// <summary>
    /// Create a new bulletin.
    /// </summary>
    [Authorize(Policy = "AdminOnly")]
    [HttpPost]
    [ProducesResponseType(typeof(BulletinAdminDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<BulletinAdminDto>> CreateBulletin(
        [FromBody] CreateBulletinRequest request,
        CancellationToken cancellationToken)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null)
        {
            return BadRequest(new { message = "Registration context required" });
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized();
        }

        try
        {
            var bulletin = await _bulletinService.CreateBulletinAsync(jobId.Value, userId, request, cancellationToken);
            return CreatedAtAction(nameof(GetAllBulletins), new { }, bulletin);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Update an existing bulletin.
    /// </summary>
    [Authorize(Policy = "AdminOnly")]
    [HttpPut("{bulletinId:guid}")]
    [ProducesResponseType(typeof(BulletinAdminDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BulletinAdminDto>> UpdateBulletin(
        Guid bulletinId,
        [FromBody] UpdateBulletinRequest request,
        CancellationToken cancellationToken)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null)
        {
            return BadRequest(new { message = "Registration context required" });
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized();
        }

        try
        {
            var bulletin = await _bulletinService.UpdateBulletinAsync(bulletinId, jobId.Value, userId, request, cancellationToken);
            return Ok(bulletin);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Delete a bulletin.
    /// </summary>
    [Authorize(Policy = "AdminOnly")]
    [HttpDelete("{bulletinId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> DeleteBulletin(
        Guid bulletinId,
        CancellationToken cancellationToken)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null)
        {
            return BadRequest(new { message = "Registration context required" });
        }

        try
        {
            var deleted = await _bulletinService.DeleteBulletinAsync(bulletinId, jobId.Value, cancellationToken);
            if (!deleted)
            {
                return NotFound(new { message = $"Bulletin with ID {bulletinId} not found" });
            }

            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Batch activate/deactivate all bulletins for the job.
    /// </summary>
    [Authorize(Policy = "AdminOnly")]
    [HttpPost("batch-status")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult> BatchUpdateStatus(
        [FromBody] BatchUpdateBulletinStatusRequest request,
        CancellationToken cancellationToken)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null)
        {
            return BadRequest(new { message = "Registration context required" });
        }

        var count = await _bulletinService.BatchUpdateStatusAsync(jobId.Value, request.Active, cancellationToken);
        return Ok(new { updatedCount = count });
    }
}
