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
using TSIC.API.Services.External;
using TSIC.API.Services.Auth;
using TSIC.API.Services.Email;
using TSIC.API.Services.Validation;

namespace TSIC.API.Controllers;

/// <summary>
/// Admin endpoints for migrating player profile metadata from GitHub POCOs
/// Restricted to Superuser role only
/// </summary>
[Authorize(Policy = "SuperUserOnly")]
[ApiController]
[Route("api/admin/profile-migration")]
public class ProfileMigrationController : ControllerBase
{
    private readonly ProfileMetadataMigrationService _migrationService;
    private readonly ILogger<ProfileMigrationController> _logger;
    private const string RegIdClaim = "regId";
    private const string MissingRegIdMsg = "Invalid or missing regId claim";
    private const string MigrationFailedMsg = "Migration failed";

    public ProfileMigrationController(
        ProfileMetadataMigrationService migrationService,
        ILogger<ProfileMigrationController> logger)
    {
        _migrationService = migrationService;
        _logger = logger;
    }

    /// <summary>
    /// Get the next profile type name for a given source profile family (PP/CAC), without creating it
    /// </summary>
    [HttpGet("next-profile-type/{sourceProfileType}")]
    public async Task<ActionResult<NextProfileTypeResult>> GetNextProfileType(string sourceProfileType)
    {
        try
        {
            var next = await _migrationService.GetNextProfileTypeAsync(sourceProfileType);
            return Ok(new NextProfileTypeResult { NewProfileType = next });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to compute next profile type for {SourceProfile}", sourceProfileType);
            return StatusCode(500, new { error = "Failed to compute next profile type", details = ex.Message });
        }
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
            _logger.LogError(ex, MigrationFailedMsg);
            return StatusCode(500, new { error = MigrationFailedMsg, details = ex.Message });
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
            return StatusCode(500, new { error = MigrationFailedMsg, details = ex.Message });
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
    /// Get all known profile types based on Jobs.PlayerProfileMetadataJson presence (no GitHub dependency)
    /// </summary>
    [HttpGet("known-profile-types")]
    public async Task<ActionResult<List<string>>> GetKnownProfileTypes()
    {
        try
        {
            var types = await _migrationService.GetKnownProfileTypesAsync();
            return Ok(types);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get known profile types");
            return StatusCode(500, new { error = "Failed to get known profile types", details = ex.Message });
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
            return StatusCode(500, new { error = MigrationFailedMsg, details = ex.Message });
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
            return StatusCode(500, new { error = MigrationFailedMsg, details = ex.Message });
        }
    }

    // ============================================================================
    // PROFILE EDITOR ENDPOINTS (for ongoing metadata management)
    // ============================================================================

    /// <summary>
    /// Build and return a distinct domain of allowed fields observed across all Jobs.PlayerProfileMetadataJson
    /// Intended for one-time export to seed the UI's static allowed-fields list.
    /// </summary>
    [HttpGet("allowed-field-domain")]
    public async Task<ActionResult<List<AllowedFieldDomainItem>>> GetAllowedFieldDomain()
    {
        try
        {
            _logger.LogInformation("Building allowed field domain from PlayerProfileMetadataJson");
            var list = await _migrationService.BuildAllowedFieldDomainAsync();
            return Ok(list);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to build allowed field domain");
            return StatusCode(500, new { error = "Failed to build allowed field domain", details = ex.Message });
        }
    }

    /// <summary>
    /// Get current metadata for a specific profile type
    /// </summary>
    /// <param name="profileType">Profile type (e.g., PP10, CAC05)</param>
    /// <returns>Current metadata for the profile</returns>
    [HttpGet("profiles/{profileType}/metadata")]
    public async Task<ActionResult<ProfileMetadata>> GetProfileMetadata(string profileType)
    {
        try
        {
            _logger.LogInformation("Getting metadata for profile {ProfileType}", profileType);
            var metadata = await _migrationService.GetProfileMetadataAsync(profileType);

            if (metadata == null)
            {
                return NotFound(new { error = $"No metadata found for profile {profileType}. Run migration first." });
            }

            return Ok(metadata);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get metadata for profile {ProfileType}", profileType);
            return StatusCode(500, new { error = "Failed to get metadata", details = ex.Message });
        }
    }

    /// <summary>
    /// Get metadata for a specific profile type enriched with a specific job's JsonOptions
    /// This allows previewing how the form will appear for a particular job
    /// </summary>
    /// <param name="profileType">Profile type (e.g., PP10, CAC05)</param>
    /// <param name="jobId">Job ID to get JsonOptions from</param>
    /// <returns>Metadata enriched with job-specific dropdown options</returns>
    [HttpGet("profiles/{profileType}/preview/{jobId:guid}")]
    public async Task<ActionResult<ProfileMetadataWithOptions>> GetProfileMetadataWithJobOptions(
        string profileType,
        Guid jobId)
    {
        try
        {
            _logger.LogInformation("Getting metadata for profile {ProfileType} with options from job {JobId}",
                profileType, jobId);

            var result = await _migrationService.GetProfileMetadataWithJobOptionsAsync(profileType, jobId);

            if (result == null)
            {
                return NotFound(new { error = $"No metadata found for profile {profileType} or job {jobId} not found." });
            }

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get metadata for profile {ProfileType} with job {JobId} options",
                profileType, jobId);
            return StatusCode(500, new { error = "Failed to get metadata with job options", details = ex.Message });
        }
    }

