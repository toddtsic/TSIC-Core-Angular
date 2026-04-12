using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TSIC.API.Extensions;
using TSIC.API.Services.Shared.Bulletins;
using TSIC.API.Services.Shared.Jobs;
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
    private readonly ILogger<BulletinsController> _logger;

    public BulletinsController(
        IBulletinService bulletinService,
        IJobLookupService jobLookupService,
        ILogger<BulletinsController> logger)
    {
        _bulletinService = bulletinService;
        _jobLookupService = jobLookupService;
        _logger = logger;
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
        var bulletins = await _bulletinService.GetActiveBulletinsForJobAsync(jobPath, User, cancellationToken);
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
