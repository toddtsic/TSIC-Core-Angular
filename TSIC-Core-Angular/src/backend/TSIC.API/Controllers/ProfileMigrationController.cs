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
using TSIC.API.Services.Shared.VerticalInsure;
using TSIC.API.Services.Auth;
using TSIC.API.Services.Shared.UsLax;

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
    // Used only by the bulk-migration actions deprecated 2026-08-16; commented out with them
    // so it does not read as live, and restored alongside them if they are ever revived.
    // private const string MigrationFailedMsg = "Migration failed";

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

    // ============================================================================
    // DEPRECATED 2026-08-16 — POST-GO-LIVE LOCKDOWN.
    //
    // RULE: a SuperUser may only read and write the job they are logged into. These
    // bulk/cross-job migration endpoints powered /tools/profile-migration, which was
    // essential BEFORE go-live (bulk-materializing PlayerProfileMetadataJson from the
    // GitHub POCOs) and is dangerous now — every one of them rewrites jobs the caller
    // is not standing in, against a live production database that PROD, STAGING and
    // the legacy app all share.
    //
    // Commented rather than deleted so the pre-go-live migration mechanism stays
    // legible if we ever need to re-materialize a profile family. A commented action
    // does not route, so this is the enforcement — hiding the nav row and the Angular
    // route only removes discoverability.
    //
    // Also disabled with these: the /tools/profile-migration route (app.routes.ts) and
    // its nav row (nav.NavItem.Active = 0, plus the seed line in
    // "scripts/5) Re-Set Nav System.sql").
    //
    // STILL LIVE below: "profiles" (summaries) and "known-profile-types" — both are
    // read-only catalogue lookups the per-job editor depends on.
    // ============================================================================
    /*
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
    */

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

    // ---- DEPRECATED 2026-08-16 (post-go-live lockdown) — see the block above. ----
    // Profile-type-scoped migration: each of these rewrites EVERY job carrying the
    // profile type, which is precisely the fan-out the lockdown exists to remove.
    /*
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
                "Migrating profiles (DryRun: {DryRun}, Filter: {Filter})",
                request.DryRun,
                request.ProfileTypes != null ? string.Join(", ", request.ProfileTypes) : "all");

            var report = await _migrationService.MigrateMultipleProfilesAsync(
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
    /// Export SQL script to apply profile migrations to production database
    /// </summary>
    /// <returns>SQL file download</returns>
    [HttpPost("export-sql")]
    public async Task<IActionResult> ExportMigrationSql()
    {
        try
        {
            _logger.LogInformation("Exporting migration SQL script");
            var sql = await _migrationService.GenerateMigrationSqlScriptAsync();

            var fileName = $"profile-migration-{DateTime.Now:yyyyMMdd-HHmmss}.sql";
            var bytes = System.Text.Encoding.UTF8.GetBytes(sql);

            return File(bytes, "text/plain", fileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to export migration SQL");
            return StatusCode(500, new { error = "SQL export failed", details = ex.Message });
        }
    }
    */

    // ============================================================================
    // ADULT PROFILE ENDPOINTS — materialize role-keyed AdultProfileMetadataJson.
    // Canonical profiles AC1/AC2 are OUR nomenclature, mapped from legacy RegformName_Coach;
    // USLax is an orthogonal per-job capability (a required sportAssnId), never a separate form.
    // ============================================================================

    /// <summary>
    /// The canonical adult profile the CURRENT job is configured for, derived from RegformName_Coach.
    /// The editor opens on this instead of the first profile in the list.
    /// </summary>
    [HttpGet("adult/current/config")]
    public async Task<ActionResult<CurrentJobAdultProfileDto>> GetCurrentJobAdultProfile()
    {
        try
        {
            var regIdClaim = User.FindFirst(RegIdClaim)?.Value;
            if (string.IsNullOrEmpty(regIdClaim) || !Guid.TryParse(regIdClaim, out var regId))
            {
                return BadRequest(new { error = MissingRegIdMsg });
            }

            var result = await _migrationService.GetCurrentJobAdultProfileAsync(regId);
            if (result is null)
            {
                return NotFound(new { error = "Current job not found" });
            }
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get current job adult profile");
            return StatusCode(500, new { error = "Failed to get current job adult profile", details = ex.Message });
        }
    }

    /// <summary>Summarize the canonical adult profiles (AC1/AC2): job counts, USLax counts, migration status.</summary>
    [HttpGet("adult/summary")]
    public async Task<ActionResult<List<AdultProfileSummary>>> GetAdultProfileSummaries()
    {
        try
        {
            var summaries = await _migrationService.GetAdultProfileSummariesAsync();
            return Ok(summaries);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get adult profile summaries");
            return StatusCode(500, new { error = "Failed to get adult profile summaries", details = ex.Message });
        }
    }

    // The bulk adult-materialization endpoints (adult/preview, adult/migrate, adult/migrate-all,
    // adult/export-sql) were removed. Adult forms are DERIVED from Jobs.RegformName_Coach via
    // AdultFormCatalog, so an empty AdultProfileMetadataJson means "use the catalog" rather than
    // "unconfigured" — materializing ~1,034 AC1 jobs would have written a copy of the catalog onto a
    // thousand rows. Configure -> Job -> Adult is the single writer; it sets the identity and the blob
    // together so they cannot desync. The removed force=true path rebuilt the whole three-role blob and
    // would have erased per-job form edits.

    /// <summary>Type-scoped adult editor READ: the role-keyed metadata for a canonical profile (AC1/AC2).</summary>
    [HttpGet("adult-profiles/{profile}/metadata")]
    public async Task<ActionResult<object>> GetAdultProfileMetadata(string profile)
    {
        try
        {
            var set = await _migrationService.GetAdultProfileMetadataAsync(profile);
            return Ok(new { profile, roles = set });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get adult profile metadata for {Profile}", profile);
            return StatusCode(500, new { error = "Failed to get adult profile metadata", details = ex.Message });
        }
    }

    public sealed class UpdateAdultProfileRoleRequest
    {
        public string RoleKey { get; set; } = string.Empty;   // UnassignedAdult | Referee | Recruiter
        public ProfileMetadata Metadata { get; set; } = new();
    }

    /// <summary>Type-scoped adult editor WRITE: replace ONE role's fields across all materialized jobs of a profile.</summary>
    [HttpPut("adult-profiles/{profile}/metadata")]
    public async Task<ActionResult<AdultProfileMigrationResult>> UpdateAdultProfileRole(string profile, [FromBody] UpdateAdultProfileRoleRequest request)
    {
        try
        {
            var result = await _migrationService.UpdateAdultProfileRoleAsync(profile, request.RoleKey, request.Metadata);
            if (!result.Success)
            {
                return BadRequest(result);
            }
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update adult profile {Profile} role", profile);
            return StatusCode(500, new { error = "Failed to update adult profile role", details = ex.Message });
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

    // ============================================================================
    // DEPRECATED 2026-08-16 (post-go-live lockdown) — see the block at the top of this file.
    //
    // Two different violations of "only the job you are logged into":
    //   GET  profiles/{profileType}/preview/{jobId}  — reads ANOTHER job's JsonOptions.
    //   PUT  profiles/{profileType}/metadata         — the template fan-out: rewrites the
    //                                                  player form on EVERY job carrying the
    //                                                  profile type, per-job customizations
    //                                                  included.
    //
    // The template scope ("This template (all jobs)") in the profile editor was the only
    // caller of the PUT, and the migration dashboard the only caller of the GET. Both UIs
    // are gone; these are commented so the fan-out cannot be reached by URL either.
    //
    // The per-job replacement is PUT/GET profiles/current/metadata further down.
    // ============================================================================
    /*
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
    */

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

            var (profileType, teamConstraint, raw, jobId, jobName, metadata) = await _migrationService.GetCurrentJobProfileConfigAsync(regId);
            if (string.IsNullOrEmpty(profileType))
            {
                return NotFound(new { error = "Current job profile configuration not found" });
            }

            // jobName added 2026-08-16: the editor used to resolve its own job's display name out of
            // GET profiles/jobs (every job the SuperUser could edit). That enumeration is gone, so the
            // name comes from the caller's own job here.
            return Ok(new { profileType, teamConstraint, coreRegform = raw, jobId, jobName, metadata });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get current job profile config");
            return StatusCode(500, new { error = "Failed to get current job profile config", details = ex.Message });
        }
    }

    // ============================================================================
    // DEPRECATED 2026-08-16 (post-go-live lockdown).
    //
    // This backed the profile editor's "This Job's Profile Assignment" card (Profile Type +
    // Team Constraint + Apply), which has been removed. Two reasons it is not coming back
    // as-is:
    //
    //  1. It never worked. UpdateCurrentJobProfileConfigAsync passes jobData.JobId into
    //     IProfileMetadataRepository.UpdateJobCoreRegformAndMetadataAsync, which resolves its
    //     argument as a Registrations.RegistrationId — a JobId never matches one, so the write
    //     was skipped and the endpoint still returned 200 with a success payload. Nobody
    //     noticed, which is its own verdict on how much the control was carrying.
    //
    //  2. Repairing it would be worse than leaving it out. Beyond setting the pointer it
    //     re-stamps PlayerProfileMetadataJson from the profile type's canonical field set —
    //     i.e. one unconfirmed click discards whatever per-job field customizations the job
    //     has. That belongs behind a deliberate confirm, not on an Apply button sitting above
    //     the field editor.
    //
    // The pointer (Jobs.CoreRegformPlayer) is set in Configure -> Job -> Player Settings, which
    // resolves its job from the JWT and does NOT touch the stored field set.
    // ============================================================================
    /*
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
    */

    // ============ CURRENT JOB ADULT METADATA (role-keyed) ============

    /// <summary>
    /// Get the current job's role-keyed adult form metadata (all three adult roles), resolved from
    /// the JWT regId. Absent roles come back as an empty { fields: [] } block.
    /// </summary>
    [HttpGet("profiles/current/adult-metadata")]
    public async Task<ActionResult<object>> GetCurrentJobAdultMetadata()
    {
        try
        {
            var regIdClaim = User.FindFirst(RegIdClaim)?.Value;
            if (string.IsNullOrEmpty(regIdClaim) || !Guid.TryParse(regIdClaim, out var regId))
            {
                return BadRequest(new { error = MissingRegIdMsg });
            }

            var set = await _migrationService.GetCurrentJobAdultMetadataAsync(regId);
            if (set == null)
            {
                return NotFound(new { error = "Current job adult metadata not found" });
            }

            return Ok(new { roles = set });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get current job adult metadata");
            return StatusCode(500, new { error = "Failed to get current job adult metadata", details = ex.Message });
        }
    }

    public sealed class UpdateAdultRoleMetadataRequest
    {
        public string RoleKey { get; set; } = string.Empty;   // UnassignedAdult | Referee | Recruiter
        public ProfileMetadata Metadata { get; set; } = new();
    }

    /// <summary>
    /// Replace ONE adult role's field set in the current job's AdultProfileMetadataJson, preserving the
    /// other roles. Returns the normalized metadata that was persisted.
    /// </summary>
    [HttpPut("profiles/current/adult-metadata")]
    public async Task<ActionResult<object>> UpdateCurrentJobAdultRoleMetadata([FromBody] UpdateAdultRoleMetadataRequest request)
    {
        try
        {
            var regIdClaim = User.FindFirst(RegIdClaim)?.Value;
            if (string.IsNullOrEmpty(regIdClaim) || !Guid.TryParse(regIdClaim, out var regId))
            {
                return BadRequest(new { error = MissingRegIdMsg });
            }

            if (!AdultMetadataRoleKeys.IsValid(request.RoleKey))
            {
                return BadRequest(new { error = "Invalid adult role key" });
            }

            var updated = await _migrationService.UpdateCurrentJobAdultRoleMetadataAsync(regId, request.RoleKey, request.Metadata);
            if (updated == null)
            {
                return NotFound(new { error = "Current job not found" });
            }

            return Ok(new { roleKey = request.RoleKey, metadata = updated });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update current job adult role metadata");
            return StatusCode(500, new { error = "Failed to update current job adult metadata", details = ex.Message });
        }
    }

    // ============================================================================
    // DEPRECATED 2026-08-16 (post-go-live lockdown) — Copy Forms.
    //
    // The Copy Forms card seeded this job's player form from another job. Every endpoint
    // here is cross-job by construction: even the "target is my own job" variant READS a
    // job the caller is not in, and profiles/copy-sources enumerates them to populate the
    // picker. Under the post-go-live rule — only the job you are logged into, reads
    // included — there is nothing left to salvage: a copy card whose source must also be
    // the current job does nothing.
    //
    // This is not a capability loss. Job Clone already carries CoreRegformPlayer and
    // PlayerProfileMetadataJson forward verbatim, and JsonOptions WITH grad-year shifting
    // (see JobCloneResetRules) — which is what standing up next season's job actually
    // needs, and is strictly better than this card, which copied the option lists without
    // shifting the years.
    //
    // The CopyForms* service methods are left intact underneath — only the routes are gone —
    // so TSIC.Tests/Metadata/CopyJobFormsTests.cs still compiles and stays green.
    // ============================================================================
    /*
    /// <summary>
    /// Copy another job's player and/or adult (coach) form definition onto the current job (target resolved
    /// from the JWT regId). Form JSON only — the copied forms render immediately from the materialized metadata.
    /// </summary>
    [HttpPost("profiles/current/copy-forms")]
    public async Task<ActionResult<CopyJobFormsResult>> CopyFormsToCurrentJob([FromBody] CopyJobFormsRequest request)
    {
        try
        {
            var regIdClaim = User.FindFirst(RegIdClaim)?.Value;
            if (string.IsNullOrEmpty(regIdClaim) || !Guid.TryParse(regIdClaim, out var regId))
            {
                return BadRequest(new { error = MissingRegIdMsg });
            }

            if (request.SourceJobId == Guid.Empty)
            {
                return BadRequest(new { error = "sourceJobId is required" });
            }

            var result = await _migrationService.CopyFormsToCurrentJobAsync(regId, request);
            if (!result.Success)
            {
                return BadRequest(result);
            }
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to copy forms to current job");
            return StatusCode(500, new { error = "Failed to copy forms", details = ex.Message });
        }
    }

    /// <summary>
    /// List jobs that can serve as a copy source for the current job (each flagged with which form
    /// it carries). The current job is excluded server-side. Feeds the copy-forms source picker.
    /// </summary>
    [HttpGet("profiles/copy-sources")]
    public async Task<ActionResult<List<CopyFormSourceDto>>> GetCopyFormSources()
    {
        try
        {
            var regIdClaim = User.FindFirst(RegIdClaim)?.Value;
            if (string.IsNullOrEmpty(regIdClaim) || !Guid.TryParse(regIdClaim, out var regId))
            {
                return BadRequest(new { error = MissingRegIdMsg });
            }

            var sources = await _migrationService.GetCopyFormSourcesAsync(regId);
            return Ok(sources);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list copy-form sources");
            return StatusCode(500, new { error = "Failed to list copy-form sources", details = ex.Message });
        }
    }
    */

    // ============================================================================
    // CURRENT-JOB PLAYER FORM EDITING (steady-state model, post-go-live lockdown).
    //
    // The job is resolved from the caller's JWT regId and is never accepted as a parameter,
    // matching JobConfigController and every other profiles/current/* action here. The
    // previous shape took a JobId in the route, which meant the safety of the write rested
    // on the client choosing to send its own — see the deprecation note below.
    // ============================================================================

    // NOTE: "form", not "metadata". GET profiles/current/metadata already exists above and returns
    // the profile TYPE's representative field set (seeding only). These two are THIS JOB's stored
    // form — the thing the editor actually reads and writes.
    /// <summary>Read the CURRENT job's player form (job resolved from the JWT regId).</summary>
    [HttpGet("profiles/current/form")]
    public async Task<ActionResult<ProfileMetadata>> GetCurrentJobPlayerForm()
    {
        try
        {
            var regIdClaim = User.FindFirst(RegIdClaim)?.Value;
            if (string.IsNullOrEmpty(regIdClaim) || !Guid.TryParse(regIdClaim, out var regId))
            {
                return BadRequest(new { error = MissingRegIdMsg });
            }

            var metadata = await _migrationService.GetCurrentJobPlayerFormAsync(regId);
            if (metadata == null)
            {
                return NotFound(new { error = "Current job has no player form." });
            }
            return Ok(metadata);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get player form for the current job");
            return StatusCode(500, new { error = "Failed to get current job player form", details = ex.Message });
        }
    }

    /// <summary>
    /// Update the CURRENT job's player form. Single row, single column — there is no parameter
    /// that could name another job, which is the whole point of this shape.
    /// </summary>
    [HttpPut("profiles/current/form")]
    public async Task<ActionResult<object>> UpdateCurrentJobPlayerForm([FromBody] ProfileMetadata metadata)
    {
        try
        {
            var regIdClaim = User.FindFirst(RegIdClaim)?.Value;
            if (string.IsNullOrEmpty(regIdClaim) || !Guid.TryParse(regIdClaim, out var regId))
            {
                return BadRequest(new { error = MissingRegIdMsg });
            }

            var jobId = await _migrationService.UpdateCurrentJobPlayerFormAsync(regId, metadata);
            if (jobId == null)
            {
                return NotFound(new { error = "Current job not found." });
            }
            return Ok(new { jobId, fieldCount = metadata.Fields.Count });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update player form for the current job");
            return StatusCode(500, new { error = "Failed to update current job player form", details = ex.Message });
        }
    }

    // ============================================================================
    // DEPRECATED 2026-08-16 (post-go-live lockdown) — the by-JobId shape and the job list.
    //
    // GET profiles/jobs enumerated every job carrying a player form, purely to populate the
    // editor's "A specific job" picker and the Copy Forms target dropdown. Both are gone, and
    // listing jobs the caller is not in is itself outside the rule.
    //
    // GET/PUT profiles/job/{jobId}/metadata read and wrote an arbitrary job. The write was
    // the editor's real save path, so it is replaced above rather than removed — by a shape
    // with no jobId to supply, so the invariant is structural instead of a comparison a later
    // edit could quietly drop.
    // ============================================================================
    /*
    /// <summary>
    /// List jobs that carry a player form, for the editor's job picker. Each is flagged with its
    /// profile type and whether its field set has drifted from that type's canonical.
    /// </summary>
    [HttpGet("profiles/jobs")]
    public async Task<ActionResult<List<EditableJobDto>>> ListEditableJobs()
    {
        try
        {
            var jobs = await _migrationService.ListEditableJobsAsync();
            return Ok(jobs);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list editable jobs");
            return StatusCode(500, new { error = "Failed to list editable jobs", details = ex.Message });
        }
    }

    /// <summary>Read one job's player form (by JobId).</summary>
    [HttpGet("profiles/job/{jobId:guid}/metadata")]
    public async Task<ActionResult<ProfileMetadata>> GetJobPlayerForm(Guid jobId)
    {
        try
        {
            var metadata = await _migrationService.GetJobPlayerFormAsync(jobId);
            if (metadata == null)
            {
                return NotFound(new { error = $"Job {jobId} has no player form." });
            }
            return Ok(metadata);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get player form for job {JobId}", jobId);
            return StatusCode(500, new { error = "Failed to get job player form", details = ex.Message });
        }
    }

    /// <summary>
    /// Update ONE job's player form (by JobId) — per-job, never a fan-out. This is the default,
    /// safe save path for the editor's "This job" / "A specific job" scopes.
    /// </summary>
    [HttpPut("profiles/job/{jobId:guid}/metadata")]
    public async Task<ActionResult<object>> UpdateJobPlayerForm(Guid jobId, [FromBody] ProfileMetadata metadata)
    {
        try
        {
            var ok = await _migrationService.UpdateJobPlayerFormAsync(jobId, metadata);
            if (!ok)
            {
                return NotFound(new { error = $"Job {jobId} not found." });
            }
            return Ok(new { jobId, fieldCount = metadata.Fields.Count });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update player form for job {JobId}", jobId);
            return StatusCode(500, new { error = "Failed to update job player form", details = ex.Message });
        }
    }
    */

    // ============================================================================
    // DEPRECATED 2026-08-16 (post-go-live lockdown).
    //
    //   GET  profiles/{profileType}/affected-jobs — fed the red template-apply confirm modal
    //        by listing every job the fan-out would overwrite. The fan-out is gone, so its
    //        blast-radius preview goes with it.
    //   POST profiles/copy-forms — the cross-job Copy Forms variant, which honours an explicit
    //        targetJobId and so writes a job the caller is not in. See the Copy Forms note above.
    // ============================================================================
    /*
    /// <summary>
    /// Preview which jobs a template-wide write to <paramref name="profileType"/> would overwrite, with
    /// the customized ones flagged. Feeds the red template-scope confirm modal.
    /// </summary>
    [HttpGet("profiles/{profileType}/affected-jobs")]
    public async Task<ActionResult<AffectedJobsResult>> GetAffectedJobs(string profileType)
    {
        try
        {
            var result = await _migrationService.GetAffectedJobsAsync(profileType);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get affected jobs for profile {ProfileType}", profileType);
            return StatusCode(500, new { error = "Failed to get affected jobs", details = ex.Message });
        }
    }

    /// <summary>
    /// Copy a source job's form(s) INTO an explicit target job (or the caller's current job when
    /// targetJobId is omitted). Optionally carries the profile-type pointer and option sets. All-or-nothing.
    /// </summary>
    [HttpPost("profiles/copy-forms")]
    public async Task<ActionResult<CopyJobFormsResult>> CopyForms([FromBody] CopyJobFormsRequest request)
    {
        try
        {
            var regIdClaim = User.FindFirst(RegIdClaim)?.Value;
            if (string.IsNullOrEmpty(regIdClaim) || !Guid.TryParse(regIdClaim, out var regId))
            {
                return BadRequest(new { error = MissingRegIdMsg });
            }

            if (request.SourceJobId == Guid.Empty)
            {
                return BadRequest(new { error = "sourceJobId is required" });
            }

            var result = await _migrationService.CopyFormsAsync(regId, request);
            if (!result.Success)
            {
                return BadRequest(result);
            }
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to copy forms");
            return StatusCode(500, new { error = "Failed to copy forms", details = ex.Message });
        }
    }
    */

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

    // ============================================================================
    // DEPRECATED 2026-08-16 (post-go-live lockdown) — create-new-profile.
    //
    // Reachable only from the editor's "CREATE NEW" option inside the template-scope picker,
    // which is gone. It also carries the same latent bug as PUT profiles/current/config: it
    // hands a JobId to UpdateJobCoreRegformAndMetadataAsync, which resolves its argument as a
    // Registrations.RegistrationId, so the pointer write silently did nothing.
    //
    // Minting a new PP/CAC profile type is a pre-go-live shaped operation anyway — it seeds a
    // type other jobs then adopt — and does not belong on a screen scoped to one live job.
    // ============================================================================
    /*
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
    */
}

/// <summary>
/// Request model for testing field validation
/// </summary>
public class TestValidationRequest
{
    public ProfileMetadataField Field { get; set; } = new();
    public string TestValue { get; set; } = string.Empty;
}