    /// <summary>
    /// Update metadata for a profile type (applies to ALL jobs using it)
    /// </summary>
    /// <param name="profileType">Profile type (e.g., PP10, CAC05)</param>
    /// <param name="metadata">Updated metadata</param>
    /// <returns>Result showing affected jobs</returns>
    [HttpPut("profiles/{profileType}/metadata")]
    public async Task<ActionResult<ProfileMigrationResult>> UpdateProfileMetadata(
        string profileType,
        [FromBody] ProfileMetadata metadata)
    {
        try
        {
            _logger.LogInformation("Updating metadata for profile {ProfileType}", profileType);
            var result = await _migrationService.UpdateProfileMetadataAsync(profileType, metadata);

            if (!result.Success)
            {
                return BadRequest(result);
            }

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update metadata for profile {ProfileType}", profileType);
            return StatusCode(500, new { error = "Failed to update metadata", details = ex.Message });
        }
    }

    // ============================================================================
    // CURRENT JOB OPTION SETS (Jobs.JsonOptions) — Phase 1
    // ============================================================================

    /// <summary>
    /// Get current job option sets from Jobs.JsonOptions
    /// </summary>
    [HttpGet("profiles/current/options")]
    public async Task<ActionResult<List<OptionSet>>> GetCurrentJobOptionSets()
    {
        try
        {
            var regIdClaim = User.FindFirst(RegIdClaim)?.Value;
            if (string.IsNullOrEmpty(regIdClaim) || !Guid.TryParse(regIdClaim, out var regId))
            {
                return BadRequest(new { error = MissingRegIdMsg });
            }

            var optionSets = await _migrationService.GetCurrentJobOptionSetsAsync(regId);
            // No schema change—client can correlate; we just return the sets
            return Ok(optionSets);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get current job option sets");
            return StatusCode(500, new { error = "Failed to get option sets", details = ex.Message });
        }
    }

    /// <summary>
    /// Create a new current job option set in Jobs.JsonOptions
    /// </summary>
    [HttpPost("profiles/current/options")]
    public async Task<ActionResult<OptionSet>> CreateCurrentJobOptionSet([FromBody] OptionSet request)
    {
        try
        {
            var regIdClaim = User.FindFirst(RegIdClaim)?.Value;
            if (string.IsNullOrEmpty(regIdClaim) || !Guid.TryParse(regIdClaim, out var regId))
            {
                return BadRequest(new { error = MissingRegIdMsg });
            }

            if (string.IsNullOrWhiteSpace(request.Key))
            {
                return BadRequest(new { error = "Option set key is required" });
            }

            var updated = await _migrationService.UpsertCurrentJobOptionSetAsync(regId, request.Key, request.Values);
            if (updated == null)
            {
                return StatusCode(500, new { error = "Failed to create option set" });
            }
            return Ok(updated);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create option set");
            return StatusCode(500, new { error = "Failed to create option set", details = ex.Message });
        }
    }

