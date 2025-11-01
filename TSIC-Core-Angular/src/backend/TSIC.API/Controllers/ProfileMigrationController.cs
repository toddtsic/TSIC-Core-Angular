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
            
            // Create a one-job report and extract the single result
            var report = await _migrationService.MigrateAllJobsAsync(
                dryRun: false,
                profileTypeFilter: null);
            
            var result = report.Results.FirstOrDefault();
            
            if (result == null)
            {
                return NotFound(new { error = "Job not found or has no profile" });
            }
            
            if (!result.Success)
            {
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
}
