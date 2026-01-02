using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TSIC.Contracts.Dtos;
using TSIC.API.Services.Shared.Bulletins;

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
    private readonly ILogger<BulletinsController> _logger;

    public BulletinsController(
        IBulletinService bulletinService,
        ILogger<BulletinsController> logger)
    {
        _bulletinService = bulletinService;
        _logger = logger;
    }

    /// <summary>
    /// Get active bulletins for a job, sorted by start date (newest first).
    /// Public endpoint for anonymous users on job landing page.
    /// Bulletins include token replacement (legacy URL translation moved to frontend).
    /// No caching - always returns fresh data.
    /// </summary>
    /// <param name="jobPath">Job path segment (e.g. summer-showcase-2025)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Collection of active bulletins with processed text</returns>
    [AllowAnonymous]
    [HttpGet("job/{jobPath}")]
    [ProducesResponseType(typeof(IEnumerable<BulletinDto>), 200)]
    [ProducesResponseType(404)]
    public async Task<ActionResult<IEnumerable<BulletinDto>>> GetJobBulletins(
        string jobPath,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Fetching active bulletins for job: {JobPath}", jobPath);

        var bulletins = await _bulletinService.GetActiveBulletinsForJobAsync(jobPath, cancellationToken);

        if (bulletins == null || bulletins.Count == 0)
        {
            _logger.LogInformation("No active bulletins found for job: {JobPath}", jobPath);
            return Ok(Array.Empty<BulletinDto>());
        }

        return Ok(bulletins);
    }

    // TODO: Admin endpoints for bulletin CRUD operations
    // [Authorize(Policy = "AdminOnly")]
    // [HttpPost]
    // public async Task<ActionResult<BulletinDto>> CreateBulletin([FromBody] CreateBulletinRequest request) { }

    // [Authorize(Policy = "AdminOnly")]
    // [HttpPut("{bulletinId}")]
    // public async Task<ActionResult<BulletinDto>> UpdateBulletin(Guid bulletinId, [FromBody] UpdateBulletinRequest request) { }

    // [Authorize(Policy = "AdminOnly")]
    // [HttpDelete("{bulletinId}")]
    // public async Task<ActionResult> DeleteBulletin(Guid bulletinId) { }
}
