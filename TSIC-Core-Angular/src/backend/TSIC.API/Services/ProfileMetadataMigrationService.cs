using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using TSIC.API.Dtos;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Data.SqlDbContext;

namespace TSIC.API.Services;

/// <summary>
/// Orchestrates migration of player profile metadata from GitHub POCOs to Jobs.PlayerProfileMetadataJson
/// </summary>
public class ProfileMetadataMigrationService
{
    private const string UnknownValue = "Unknown";

    private readonly SqlDbContext _context;
    private readonly GitHubProfileFetcher _githubFetcher;
    private readonly CSharpToMetadataParser _parser;
    private readonly ILogger<ProfileMetadataMigrationService> _logger;

    // Profiles to be hidden from summaries and batch operations derived from summaries
    // Case-insensitive to avoid casing mismatches from upstream data
    private static readonly HashSet<string> ExcludedProfileTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "PP1_Player_Regform"
    };

    // Special legacy CoreRegformPlayer marker to exclude at query-time
    private const string CoreRegformExcludeMarker = "PP1_Player_RegForm";

    // Shared JSON serializer options
    private static readonly JsonSerializerOptions s_IndentedCamelCase = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly JsonSerializerOptions s_CaseInsensitive = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public ProfileMetadataMigrationService(
        SqlDbContext context,
        GitHubProfileFetcher githubFetcher,
        CSharpToMetadataParser parser,
        ILogger<ProfileMetadataMigrationService> logger)
    {
        _context = context;
        _githubFetcher = githubFetcher;
        _parser = parser;
        _logger = logger;
    }

    /// <summary>
    /// Preview migration for a single job without committing changes
    /// </summary>
    public async Task<MigrationResult> PreviewMigrationAsync(Guid jobId)
    {
        return await MigrateJobAsync(jobId, dryRun: true);
    }

    /// <summary>
    /// Migrate a single job (commits to database)
    /// </summary>
    public async Task<MigrationResult> MigrateSingleJobAsync(Guid jobId, bool dryRun = false)
    {
        var result = await MigrateJobAsync(jobId, dryRun);
        return result;
    }

    /// <summary>
    /// Migrate all jobs with valid CoreRegformPlayer profiles
    /// </summary>
    public async Task<MigrationReport> MigrateAllJobsAsync(bool dryRun = false, List<string>? profileTypeFilter = null)
    {
        var report = new MigrationReport
        {
            StartedAt = DateTime.UtcNow
        };

        try
        {
            // Get all jobs with a CoreRegformPlayer setting
            var jobs = await _context.Jobs
                .AsNoTracking()
                .Where(j => !string.IsNullOrEmpty(j.CoreRegformPlayer) && (!j.CoreRegformPlayer.Contains(CoreRegformExcludeMarker)))
                .Select(j => new { j.JobId, j.JobName, j.CoreRegformPlayer })
                .ToListAsync();

            foreach (var job in jobs)
            {
                try
                {
                    // Extract profile type from CoreRegformPlayer (e.g., "PP10|BYGRADYEAR|ALLOWPIF" -> "PP10")
                    var profileType = ExtractProfileType(job.CoreRegformPlayer);

                    if (string.IsNullOrEmpty(profileType))
                    {
                        report.SkippedCount++;
                        report.GlobalWarnings.Add($"Job {job.JobName}: Could not extract profile type from '{job.CoreRegformPlayer}'");
                        continue;
                    }

                    // Apply filter if specified
                    if (profileTypeFilter != null && profileTypeFilter.Count > 0 && !profileTypeFilter.Contains(profileType))
                    {
                        report.SkippedCount++;
                        continue;
                    }

                    var result = await MigrateJobAsync(job.JobId, dryRun);
                    report.Results.Add(result);

                    if (result.Success)
                        report.SuccessCount++;
                    else
                        report.FailureCount++;

                    if (result.Warnings.Count > 0)
                        report.WarningCount += result.Warnings.Count;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error migrating job {JobId}", job.JobId);
                    report.Results.Add(new MigrationResult
                    {
                        JobId = job.JobId,
                        JobName = job.JobName ?? UnknownValue,
                        ProfileType = UnknownValue,
                        Success = false,
                        ErrorMessage = ex.Message
                    });
                    report.FailureCount++;
                }
            }

            report.CompletedAt = DateTime.UtcNow;

            _logger.LogInformation(
                "Migration {Mode} completed: {Success} succeeded, {Failed} failed, {Skipped} skipped, {Warnings} warnings",
                dryRun ? "preview" : "execution",
                report.SuccessCount,
                report.FailureCount,
                report.SkippedCount,
                report.WarningCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Migration failed");
            report.CompletedAt = DateTime.UtcNow;
            report.GlobalWarnings.Add($"Migration failed: {ex.Message}");
        }

        return report;
    }

    /// <summary>
    /// Migrate a single job
    /// </summary>
    private async Task<MigrationResult> MigrateJobAsync(Guid jobId, bool dryRun)
    {
        var job = await _context.Jobs
            .Where(j => j.JobId == jobId)
            .Select(j => new { j.JobId, j.JobName, j.CoreRegformPlayer })
            .FirstOrDefaultAsync();

        if (job == null)
        {
            return new MigrationResult
            {
                JobId = jobId,
                JobName = UnknownValue,
                ProfileType = UnknownValue,
                Success = false,
                ErrorMessage = "Job not found"
            };
        }

        var profileType = ExtractProfileType(job.CoreRegformPlayer);

        if (string.IsNullOrEmpty(profileType))
        {
            return new MigrationResult
            {
                JobId = jobId,
                JobName = job.JobName ?? UnknownValue,
                ProfileType = UnknownValue,
                Success = false,
                ErrorMessage = $"Could not extract profile type from '{job.CoreRegformPlayer}'"
            };
        }

        var result = new MigrationResult
        {
            JobId = jobId,
            JobName = job.JobName ?? UnknownValue,
            ProfileType = profileType,
            Success = false
        };

        try
        {
            _logger.LogInformation("Migrating job {JobName} ({JobId}) with profile {ProfileType}",
                job.JobName, jobId, profileType);

            // Fetch profile source from GitHub
            var (profileSource, profileSha) = await _githubFetcher.FetchProfileSourceAsync(profileType);
            var (baseSource, _) = await _githubFetcher.FetchBaseClassSourceAsync();

            // Fetch corresponding .cshtml view file for hidden field detection
            var viewContent = await _githubFetcher.FetchViewFileAsync(profileType);

            // Parse into metadata
            var metadata = await _parser.ParseProfileAsync(profileSource, baseSource, profileType, profileSha, viewContent);

            // Ensure order numbers are consecutive starting from 1 (in case of any gaps from skipped fields)
            for (int i = 0; i < metadata.Fields.Count; i++)
            {
                metadata.Fields[i].Order = i + 1;
            }

            result.FieldCount = metadata.Fields.Count;
            result.GeneratedMetadata = metadata;

            // Serialize to JSON
            var metadataJson = JsonSerializer.Serialize(metadata, s_IndentedCamelCase);

            {
                var jobEntity = await _context.Jobs.FindAsync(jobId);
                if (jobEntity != null)
                {
                    jobEntity.PlayerProfileMetadataJson = metadataJson;
                    await _context.SaveChangesAsync();

                    _logger.LogInformation(
                        "Updated PlayerProfileMetadataJson for job {JobName} with {FieldCount} fields",
                        job.JobName, metadata.Fields.Count);
                }
            }

            result.Success = true;

            // Add warnings if any
            if (metadata.Fields.Exists(f => string.IsNullOrEmpty(f.DataSource) && f.InputType == "SELECT"))
            {
                result.Warnings.Add("Some SELECT fields are missing dataSource mapping");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to migrate job {JobId}", jobId);
            result.Success = false;
            result.ErrorMessage = ex.Message;
        }

        return result;
    }

    /// <summary>
    /// Extract profile type from CoreRegformPlayer string
    /// Examples: "PP10|BYGRADYEAR|ALLOWPIF" -> "PP10", "CAC05|BYAGEGROUP" -> "CAC05"
    /// </summary>
    private static string? ExtractProfileType(string? coreRegformPlayer)
    {
        if (string.IsNullOrEmpty(coreRegformPlayer))
            return null;

        // Handle boolean "1" or "0" values (old jobs)
        if (coreRegformPlayer == "1" || coreRegformPlayer == "0")
            return null;

        // Split by pipe and take first segment
        var parts = coreRegformPlayer.Split('|');
        if (parts.Length == 0)
            return null;

        var profileType = parts[0].Trim();

        // Validate format (PP## or CAC##)
        if (!profileType.StartsWith("PP") && !profileType.StartsWith("CAC"))
            return null;

        return profileType;
    }

    // ============================================================================
    // PROFILE-CENTRIC MIGRATION METHODS
    // ============================================================================

    /// <summary>
    /// Get summary of all unique profile types and their usage
    /// </summary>
    public async Task<List<ProfileSummary>> GetProfileSummariesAsync()
    {
        var jobs = await _context.Jobs
            .AsNoTracking()
            .Where(j => !string.IsNullOrEmpty(j.CoreRegformPlayer) && (!j.CoreRegformPlayer.Contains(CoreRegformExcludeMarker)))
            .Select(j => new { j.JobId, j.JobName, j.CoreRegformPlayer, j.PlayerProfileMetadataJson })
            .ToListAsync();

        var profileGroups = jobs
            .Select(j => new
            {
                j.JobId,
                j.JobName,
                ProfileType = ExtractProfileType(j.CoreRegformPlayer),
                HasMetadata = !string.IsNullOrEmpty(j.PlayerProfileMetadataJson)
            })
            .Where(j => !string.IsNullOrEmpty(j.ProfileType))
            // Exclude any profile types explicitly configured to be hidden
            .Where(j => j.ProfileType != null && !ExcludedProfileTypes.Contains(j.ProfileType))
            .GroupBy(j => j.ProfileType!)
            .Select(g => new ProfileSummary
            {
                ProfileType = g.Key,
                JobCount = g.Count(),
                MigratedJobCount = g.Count(j => j.HasMetadata),
                AllJobsMigrated = g.All(j => j.HasMetadata),
                SampleJobNames = g.Take(5).Select(j => j.JobName ?? "Unnamed Job").ToList()
            })
            .OrderBy(p => p.ProfileType)
            .ToList();

        return profileGroups;
    }

    /// <summary>
    /// Preview migration for a single profile type (dry run)
    /// </summary>
    public async Task<ProfileMigrationResult> PreviewProfileMigrationAsync(string profileType)
    {
        return await MigrateProfileAsync(profileType, dryRun: true);
    }

    /// <summary>
    /// Migrate a single profile type across all jobs using it
    /// </summary>
    public async Task<ProfileMigrationResult> MigrateProfileAsync(string profileType, bool dryRun = false)
    {
        var result = new ProfileMigrationResult
        {
            ProfileType = profileType,
            Success = false
        };

        try
        {
            _logger.LogInformation("Migrating profile {ProfileType} (DryRun: {DryRun})", profileType, dryRun);

            // 1. Fetch from GitHub ONCE
            var (profileSource, profileSha) = await _githubFetcher.FetchProfileSourceAsync(profileType);
            var (baseSource, _) = await _githubFetcher.FetchBaseClassSourceAsync();

            // Fetch corresponding .cshtml view file for hidden field detection
            var viewContent = await _githubFetcher.FetchViewFileAsync(profileType);

            // 2. Parse ONCE
            var metadata = await _parser.ParseProfileAsync(profileSource, baseSource, profileType, profileSha, viewContent);

            // Ensure order numbers are consecutive starting from 1 (in case of any gaps from skipped fields)
            for (int i = 0; i < metadata.Fields.Count; i++)
            {
                metadata.Fields[i].Order = i + 1;
            }

            result.FieldCount = metadata.Fields.Count;
            result.GeneratedMetadata = metadata;

            // 3. Find ALL jobs using this profile
            var jobs = await _context.Jobs
                .Where(j => j.CoreRegformPlayer != null &&
                           (j.CoreRegformPlayer.StartsWith(profileType + "|") ||
                            j.CoreRegformPlayer == profileType))
                .ToListAsync();

            result.JobsAffected = jobs.Count;
            result.AffectedJobIds = jobs.Select(j => j.JobId).ToList();
            result.AffectedJobNames = jobs.Select(j => j.JobName ?? "Unnamed Job").ToList();
            result.AffectedJobYears = jobs.Select(j => j.Year ?? "").ToList();

            if (jobs.Count == 0)
            {
                result.Success = true;
                result.Warnings.Add($"No jobs found using profile type {profileType}");
                return result;
            }

            // 4. Serialize metadata JSON options
            var serializerOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            // 5. Apply to ALL jobs with JOB-SPECIFIC dropdown options (if not dry run)
            if (!dryRun)
            {
                foreach (var job in jobs)
                {
                    // Clone the base metadata for this specific job
                    var jobSpecificMetadata = CloneMetadata(metadata);

                    // Inject this job's dropdown options into SELECT fields
                    InjectJobOptionsIntoMetadata(jobSpecificMetadata, job.JsonOptions);

                    // Serialize job-specific metadata
                    var metadataJson = JsonSerializer.Serialize(jobSpecificMetadata, serializerOptions);
                    job.PlayerProfileMetadataJson = metadataJson;
                }

                await _context.SaveChangesAsync();

                _logger.LogInformation(
                    "Applied {ProfileType} metadata ({FieldCount} fields) to {JobCount} jobs with job-specific dropdown options",
                    profileType, metadata.Fields.Count, jobs.Count);
            }

            result.Success = true;

            // Add warnings if any
            if (metadata.Fields.Exists(f => string.IsNullOrEmpty(f.DataSource) && f.InputType == "SELECT"))
            {
                result.Warnings.Add("Some SELECT fields are missing dataSource mapping");
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to migrate profile {ProfileType}", profileType);
            result.Success = false;
            result.ErrorMessage = ex.Message;
            return result;
        }
    }

    /// <summary>
    /// Migrate multiple profiles (or all profiles if filter is null/empty)
    /// </summary>
    public async Task<ProfileBatchMigrationReport> MigrateMultipleProfilesAsync(
        bool dryRun = false,
        List<string>? profileTypeFilter = null)
    {
        var report = new ProfileBatchMigrationReport
        {
            StartedAt = DateTime.UtcNow
        };

        try
        {
            // Get all unique profile types
            var summaries = await GetProfileSummariesAsync();

            // Apply filter if specified
            var profilesToMigrate = summaries
                .Where(s => profileTypeFilter == null || profileTypeFilter.Count == 0 || profileTypeFilter.Contains(s.ProfileType))
                .Select(s => s.ProfileType)
                .ToList();

            report.TotalProfiles = profilesToMigrate.Count;

            _logger.LogInformation(
                "Starting batch profile migration: {Count} profiles (DryRun: {DryRun})",
                profilesToMigrate.Count, dryRun);

            // Migrate each profile
            foreach (var profileType in profilesToMigrate)
            {
                try
                {
                    var result = await MigrateProfileAsync(profileType, dryRun);
                    report.Results.Add(result);

                    if (result.Success)
                    {
                        report.SuccessCount++;
                        report.TotalJobsAffected += result.JobsAffected;
                    }
                    else
                    {
                        report.FailureCount++;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error migrating profile {ProfileType}", profileType);
                    report.Results.Add(new ProfileMigrationResult
                    {
                        ProfileType = profileType,
                        Success = false,
                        ErrorMessage = ex.Message
                    });
                    report.FailureCount++;
                }
            }

            report.CompletedAt = DateTime.UtcNow;

            _logger.LogInformation(
                "Batch profile migration completed: {Success} succeeded, {Failed} failed, {TotalJobs} total jobs affected",
                report.SuccessCount, report.FailureCount, report.TotalJobsAffected);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Batch profile migration failed");
            report.CompletedAt = DateTime.UtcNow;
            report.GlobalWarnings.Add($"Migration failed: {ex.Message}");
        }

        return report;
    }

    // ============================================================================
    // PROFILE EDITOR METHODS (for ongoing metadata management)
    // ============================================================================

    /// <summary>
    /// Get current metadata for a specific profile type
    /// </summary>
    public async Task<ProfileMetadata?> GetProfileMetadataAsync(string profileType)
    {
        // Get any job using this profile (they all have the same metadata)
        var job = await _context.Jobs
            .AsNoTracking()
            .Where(j => j.CoreRegformPlayer != null &&
                        (!j.CoreRegformPlayer.Contains(CoreRegformExcludeMarker)) &&
                       (j.CoreRegformPlayer.StartsWith(profileType + "|") ||
                        j.CoreRegformPlayer == profileType) &&
                       !string.IsNullOrEmpty(j.PlayerProfileMetadataJson))
            .Select(j => j.PlayerProfileMetadataJson)
            .FirstOrDefaultAsync();

        if (string.IsNullOrEmpty(job))
        {
            return null;
        }

        var metadata = JsonSerializer.Deserialize<ProfileMetadata>(job, s_CaseInsensitive);

        return metadata;
    }

    /// <summary>
    /// Get metadata for a profile type enriched with a specific job's JsonOptions
    /// This allows previewing how the form will appear for a particular job
    /// </summary>
    public async Task<ProfileMetadataWithOptions?> GetProfileMetadataWithJobOptionsAsync(
        string profileType,
        Guid jobId)
    {
        _logger.LogInformation("Getting metadata for {ProfileType} with options from job {JobId}",
            profileType, jobId);

        // Get the metadata for this profile type
        var metadata = await GetProfileMetadataAsync(profileType);
        if (metadata == null)
        {
            _logger.LogWarning("No metadata found for profile type {ProfileType}", profileType);
            return null;
        }

        // Get the specific job's JsonOptions
        var job = await _context.Jobs
            .AsNoTracking()
            .Where(j => j.JobId == jobId)
            .Select(j => new { j.JobId, j.JobName, j.JsonOptions })
            .FirstOrDefaultAsync();

        if (job == null)
        {
            _logger.LogWarning("Job {JobId} not found", jobId);
            return null;
        }

        // Parse JsonOptions if available
        Dictionary<string, object>? jsonOptions = null;
        if (!string.IsNullOrEmpty(job.JsonOptions))
        {
            try
            {
                jsonOptions = JsonSerializer.Deserialize<Dictionary<string, object>>(job.JsonOptions, s_CaseInsensitive);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse JsonOptions for job {JobId}", jobId);
            }
        }

        return new ProfileMetadataWithOptions
        {
            JobId = job.JobId,
            JobName = job.JobName ?? "Unknown Job",
            Metadata = metadata,
            JsonOptions = jsonOptions
        };
    }

    /// <summary>
    /// Update metadata for a profile type (applies to ALL jobs using it)
    /// </summary>
    public async Task<ProfileMigrationResult> UpdateProfileMetadataAsync(string profileType, ProfileMetadata metadata)
    {
        var result = new ProfileMigrationResult
        {
            ProfileType = profileType,
            Success = false
        };

        try
        {
            _logger.LogInformation("Updating metadata for profile {ProfileType}", profileType);

            // Find ALL jobs using this profile
            var jobs = await _context.Jobs
                .Where(j => j.CoreRegformPlayer != null &&
                           (j.CoreRegformPlayer.StartsWith(profileType + "|") ||
                            j.CoreRegformPlayer == profileType))
                .ToListAsync();

            if (jobs.Count == 0)
            {
                result.Success = false;
                result.ErrorMessage = $"No jobs found using profile type {profileType}";
                return result;
            }

            result.JobsAffected = jobs.Count;
            result.AffectedJobIds = jobs.Select(j => j.JobId).ToList();
            result.AffectedJobNames = jobs.Select(j => j.JobName ?? "Unnamed Job").ToList();
            result.FieldCount = metadata.Fields.Count;

            // Update source tracking
            metadata.Source ??= new ProfileMetadataSource();
            metadata.Source.MigratedAt = DateTime.UtcNow;
            metadata.Source.MigratedBy = "ProfileEditor";

            // Serialize metadata
            var jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
            var metadataJson = JsonSerializer.Serialize(metadata, jsonOptions);

            // Apply to ALL jobs
            foreach (var job in jobs)
            {
                job.PlayerProfileMetadataJson = metadataJson;
            }

            await _context.SaveChangesAsync();

            _logger.LogInformation(
                "Updated metadata for {ProfileType}: {FieldCount} fields applied to {JobCount} jobs",
                profileType, metadata.Fields.Count, jobs.Count);

            result.Success = true;
            result.GeneratedMetadata = metadata;

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update metadata for profile {ProfileType}", profileType);
            result.Success = false;
            result.ErrorMessage = ex.Message;
            return result;
        }
    }

    /// <summary>
    /// Test field validation rules without saving
    /// </summary>
    public ValidationTestResult TestFieldValidation(ProfileMetadataField field, string testValue)
    {
        var result = new ValidationTestResult
        {
            FieldName = field.Name,
            TestValue = testValue,
            IsValid = true,
            Messages = new List<string>()
        };

        if (field.Validation == null)
        {
            result.Messages.Add("No validation rules defined");
            return result;
        }

        // Test required
        if (field.Validation.Required && string.IsNullOrWhiteSpace(testValue))
        {
            result.IsValid = false;
            result.Messages.Add("Field is required");
        }

        // Test requiredTrue (for checkboxes)
        if (field.Validation.RequiredTrue)
        {
            if (!bool.TryParse(testValue, out var boolValue) || !boolValue)
            {
                result.IsValid = false;
                result.Messages.Add("Checkbox must be checked (value must be true)");
            }
        }

        if (!string.IsNullOrWhiteSpace(testValue))
        {
            // Test min/max length
            if (field.Validation.MinLength.HasValue && testValue.Length < field.Validation.MinLength.Value)
            {
                result.IsValid = false;
                result.Messages.Add($"Value too short (min: {field.Validation.MinLength})");
            }

            if (field.Validation.MaxLength.HasValue && testValue.Length > field.Validation.MaxLength.Value)
            {
                result.IsValid = false;
                result.Messages.Add($"Value too long (max: {field.Validation.MaxLength})");
            }

            // Test numeric range
            if (field.InputType == "NUMBER" && double.TryParse(testValue, out var numValue))
            {
                if (field.Validation.Min.HasValue && numValue < field.Validation.Min.Value)
                {
                    result.IsValid = false;
                    result.Messages.Add($"Value too small (min: {field.Validation.Min})");
                }

                if (field.Validation.Max.HasValue && numValue > field.Validation.Max.Value)
                {
                    result.IsValid = false;
                    result.Messages.Add($"Value too large (max: {field.Validation.Max})");
                }
            }

            // Test pattern
            if (!string.IsNullOrEmpty(field.Validation.Pattern))
            {
                var regex = new System.Text.RegularExpressions.Regex(field.Validation.Pattern);
                if (!regex.IsMatch(testValue))
                {
                    result.IsValid = false;
                    result.Messages.Add($"Value does not match required pattern: {field.Validation.Pattern}");
                }
            }

            // Test email
            if (field.Validation.Email && field.InputType == "EMAIL")
            {
                var emailRegex = new System.Text.RegularExpressions.Regex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$");
                if (!emailRegex.IsMatch(testValue))
                {
                    result.IsValid = false;
                    result.Messages.Add("Invalid email format");
                }
            }
        }

        if (result.IsValid && result.Messages.Count == 0)
        {
            result.Messages.Add("✓ Validation passed");
        }

        return result;
    }

    /// <summary>
    /// Clone an existing profile with auto-incremented name FOR THE CURRENT JOB
    /// Gets the job from the registration ID (regId claim from JWT token)
    /// Finds the max version of the profile base name and creates new profile as +1
    /// Example: PlayerProfile -> PlayerProfile2, CoachProfile3 -> CoachProfile4
    /// The new profile is only available within the context of jobs that explicitly use it
    /// </summary>
    public async Task<CloneProfileResult> CloneProfileAsync(string sourceProfileType, Guid regId)
    {
        var result = new CloneProfileResult
        {
            SourceProfileType = sourceProfileType
        };

        try
        {
            // Get the source profile metadata
            var sourceMetadata = await GetProfileMetadataAsync(sourceProfileType);
            if (sourceMetadata == null)
            {
                result.Success = false;
                result.ErrorMessage = $"Source profile '{sourceProfileType}' not found";
                return result;
            }

            // Get the job from the registration
            var registration = await _context.Registrations
                .Include(r => r.Job)
                .FirstOrDefaultAsync(r => r.RegistrationId == regId);

            if (registration == null)
            {
                result.Success = false;
                result.ErrorMessage = $"Registration with ID '{regId}' not found";
                return result;
            }

            var job = registration.Job;
            if (job == null)
            {
                result.Success = false;
                result.ErrorMessage = $"Job not found for registration '{regId}'";
                return result;
            }

            // Determine base name and find max version for THIS job
            var newProfileType = GenerateNewProfileName(sourceProfileType);

            // Create metadata for new profile (clone from source)
            var newMetadata = CloneMetadata(sourceMetadata);
            var metadataJson = JsonSerializer.Serialize(newMetadata);

            // Update the current job to use the new profile
            job.CoreRegformPlayer = newProfileType;
            job.PlayerProfileMetadataJson = metadataJson;
            await _context.SaveChangesAsync();

            _logger.LogInformation(
                "Created new profile type: {NewProfileType} from {SourceProfileType} for job {JobName} (JobId: {JobId})",
                newProfileType, sourceProfileType, job.JobName, job.JobId);

            result.Success = true;
            result.NewProfileType = newProfileType;
            result.FieldCount = newMetadata.Fields.Count;

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cloning profile {SourceProfileType}", sourceProfileType);
            result.Success = false;
            result.ErrorMessage = ex.Message;
            return result;
        }
    }

    /// <summary>
    /// Generate new profile name by finding max version for a specific job and incrementing
    /// </summary>
    private static string GenerateNewProfileName(string sourceProfileType)
    {
        // Extract base name (remove trailing numbers if any)
        var baseName = System.Text.RegularExpressions.Regex.Replace(
            sourceProfileType,
            @"\d+$",
            string.Empty
        );

        // Extract version from source
        var sourceMatch = System.Text.RegularExpressions.Regex.Match(sourceProfileType, @"(\d+)$");
        var sourceVersion = sourceMatch.Success && int.TryParse(sourceMatch.Groups[1].Value, out var sv) ? sv : 1;

        // Return incremented version
        return $"{baseName}{sourceVersion + 1}";
    }

    /// <summary>
    /// Clone metadata structure (deep copy)
    /// </summary>
    private ProfileMetadata CloneMetadata(ProfileMetadata source)
    {
        // Deep clone via JSON serialization
        var json = JsonSerializer.Serialize(source);
        return JsonSerializer.Deserialize<ProfileMetadata>(json)
            ?? new ProfileMetadata { Fields = new List<ProfileMetadataField>() };
    }

    /// <summary>
    /// Inject job-specific dropdown options into SELECT fields in the metadata
    /// Maps Job.JsonOptions into ProfileMetadataField.Options for each SELECT field
    /// </summary>
    /// <param name="metadata">The metadata to modify</param>
    /// <param name="jsonOptionsString">The job's JsonOptions property (JSON string)</param>
    private void InjectJobOptionsIntoMetadata(ProfileMetadata metadata, string? jsonOptionsString)
    {
        if (string.IsNullOrWhiteSpace(jsonOptionsString))
        {
            _logger.LogDebug("No JsonOptions available for this job");
            return;
        }

        try
        {
            // Parse the JsonOptions string
            var jsonOptions = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(jsonOptionsString,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (jsonOptions == null)
            {
                _logger.LogWarning("Failed to deserialize JsonOptions");
                return;
            }

            // Find all SELECT fields and inject their options
            foreach (var field in metadata.Fields.Where(f => f.InputType == "SELECT"))
            {
                if (string.IsNullOrEmpty(field.DataSource))
                {
                    _logger.LogDebug("SELECT field {FieldName} has no DataSource, skipping", field.Name);
                    continue;
                }

                // Try to find matching key in JsonOptions
                // DataSource might be "positions", JsonOptions key might be "List_Positions"
                var optionsKey = FindJsonOptionsKey(jsonOptions, field.DataSource);

                if (optionsKey == null)
                {
                    _logger.LogDebug("No JsonOptions key found for DataSource '{DataSource}'", field.DataSource);
                    continue;
                }

                var jsonElement = jsonOptions[optionsKey];

                // Parse the array of options
                if (jsonElement.ValueKind == JsonValueKind.Array)
                {
                    var options = ParseJsonOptionsArray(jsonElement);
                    if (options.Count > 0)
                    {
                        field.Options = options;
                        _logger.LogDebug("Injected {Count} options into field {FieldName} from key {Key}",
                            options.Count, field.Name, optionsKey);
                    }
                }
                else
                {
                    _logger.LogWarning("JsonOptions key {Key} is not an array", optionsKey);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error injecting job options into metadata");
        }
    }

    /// <summary>
    /// Find the JsonOptions key that matches the field's DataSource
    /// Examples: "positions" -> "List_Positions", "jerseySize" -> "ListSizes_Jersey"
    /// </summary>
    private static string? FindJsonOptionsKey(Dictionary<string, JsonElement> jsonOptions, string dataSource)
    {
        // Normalize helper: lowercase and remove non-alphanumerics for flexible matching
        static string Normalize(string s)
            => new string(s.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

        // Generate candidate variants from the dataSource
        // Examples:
        // - positions -> [positions, listpositions]
        // - List_Positions -> [listpositions, positions]
        // - ListSizes_Jersey -> [listsizesjersey, listjerseysizes, jerseysizes, sizesjersey, jersey]
        var candidates = new HashSet<string>();

        var ds = dataSource ?? string.Empty;
        var dsNorm = Normalize(ds);
        candidates.Add(dsNorm);

        // Strip common prefixes
        string StripPrefix(string s, string prefixNorm)
            => s.StartsWith(prefixNorm) ? s.Substring(prefixNorm.Length) : s;

        var dsNoList = StripPrefix(dsNorm, "list");
        candidates.Add(dsNoList);

        var dsNoListSizes = StripPrefix(dsNorm, "listsizes");
        candidates.Add(dsNoListSizes);

        // Add prefixed forms
        candidates.Add("list" + dsNoList);
        candidates.Add("listsizes" + dsNoList);

        // Handle Sizes_ reordering: ListSizes_Jersey <-> List_JerseySizes
        // Try to split on "sizes" token and swap
        int sizesIdx = dsNorm.IndexOf("sizes", StringComparison.Ordinal);
        if (sizesIdx >= 0)
        {
            var before = dsNorm.Substring(0, sizesIdx); // may include 'list'
            var after = dsNorm.Substring(sizesIdx + "sizes".Length); // e.g., _jersey (without underscore after normalize)
            // Normalize again in case we cut mid-token
            before = Normalize(before);
            after = Normalize(after);

            if (!string.IsNullOrEmpty(after))
            {
                candidates.Add(before + after + "sizes"); // listjerseysizes
                candidates.Add("list" + after + "sizes");
                candidates.Add(after + "sizes");
            }
        }

        // Direct exact, prefix, and contains search using original strings first
        var exact = jsonOptions.Keys.FirstOrDefault(k => k.Equals(ds, StringComparison.OrdinalIgnoreCase));
        if (exact != null) return exact;

        var withList = jsonOptions.Keys.FirstOrDefault(k => k.Equals($"List_{ds}", StringComparison.OrdinalIgnoreCase));
        if (withList != null) return withList;

        var contains = jsonOptions.Keys.FirstOrDefault(k => k.Contains(ds, StringComparison.OrdinalIgnoreCase));
        if (contains != null) return contains;

        // Fallback: normalized fuzzy matching (both directions)
        foreach (var key in jsonOptions.Keys)
        {
            var nk = Normalize(key);
            foreach (var cand in candidates)
            {
                if (string.IsNullOrEmpty(cand)) continue;
                if (nk.Contains(cand) || cand.Contains(nk))
                {
                    return key;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Parse JsonOptions array format into ProfileFieldOption list
    /// Input format: [{"Text":"Attack","Value":"attack"}, ...]
    /// Output format: List<ProfileFieldOption>
    /// </summary>
    private static List<ProfileFieldOption> ParseJsonOptionsArray(JsonElement jsonElement)
    {
        var options = new List<ProfileFieldOption>();

        try
        {
            foreach (var item in jsonElement.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object)
                {
                    // Try to extract Text and Value properties
                    var text = GetPropertyString(item, "Text") ?? GetPropertyString(item, "text");
                    var value = GetPropertyString(item, "Value") ?? GetPropertyString(item, "value");

                    if (!string.IsNullOrEmpty(value))
                    {
                        options.Add(new ProfileFieldOption
                        {
                            Value = value,
                            Label = text ?? value
                        });
                    }
                }
                else if (item.ValueKind == JsonValueKind.String)
                {
                    // Simple string array
                    var stringValue = item.GetString();
                    if (!string.IsNullOrEmpty(stringValue))
                    {
                        options.Add(new ProfileFieldOption
                        {
                            Value = stringValue,
                            Label = stringValue
                        });
                    }
                }
            }
        }
        catch (Exception)
        {
            // Return empty list on parse error
        }

        return options;
    }

    /// <summary>
    /// Helper to extract string property from JsonElement
    /// </summary>
    private static string? GetPropertyString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var prop) ? prop.GetString() : null;
    }
}


