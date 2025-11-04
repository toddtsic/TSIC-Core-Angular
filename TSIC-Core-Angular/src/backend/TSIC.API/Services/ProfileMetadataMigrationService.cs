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
    private const string TokenList = "list";
    private const string TokenListSizes = "listsizes";
    private const string TokenSizes = "sizes";

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
    /// Public facade to compute the next profile type name for a given source family (PP/CAC)
    /// </summary>
    public async Task<string> GetNextProfileTypeAsync(string sourceProfileType)
        => await ComputeNextProfileTypeAsync(sourceProfileType);

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

            // Normalize: any hidden field must use inputType = HIDDEN
            metadata.Fields
                .Where(f => string.Equals(f.Visibility, "hidden", StringComparison.OrdinalIgnoreCase))
                .ToList()
                .ForEach(f => f.InputType = "HIDDEN");

            // Renumber and reorder by visibility groups to match UI grouping (Hidden -> Public -> Admin Only)
            var publics = metadata.Fields.Where(f => string.Equals(f.Visibility, "public", StringComparison.OrdinalIgnoreCase))
                                         .OrderBy(f => f.Order)
                                         .ToList();
            var admins = metadata.Fields.Where(f => string.Equals(f.Visibility, "adminOnly", StringComparison.OrdinalIgnoreCase))
                                         .OrderBy(f => f.Order)
                                         .ToList();
            var hiddens = metadata.Fields.Where(f => string.Equals(f.Visibility, "hidden", StringComparison.OrdinalIgnoreCase))
                                         .OrderBy(f => f.Order)
                                         .ToList();

            var counter = 1;
            foreach (var f in hiddens) f.Order = counter++;
            foreach (var f in publics) f.Order = counter++;
            foreach (var f in admins) f.Order = counter++;

            metadata.Fields = hiddens.Concat(publics).Concat(admins).ToList();

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
    /// Return all known profile types (PP/CAC) observed in the environment.
    /// Source of truth: Jobs.CoreRegformPlayer across all jobs (excluding markers and 0/1),
    /// optionally including jobs that already have PlayerProfileMetadataJson.
    /// This avoids any dependency on GitHub and lists all types in use, migrated or not.
    /// </summary>
    public async Task<List<string>> GetKnownProfileTypesAsync()
    {
        // Pull the minimal columns needed and process in-memory to reuse ExtractProfileType logic
        var rows = await _context.Jobs
            .AsNoTracking()
            .Where(j => j.CoreRegformPlayer != null
                        && j.CoreRegformPlayer != "0"
                        && j.CoreRegformPlayer != "1"
                        && !j.CoreRegformPlayer!.Contains(CoreRegformExcludeMarker))
            .Select(j => new { j.CoreRegformPlayer, j.PlayerProfileMetadataJson })
            .ToListAsync();

        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            var t = ExtractProfileType(row.CoreRegformPlayer);
            if (!string.IsNullOrEmpty(t) && !ExcludedProfileTypes.Contains(t))
            {
                set.Add(t!);
            }
        }

        return set.OrderBy(t => t).ToList();
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

            // Normalize: any hidden field must use inputType = HIDDEN
            metadata.Fields
                .Where(f => string.Equals(f.Visibility, "hidden", StringComparison.OrdinalIgnoreCase))
                .ToList()
                .ForEach(f => f.InputType = "HIDDEN");

            // Renumber and reorder by visibility groups to match UI grouping (Hidden -> Public -> Admin Only)
            var publics = metadata.Fields.Where(f => string.Equals(f.Visibility, "public", StringComparison.OrdinalIgnoreCase))
                                         .OrderBy(f => f.Order)
                                         .ToList();
            var admins = metadata.Fields.Where(f => string.Equals(f.Visibility, "adminOnly", StringComparison.OrdinalIgnoreCase))
                                         .OrderBy(f => f.Order)
                                         .ToList();
            var hiddens = metadata.Fields.Where(f => string.Equals(f.Visibility, "hidden", StringComparison.OrdinalIgnoreCase))
                                         .OrderBy(f => f.Order)
                                         .ToList();

            var counter = 1;
            foreach (var f in hiddens) f.Order = counter++;
            foreach (var f in publics) f.Order = counter++;
            foreach (var f in admins) f.Order = counter++;

            metadata.Fields = hiddens.Concat(publics).Concat(admins).ToList();

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
    /// Get the current job's profile metadata using the registration id (regId claim)
    /// Returns both the resolved profileType (e.g., PP10, CAC05) and the metadata
    /// </summary>
    public async Task<(string? ProfileType, ProfileMetadata? Metadata)> GetCurrentJobProfileMetadataAsync(Guid regId)
    {
        // Load the registration and its job
        var registration = await _context.Registrations
            .Include(r => r.Job)
            .FirstOrDefaultAsync(r => r.RegistrationId == regId);

        if (registration?.Job == null)
        {
            return (null, null);
        }

        var profileType = ExtractProfileType(registration.Job.CoreRegformPlayer);
        if (string.IsNullOrEmpty(profileType))
        {
            return (null, null);
        }

        var metadata = await GetProfileMetadataAsync(profileType);
        return (profileType, metadata);
    }

    // ============================================================================
    // CURRENT JOB OPTION SETS (Jobs.JsonOptions) — helpers
    // ============================================================================

    public async Task<List<OptionSet>> GetCurrentJobOptionSetsAsync(Guid regId)
    {
        var registration = await _context.Registrations
            .Include(r => r.Job)
            .FirstOrDefaultAsync(r => r.RegistrationId == regId);

        var optionSets = new List<OptionSet>();
        if (registration?.Job == null || string.IsNullOrWhiteSpace(registration.Job.JsonOptions))
        {
            return optionSets; // empty
        }

        try
        {
            var json = registration.Job.JsonOptions!;
            var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json, s_CaseInsensitive);
            if (dict == null) return optionSets;

            foreach (var kvp in dict)
            {
                if (kvp.Value.ValueKind == JsonValueKind.Array)
                {
                    var values = ParseJsonOptionsArray(kvp.Value);
                    optionSets.Add(new OptionSet
                    {
                        Key = kvp.Key,
                        Provider = "Jobs.JsonOptions",
                        ReadOnly = false,
                        Values = values
                    });
                }
            }
        }
        catch (Exception ex)
        {
            // Log and return empty on parse errors; not fatal for listing option sets
            _logger.LogDebug(ex, "Failed to parse Jobs.JsonOptions for regId {RegId}", regId);
        }

        return optionSets;
    }

    public async Task<OptionSet?> UpsertCurrentJobOptionSetAsync(Guid regId, string key, List<ProfileFieldOption> values)
    {
        var registration = await _context.Registrations
            .Include(r => r.Job)
            .FirstOrDefaultAsync(r => r.RegistrationId == regId);

        if (registration?.Job == null)
            return null;

        Dictionary<string, JsonElement>? dict = null;
        if (!string.IsNullOrWhiteSpace(registration.Job.JsonOptions))
        {
            try
            {
                dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(registration.Job.JsonOptions, s_CaseInsensitive);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to parse existing Jobs.JsonOptions for upsert (regId {RegId})", regId);
            }
        }
        dict ??= new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);

        // Convert values to JsonElement array
        var json = JsonSerializer.Serialize(values, s_IndentedCamelCase);
        using var doc = JsonDocument.Parse(json);
        dict[key] = doc.RootElement.Clone();

        registration.Job.JsonOptions = JsonSerializer.Serialize(dict, s_IndentedCamelCase);
        await _context.SaveChangesAsync();

        return new OptionSet
        {
            Key = key,
            Provider = "Jobs.JsonOptions",
            ReadOnly = false,
            Values = values
        };
    }

    /// <summary>
    /// Build a distinct domain list of all profile fields found across Jobs.PlayerProfileMetadataJson.
    /// For each field name, selects the most frequently observed input type and visibility, and a representative display name.
    /// Intended as a one-time or occasional generator to seed a static allowed-fields list in the UI.
    /// </summary>
    public async Task<List<AllowedFieldDomainItem>> BuildAllowedFieldDomainAsync()
    {
        var results = new Dictionary<string, (Dictionary<string, int> inputTypes, Dictionary<string, int> visibilities, Dictionary<string, int> displayNames, int total)>(StringComparer.OrdinalIgnoreCase);

        // Load all metadata jsons present
        var jsonList = await _context.Jobs
            .AsNoTracking()
            .Where(j => !string.IsNullOrEmpty(j.PlayerProfileMetadataJson))
            .Select(j => j.PlayerProfileMetadataJson!)
            .ToListAsync();

        foreach (var json in jsonList)
        {
            try
            {
                var metadata = JsonSerializer.Deserialize<ProfileMetadata>(json, s_CaseInsensitive);
                if (metadata?.Fields == null) continue;

                foreach (var f in metadata.Fields)
                {
                    if (string.IsNullOrWhiteSpace(f.Name)) continue;

                    if (!results.TryGetValue(f.Name, out var agg))
                    {
                        agg = (new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
                               new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
                               new Dictionary<string, int>(StringComparer.Ordinal),
                               0);
                        results[f.Name] = agg;
                    }

                    void Inc(Dictionary<string, int> d, string key)
                    {
                        if (string.IsNullOrWhiteSpace(key)) return;
                        d[key] = d.TryGetValue(key, out var n) ? n + 1 : 1;
                    }

                    Inc(agg.inputTypes, f.InputType ?? string.Empty);
                    Inc(agg.visibilities, f.Visibility ?? string.Empty);
                    Inc(agg.displayNames, f.DisplayName ?? string.Empty);

                    // bump total sightings for this field name
                    var tmp = results[f.Name];
                    tmp.total++;
                    results[f.Name] = tmp;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to parse PlayerProfileMetadataJson while building field domain");
            }
        }

        static string MostFrequent(Dictionary<string, int> d, string fallback)
        {
            if (d.Count == 0) return fallback;
            return d.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key).First().Key;
        }

        var list = new List<AllowedFieldDomainItem>();
        foreach (var (name, agg) in results)
        {
            var item = new AllowedFieldDomainItem
            {
                Name = name,
                DisplayName = MostFrequent(agg.displayNames, name),
                DefaultInputType = MostFrequent(agg.inputTypes, "TEXT"),
                DefaultVisibility = MostFrequent(agg.visibilities, "public"),
                SeenInProfiles = agg.total
            };
            list.Add(item);
        }

        // Order by visibility then name for readability
        list = list
            .OrderBy(i => i.DefaultVisibility.Equals("hidden", StringComparison.OrdinalIgnoreCase) ? 0 : i.DefaultVisibility.Equals("public", StringComparison.OrdinalIgnoreCase) ? 1 : 2)
            .ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return list;
    }

    public async Task<bool> DeleteCurrentJobOptionSetAsync(Guid regId, string key)
    {
        var registration = await _context.Registrations
            .Include(r => r.Job)
            .FirstOrDefaultAsync(r => r.RegistrationId == regId);

        if (registration?.Job == null)
            return false;

        Dictionary<string, JsonElement>? dict = null;
        if (!string.IsNullOrWhiteSpace(registration.Job.JsonOptions))
        {
            try
            {
                dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(registration.Job.JsonOptions, s_CaseInsensitive);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to parse Jobs.JsonOptions for delete (regId {RegId})", regId);
            }
        }

        if (dict == null || !dict.Remove(key))
            return false;

        registration.Job.JsonOptions = JsonSerializer.Serialize(dict, s_IndentedCamelCase);
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> RenameCurrentJobOptionSetAsync(Guid regId, string oldKey, string newKey)
    {
        var registration = await _context.Registrations
            .Include(r => r.Job)
            .FirstOrDefaultAsync(r => r.RegistrationId == regId);

        if (registration?.Job == null)
            return false;

        Dictionary<string, JsonElement>? dict = null;
        if (!string.IsNullOrWhiteSpace(registration.Job.JsonOptions))
        {
            try
            {
                dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(registration.Job.JsonOptions, s_CaseInsensitive);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to parse Jobs.JsonOptions for rename (regId {RegId})", regId);
            }
        }
        if (dict == null || !dict.ContainsKey(oldKey))
            return false;

        var element = dict[oldKey];
        dict.Remove(oldKey);
        dict[newKey] = element;

        registration.Job.JsonOptions = JsonSerializer.Serialize(dict, s_IndentedCamelCase);
        await _context.SaveChangesAsync();
        return true;
    }

    /// <summary>
    /// Read-only sources for option values derived from Registrations columns for the current job.
    /// Keys are aligned to metadata field.DataSource when available; otherwise fallback to List_{FieldName}.
    /// </summary>
    public async Task<List<OptionSet>> GetCurrentJobOptionSourcesAsync(Guid regId)
    {
        var (profileType, metadata) = await GetCurrentJobProfileMetadataAsync(regId);
        var results = new List<OptionSet>();
        if (string.IsNullOrEmpty(profileType) || metadata == null)
        {
            return results;
        }

        // Get the jobId from registration
        var registration = await _context.Registrations
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.RegistrationId == regId);
        if (registration == null)
        {
            return results;
        }

        var jobId = registration.JobId;

        // Gather candidate fields: SELECT types with a dbColumn to map to Registrations
        var selectFields = metadata.Fields
            .Where(f => !string.IsNullOrWhiteSpace(f.DbColumn)
                        && !string.IsNullOrWhiteSpace(f.Name)
                        && string.Equals(f.InputType, "SELECT", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var field in selectFields)
        {
            var key = !string.IsNullOrWhiteSpace(field.DataSource) ? field.DataSource! : ($"List_{field.Name}");
            if (!seen.Add(key))
                continue; // avoid duplicate keys

            var column = field.DbColumn!;
            try
            {
                // Dynamically select the registration property using EF.Property
                var values = await _context.Registrations
                    .Where(r => r.JobId == jobId)
                    .Select(r => EF.Property<string>(r, column))
                    .Distinct()
                    .ToListAsync();

                var options = values
                    .Where(v => !string.IsNullOrWhiteSpace(v))
                    .Select(v => v!.Trim())
                    .Where(v => v.Length > 0)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(v => v)
                    .Select(v => new ProfileFieldOption { Value = v, Label = v })
                    .ToList();

                // Only include if we have some options
                if (options.Count > 0)
                {
                    results.Add(new OptionSet
                    {
                        Key = key,
                        Provider = "Registrations",
                        ReadOnly = true,
                        Values = options
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to read option source from Registrations column {Column} for job {JobId}", column, jobId);
            }
        }

        return results;
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

            // Normalize: ensure hidden fields use inputType = HIDDEN
            metadata.Fields
                .Where(f => string.Equals(f.Visibility, "hidden", StringComparison.OrdinalIgnoreCase))
                .ToList()
                .ForEach(f => f.InputType = "HIDDEN");

            // Renumber orders consistently by visibility groups: Public -> AdminOnly -> Hidden
            var publics = metadata.Fields.Where(f => string.Equals(f.Visibility, "public", StringComparison.OrdinalIgnoreCase))
                                         .OrderBy(f => f.Order)
                                         .ToList();
            var admins = metadata.Fields.Where(f => string.Equals(f.Visibility, "adminOnly", StringComparison.OrdinalIgnoreCase))
                                         .OrderBy(f => f.Order)
                                         .ToList();
            var hiddens = metadata.Fields.Where(f => string.Equals(f.Visibility, "hidden", StringComparison.OrdinalIgnoreCase))
                                         .OrderBy(f => f.Order)
                                         .ToList();

            var counter = 1;
            foreach (var f in hiddens) f.Order = counter++;
            foreach (var f in publics) f.Order = counter++;
            foreach (var f in admins) f.Order = counter++;

            metadata.Fields = hiddens.Concat(publics).Concat(admins).ToList();

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

            // Determine the next profile id for the family (PP or CAC) based on global max across Jobs
            var newProfileType = await ComputeNextProfileTypeAsync(sourceProfileType);

            // Create metadata for new profile (clone from source)
            var newMetadata = CloneMetadata(sourceMetadata);
            var metadataJson = JsonSerializer.Serialize(newMetadata);

            // Update the current job's CoreRegformPlayer preserving pipe-delimited structure
            job.CoreRegformPlayer = UpdateCoreRegformPlayer(job.CoreRegformPlayer, newProfileType);
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
    /// Compute next profile type for the family prefix (PP or CAC) using the current max across Jobs.CoreRegformPlayer.
    /// Falls back to source+1 if none found.
    /// </summary>
    private async Task<string> ComputeNextProfileTypeAsync(string sourceProfileType)
    {
        var prefix = GetProfilePrefix(sourceProfileType);
        var sourceNum = ExtractTrailingNumber(sourceProfileType) ?? 0;

        // Gather all CoreRegformPlayer values (ignore null/0/1 sentinel values)
        var all = await _context.Jobs
            .Where(j => j.CoreRegformPlayer != null && j.CoreRegformPlayer != "0" && j.CoreRegformPlayer != "1")
            .Select(j => j.CoreRegformPlayer!)
            .ToListAsync();

        int maxNum = 0;
        foreach (var val in all)
        {
            foreach (var part in SplitCoreRegform(val))
            {
                if (part.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    var n = ExtractTrailingNumber(part);
                    if (n.HasValue && n.Value > maxNum)
                        maxNum = n.Value;
                }
            }
        }

        var next = (maxNum > 0 ? maxNum : sourceNum) + 1;
        return $"{prefix}{next}";
    }

    private static string GetProfilePrefix(string profileType)
        => profileType.StartsWith("CAC", StringComparison.OrdinalIgnoreCase) ? "CAC" : "PP";

    private static int? ExtractTrailingNumber(string value)
    {
        var m = System.Text.RegularExpressions.Regex.Match(value ?? string.Empty, @"(\d+)$");
        return m.Success && int.TryParse(m.Groups[1].Value, out var n) ? n : (int?)null;
    }

    private static IEnumerable<string> SplitCoreRegform(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) yield break;
        foreach (var part in value.Split('|'))
        {
            var p = part?.Trim();
            if (!string.IsNullOrEmpty(p)) yield return p;
        }
    }

    /// <summary>
    /// Update CoreRegformPlayer string preserving other parts; replace the PP/CAC segment with the provided new type.
    /// If no PP/CAC segment exists, replace the whole value with new type.
    /// </summary>
    private static string UpdateCoreRegformPlayer(string? existing, string newProfileType)
    {
        var prefix = GetProfilePrefix(newProfileType);
        if (string.IsNullOrWhiteSpace(existing) || existing == "0" || existing == "1")
            return newProfileType;

        var parts = existing.Split('|');
        bool replaced = false;
        for (int i = 0; i < parts.Length; i++)
        {
            var p = parts[i]?.Trim() ?? string.Empty;
            if (p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                parts[i] = newProfileType;
                replaced = true;
            }
        }
        if (!replaced)
        {
            // No matching segment found. If it's a single-part value, replace; otherwise append as best-effort.
            if (parts.Length <= 1)
                return newProfileType;
            return string.Join('|', parts.Append(newProfileType));
        }
        return string.Join('|', parts);
    }

    // ----------------------------------------------------------------------------
    // CoreRegformPlayer helpers (compose/decompose)
    // ----------------------------------------------------------------------------
    private static (string? ProfileType, string? TeamConstraint, bool AllowPayInFull) ParseCoreRegformParts(string? coreRegformPlayer)
    {
        if (string.IsNullOrWhiteSpace(coreRegformPlayer) || coreRegformPlayer == "0" || coreRegformPlayer == "1")
            return (null, null, false);

        var parts = coreRegformPlayer.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var profileType = parts.Length >= 1 ? parts[0] : null;
        var teamConstraint = parts.Length >= 2 ? parts[1] : null;
        var allowPif = parts.Any(p => p.Equals("ALLOWPIF", StringComparison.OrdinalIgnoreCase));
        return (profileType, teamConstraint, allowPif);
    }

    private static string BuildCoreRegform(string profileType, string teamConstraint, bool allowPayInFull)
    {
        var list = new List<string> { profileType, teamConstraint };
        if (allowPayInFull) list.Add("ALLOWPIF");
        return string.Join('|', list);
    }

    public async Task<(string? ProfileType, string? TeamConstraint, bool AllowPayInFull, string Raw, ProfileMetadata? Metadata)> GetCurrentJobProfileConfigAsync(Guid regId)
    {
        var registration = await _context.Registrations
            .Include(r => r.Job)
            .FirstOrDefaultAsync(r => r.RegistrationId == regId);
        if (registration?.Job == null)
        {
            return (null, null, false, string.Empty, null);
        }

        var raw = registration.Job.CoreRegformPlayer ?? string.Empty;
        var (pt, constraint, allowPif) = ParseCoreRegformParts(raw);
        ProfileMetadata? metadata = null;
        if (!string.IsNullOrEmpty(pt))
        {
            metadata = await GetProfileMetadataAsync(pt);
        }
        return (pt, constraint, allowPif, raw, metadata);
    }

    public async Task<(string ProfileType, string TeamConstraint, bool AllowPayInFull, string Raw, ProfileMetadata? Metadata)>
        UpdateCurrentJobProfileConfigAsync(Guid regId, string profileType, string teamConstraint, bool allowPayInFull)
    {
        var registration = await _context.Registrations
            .Include(r => r.Job)
            .FirstOrDefaultAsync(r => r.RegistrationId == regId);
        if (registration?.Job == null)
        {
            throw new InvalidOperationException("Current job not found for supplied regId");
        }

        // Build and persist CoreRegformPlayer
        var newCore = BuildCoreRegform(profileType, teamConstraint, allowPayInFull);
        registration.Job.CoreRegformPlayer = newCore;

        // Refresh PlayerProfileMetadataJson to match the selected type
        var metadata = await GetProfileMetadataAsync(profileType);
        if (metadata != null)
        {
            // Serialize using default options (consistent with other paths)
            var metadataJson = JsonSerializer.Serialize(metadata);
            registration.Job.PlayerProfileMetadataJson = metadataJson;
        }

        await _context.SaveChangesAsync();

        return (profileType, teamConstraint, allowPayInFull, newCore, metadata);
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

        var dsNoList = StripPrefix(dsNorm, TokenList);
        candidates.Add(dsNoList);

        var dsNoListSizes = StripPrefix(dsNorm, TokenListSizes);
        candidates.Add(dsNoListSizes);

        // Add prefixed forms
        candidates.Add(TokenList + dsNoList);
        candidates.Add(TokenListSizes + dsNoList);

        // Handle Sizes_ reordering: ListSizes_Jersey <-> List_JerseySizes
        // Try to split on "sizes" token and swap
        int sizesIdx = dsNorm.IndexOf(TokenSizes, StringComparison.Ordinal);
        if (sizesIdx >= 0)
        {
            var before = dsNorm.Substring(0, sizesIdx); // may include 'list'
            var after = dsNorm.Substring(sizesIdx + TokenSizes.Length); // e.g., _jersey (without underscore after normalize)
            // Normalize again in case we cut mid-token
            before = Normalize(before);
            after = Normalize(after);

            if (!string.IsNullOrEmpty(after))
            {
                candidates.Add(before + after + TokenSizes); // listjerseysizes
                candidates.Add(TokenList + after + TokenSizes);
                candidates.Add(after + TokenSizes);
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