    /// <summary>
    /// Update values of an existing option set
    /// </summary>
    [HttpPut("profiles/current/options/{key}")]
    public async Task<ActionResult<OptionSet>> UpdateCurrentJobOptionSet(string key, [FromBody] OptionSetUpdateRequest request)
    {
        try
        {
            var regIdClaim = User.FindFirst(RegIdClaim)?.Value;
            if (string.IsNullOrEmpty(regIdClaim) || !Guid.TryParse(regIdClaim, out var regId))
            {
                return BadRequest(new { error = MissingRegIdMsg });
            }

            var updated = await _migrationService.UpsertCurrentJobOptionSetAsync(regId, key, request.Values);
            if (updated == null)
            {
                return NotFound(new { error = $"Option set '{key}' not found" });
            }
            return Ok(updated);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update option set {Key}", key);
            return StatusCode(500, new { error = "Failed to update option set", details = ex.Message });
        }
    }

    /// <summary>
    /// Delete an option set
    /// </summary>
    [HttpDelete("profiles/current/options/{key}")]
    public async Task<ActionResult> DeleteCurrentJobOptionSet(string key)
    {
        try
        {
            var regIdClaim = User.FindFirst(RegIdClaim)?.Value;
            if (string.IsNullOrEmpty(regIdClaim) || !Guid.TryParse(regIdClaim, out var regId))
            {
                return BadRequest(new { error = MissingRegIdMsg });
            }

            var ok = await _migrationService.DeleteCurrentJobOptionSetAsync(regId, key);
            if (!ok)
            {
                return NotFound(new { error = $"Option set '{key}' not found" });
            }
            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete option set {Key}", key);
            return StatusCode(500, new { error = "Failed to delete option set", details = ex.Message });
        }
    }

    /// <summary>
    /// Rename an option set key and return referencing fields for guidance
    /// </summary>
    [HttpPost("profiles/current/options/{oldKey}/rename")]
    public async Task<ActionResult<object>> RenameCurrentJobOptionSet(string oldKey, [FromBody] RenameOptionSetRequest request)
    {
        try
        {
            var regIdClaim = User.FindFirst(RegIdClaim)?.Value;
            if (string.IsNullOrEmpty(regIdClaim) || !Guid.TryParse(regIdClaim, out var regId))
            {
                return BadRequest(new { error = MissingRegIdMsg });
            }

            // Compute referencing fields BEFORE rename
            var (_, metadata) = await _migrationService.GetCurrentJobProfileMetadataAsync(regId);
            var referencing = metadata?.Fields
                .Where(f => !string.IsNullOrEmpty(f.DataSource) && f.DataSource!.Equals(oldKey, StringComparison.OrdinalIgnoreCase))
                .Select(f => f.Name)
                .ToList() ?? new List<string>();

            var ok = await _migrationService.RenameCurrentJobOptionSetAsync(regId, oldKey, request.NewKey);
            if (!ok)
            {
                return NotFound(new { error = $"Option set '{oldKey}' not found" });
            }

            return Ok(new { updatedKey = request.NewKey, referencingFields = referencing });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to rename option set {OldKey}", oldKey);
            return StatusCode(500, new { error = "Failed to rename option set", details = ex.Message });
        }
    }
    /// <summary>
    /// Get the current job's profile metadata using the regId from JWT claims
    /// Returns both the resolved profileType and the metadata
    /// </summary>
    [HttpGet("profiles/current/metadata")]
    public async Task<ActionResult<object>> GetCurrentJobProfileMetadata()
    {
        try
        {
            var regIdClaim = User.FindFirst(RegIdClaim)?.Value;
            if (string.IsNullOrEmpty(regIdClaim) || !Guid.TryParse(regIdClaim, out var regId))
            {
                return BadRequest(new { error = MissingRegIdMsg });
            }

            var (profileType, metadata) = await _migrationService.GetCurrentJobProfileMetadataAsync(regId);
            if (string.IsNullOrEmpty(profileType) || metadata == null)
            {
                return NotFound(new { error = "Current job or profile metadata not found" });
            }

            return Ok(new { profileType, metadata });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get current job profile metadata");
            return StatusCode(500, new { error = "Failed to get current job metadata", details = ex.Message });
        }
    }

