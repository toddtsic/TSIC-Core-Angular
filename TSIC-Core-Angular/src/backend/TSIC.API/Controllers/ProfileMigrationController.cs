using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TSIC.API.Dtos;
using TSIC.API.Services;

namespace TSIC.API.Controllers;

/// <summary>
/// Admin endpoints for migrating player profile metadata from GitHub POCOs
/// </summary>
[Authorize]
[ApiController]
[Route("api/admin/profile-migration")]
public class ProfileMigrationController : ControllerBase
{
    private readonly ProfileMetadataMigrationService _migrationService;
    private readonly ILogger<ProfileMigrationController> _logger;

    public ProfileMigrationController(
        ProfileMetadataMigrationService migrationService,
        ILogger<ProfileMigrationController> logger)
    {
        _migrationService = migrationService;
        _logger = logger;
    }

    /// <summary>
    /// Preview migration for a single job (does not commit to database)
    /// </summary>
    /// <param name="jobId">Job ID to preview</param>
    /// <returns>Migration result with generated metadata</returns>
    [HttpGet("preview/{jobId}")]
    public async Task<ActionResult<MigrationResult>> PreviewMigration(Guid jobId)
    {
        try
        {
            _logger.LogInformation("Previewing migration for job {JobId}", jobId);
            var result = await _migrationService.PreviewMigrationAsync(jobId);

            if (!result.Success)
            {
                return BadRequest(result);
            }

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to preview migration for job {JobId}", jobId);
            return StatusCode(500, new { error = "Migration preview failed", details = ex.Message });
        }
    }

    /// <summary>
    /// Migrate all jobs with player profiles
    /// </summary>
    /// <param name="request">Migration options (dry run, profile type filter)</param>
    /// <returns>Complete migration report</returns>
    [HttpPost("migrate-all")]
    public async Task<ActionResult<MigrationReport>> MigrateAll([FromBody] MigrateAllRequest request)
    {
        try
        {
            _logger.LogInformation(
                "Starting migration (DryRun: {DryRun}, Filter: {Filter})",
                request.DryRun,
                request.ProfileTypes != null ? string.Join(", ", request.ProfileTypes) : "none");

            var report = await _migrationService.MigrateAllJobsAsync(
                request.DryRun,
                request.ProfileTypes);

            return Ok(report);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Migration failed");
            return StatusCode(500, new { error = "Migration failed", details = ex.Message });
        }
    }

    /// <summary>
    /// Migrate a single job (commits to database)
    /// </summary>
    /// <param name="jobId">Job ID to migrate</param>
    /// <returns>Migration result</returns>
    [HttpPost("migrate/{jobId}")]
    public async Task<ActionResult<MigrationResult>> MigrateSingleJob(Guid jobId)
    {
        try
        {
            _logger.LogInformation("Migrating job {JobId}", jobId);

            // Directly migrate the single job (not using MigrateAllJobsAsync to avoid migrating all jobs)
            var result = await _migrationService.MigrateSingleJobAsync(jobId, dryRun: false);

            if (!result.Success)
            {
                if (result.ErrorMessage?.Contains("not found") ?? false)
                {
                    return NotFound(result);
                }
                return BadRequest(result);
            }

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to migrate job {JobId}", jobId);
            return StatusCode(500, new { error = "Migration failed", details = ex.Message });
        }
    }

    // ============================================================================
    // PROFILE-CENTRIC ENDPOINTS (Recommended - more efficient)
    // ============================================================================

    /// <summary>
    /// Get summary of all profile types and their usage across jobs
    /// </summary>
    /// <returns>List of profile summaries showing job counts and migration status</returns>
    [HttpGet("profiles")]
    public async Task<ActionResult<List<ProfileSummary>>> GetProfileSummaries()
    {
        try
        {
            _logger.LogInformation("Getting profile summaries");
            var summaries = await _migrationService.GetProfileSummariesAsync();
            return Ok(summaries);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get profile summaries");
            return StatusCode(500, new { error = "Failed to get profile summaries", details = ex.Message });
        }
    }

    /// <summary>
    /// Preview migration for a single profile type (dry run - does not commit)
    /// </summary>
    /// <param name="profileType">Profile type (e.g., PP10, CAC05)</param>
    /// <returns>Preview of what would be migrated</returns>
    [HttpGet("preview-profile/{profileType}")]
    public async Task<ActionResult<ProfileMigrationResult>> PreviewProfileMigration(string profileType)
    {
        try
        {
            _logger.LogInformation("Previewing migration for profile {ProfileType}", profileType);
            var result = await _migrationService.PreviewProfileMigrationAsync(profileType);

            if (!result.Success)
            {
                return BadRequest(result);
            }

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to preview profile migration for {ProfileType}", profileType);
            return StatusCode(500, new { error = "Preview failed", details = ex.Message });
        }
    }

    /// <summary>
    /// Migrate a single profile type across all jobs using it
    /// </summary>
    /// <param name="profileType">Profile type (e.g., PP10, CAC05)</param>
    /// <returns>Migration result with affected jobs</returns>
    [HttpPost("migrate-profile/{profileType}")]
    public async Task<ActionResult<ProfileMigrationResult>> MigrateProfile(string profileType)
    {
        try
        {
            _logger.LogInformation("Migrating profile {ProfileType}", profileType);
            var result = await _migrationService.MigrateProfileAsync(profileType, dryRun: false);

            if (!result.Success)
            {
                return BadRequest(result);
            }

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to migrate profile {ProfileType}", profileType);
            return StatusCode(500, new { error = "Migration failed", details = ex.Message });
        }
    }

    /// <summary>
    /// Migrate multiple profiles (or all if no filter specified)
    /// </summary>
    /// <param name="request">Migration options with optional profile type filter</param>
    /// <returns>Batch migration report</returns>
    [HttpPost("migrate-all-profiles")]
    public async Task<ActionResult<ProfileBatchMigrationReport>> MigrateAllProfiles([FromBody] MigrateProfilesRequest request)
    {
        try
        {
            _logger.LogInformation(
                "Starting batch profile migration (DryRun: {DryRun}, Filter: {Filter})",
                request.DryRun,
                request.ProfileTypes != null ? string.Join(", ", request.ProfileTypes) : "all");

            var report = await _migrationService.MigrateMultipleProfilesAsync(
                request.DryRun,
                request.ProfileTypes);

            return Ok(report);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Batch profile migration failed");
            return StatusCode(500, new { error = "Migration failed", details = ex.Message });
        }
    }
}
