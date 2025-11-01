using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using TSIC.API.Dtos;
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
        var result = await MigrateJobAsync(jobId, dryRun: true);
        return result;
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
                .Where(j => !string.IsNullOrEmpty(j.CoreRegformPlayer))
                .Select(j => new { j.JobId, j.JobName, j.CoreRegformPlayer })
                .ToListAsync();

            _logger.LogInformation("Found {Count} jobs with CoreRegformPlayer settings", jobs.Count);

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

            // Parse into metadata
            var metadata = await _parser.ParseProfileAsync(profileSource, baseSource, profileType, profileSha);

            result.FieldCount = metadata.Fields.Count;
            result.GeneratedMetadata = metadata;

            // Serialize to JSON
            var jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
            var metadataJson = JsonSerializer.Serialize(metadata, jsonOptions);

            // Update database (unless dry run)
            if (!dryRun)
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
            .Where(j => !string.IsNullOrEmpty(j.CoreRegformPlayer))
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

            // 2. Parse ONCE
            var metadata = await _parser.ParseProfileAsync(profileSource, baseSource, profileType, profileSha);
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

            if (jobs.Count == 0)
            {
                result.Success = true;
                result.Warnings.Add($"No jobs found using profile type {profileType}");
                return result;
            }

            // 4. Serialize metadata JSON ONCE
            var jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
            var metadataJson = JsonSerializer.Serialize(metadata, jsonOptions);

            // 5. Apply to ALL jobs (if not dry run)
            if (!dryRun)
            {
                foreach (var job in jobs)
                {
                    job.PlayerProfileMetadataJson = metadataJson;
                }

                await _context.SaveChangesAsync();

                _logger.LogInformation(
                    "Applied {ProfileType} metadata ({FieldCount} fields) to {JobCount} jobs",
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
            .Where(j => j.CoreRegformPlayer != null &&
                       (j.CoreRegformPlayer.StartsWith(profileType + "|") ||
                        j.CoreRegformPlayer == profileType) &&
                       !string.IsNullOrEmpty(j.PlayerProfileMetadataJson))
            .Select(j => j.PlayerProfileMetadataJson)
            .FirstOrDefaultAsync();

        if (string.IsNullOrEmpty(job))
        {
            return null;
        }

        var metadata = JsonSerializer.Deserialize<ProfileMetadata>(job,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        return metadata;
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
    /// Clone an existing profile with auto-incremented name
    /// Finds the max version of the profile base name and creates new profile as +1
    /// Example: PlayerProfile -> PlayerProfile2, CoachProfile3 -> CoachProfile4
    /// </summary>
    public async Task<CloneProfileResult> CloneProfileAsync(string sourceProfileType)
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

            // Determine base name and find max version
            var newProfileType = await GenerateNewProfileNameAsync(sourceProfileType);

            // Create metadata for new profile (clone from source)
            var newMetadata = CloneMetadata(sourceMetadata);

            // Log the creation - actual job assignment happens in the editor
            _logger.LogInformation("Created new profile type: {NewProfileType} from {SourceProfileType}",
                newProfileType, sourceProfileType);

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
    /// Generate new profile name by finding max version and incrementing
    /// </summary>
    private async Task<string> GenerateNewProfileNameAsync(string sourceProfileType)
    {
        // Extract base name (remove trailing numbers if any)
        var baseName = System.Text.RegularExpressions.Regex.Replace(
            sourceProfileType,
            @"\d+$",
            string.Empty
        );

        // Find all profiles with same base name
        var existingProfiles = await _context.Jobs
            .Select(j => j.CoreRegformPlayer)
            .Distinct()
            .Where(p => p != null && p.StartsWith(baseName))
            .ToListAsync();

        // Extract version numbers
        var maxVersion = 1;
        foreach (var profile in existingProfiles)
        {
            var match = System.Text.RegularExpressions.Regex.Match(profile!, $@"^{baseName}(\d+)$");
            if (match.Success && int.TryParse(match.Groups[1].Value, out var version))
            {
                maxVersion = Math.Max(maxVersion, version);
            }
        }

        // If source has no number, check if base name exists
        if (!System.Text.RegularExpressions.Regex.IsMatch(sourceProfileType, @"\d+$"))
        {
            // Source is like "PlayerProfile", new should be "PlayerProfile2"
            return $"{baseName}{maxVersion + 1}";
        }
        else
        {
            // Source is like "PlayerProfile2", new should be "PlayerProfile3"
            return $"{baseName}{maxVersion + 1}";
        }
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
}