    /// <summary>
    /// Get the current job's player profile configuration (CoreRegformPlayer parts)
    /// </summary>
    [HttpGet("profiles/current/config")]
    public async Task<ActionResult<object>> GetCurrentJobProfileConfig()
    {
        try
        {
            var regIdClaim = User.FindFirst(RegIdClaim)?.Value;
            if (string.IsNullOrEmpty(regIdClaim) || !Guid.TryParse(regIdClaim, out var regId))
            {
                return BadRequest(new { error = MissingRegIdMsg });
            }

            var (profileType, teamConstraint, raw, metadata) = await _migrationService.GetCurrentJobProfileConfigAsync(regId);
            if (string.IsNullOrEmpty(profileType))
            {
                return NotFound(new { error = "Current job profile configuration not found" });
            }

            return Ok(new { profileType, teamConstraint, coreRegform = raw, metadata });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get current job profile config");
            return StatusCode(500, new { error = "Failed to get current job profile config", details = ex.Message });
        }
    }

    public sealed class UpdateCurrentJobProfileConfigRequest
    {
        public string ProfileType { get; set; } = string.Empty;
        public string TeamConstraint { get; set; } = string.Empty;
    }

    /// <summary>
    /// Update the current job's player profile configuration (CoreRegformPlayer) and refresh metadata.
    /// </summary>
    [HttpPut("profiles/current/config")]
    public async Task<ActionResult<object>> UpdateCurrentJobProfileConfig([FromBody] UpdateCurrentJobProfileConfigRequest request)
    {
        try
        {
            var regIdClaim = User.FindFirst(RegIdClaim)?.Value;
            if (string.IsNullOrEmpty(regIdClaim) || !Guid.TryParse(regIdClaim, out var regId))
            {
                return BadRequest(new { error = MissingRegIdMsg });
            }

            if (string.IsNullOrWhiteSpace(request.ProfileType))
            {
                return BadRequest(new { error = "profileType is required" });
            }

            var team = request.TeamConstraint ?? string.Empty;
            var (profileType, teamConstraint, raw, metadata) =
                await _migrationService.UpdateCurrentJobProfileConfigAsync(regId, request.ProfileType, team);

            return Ok(new { profileType, teamConstraint, coreRegform = raw, metadata });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update current job profile config");
            return StatusCode(500, new { error = "Failed to update current job profile config", details = ex.Message });
        }
    }

    // (Deprecated) CURRENT JOB OPTION SOURCES endpoints removed. Source discovery has been retired from the UI.

    /// <summary>
    /// Test field validation rules
    /// </summary>
    /// <param name="field">Field metadata with validation rules</param>
    /// <param name="testValue">Value to test</param>
    /// <returns>Validation test result</returns>
    [HttpPost("test-validation")]
    public ActionResult<ValidationTestResult> TestValidation(
        [FromBody] TestValidationRequest request)
    {
        try
        {
            var result = _migrationService.TestFieldValidation(request.Field, request.TestValue);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to test validation");
            return StatusCode(500, new { error = "Validation test failed", details = ex.Message });
        }
    }

    /// <summary>
    /// Create a new profile by cloning an existing one with auto-incremented name
    /// The new profile is specific to the current user's job (from regId claim)
    /// </summary>
    /// <param name="request">Clone profile request with source profile type</param>
    /// <returns>Result with new profile name</returns>
    [HttpPost("clone-profile")]
    public async Task<ActionResult<CloneProfileResult>> CloneProfile(
        [FromBody] CloneProfileRequest request)
    {
        try
        {
            // Get regId from JWT claims
            var regIdClaim = User.FindFirst(RegIdClaim)?.Value;
            if (string.IsNullOrEmpty(regIdClaim) || !Guid.TryParse(regIdClaim, out var regId))
            {
                return BadRequest(new { error = MissingRegIdMsg });
            }

            _logger.LogInformation("Cloning profile from {SourceProfile} for regId {RegId}",
                request.SourceProfileType, regId);

            var result = await _migrationService.CloneProfileAsync(request.SourceProfileType, regId);

            if (!result.Success)
            {
                return BadRequest(result);
            }

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clone profile from {SourceProfile}",
                request.SourceProfileType);
            return StatusCode(500, new { error = "Failed to clone profile", details = ex.Message });
        }
    }
}

/// <summary>
/// Request model for testing field validation
/// </summary>
public class TestValidationRequest
{
    public ProfileMetadataField Field { get; set; } = new();
    public string TestValue { get; set; } = string.Empty;
}
