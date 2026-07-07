using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text.Json.Nodes;
using TSIC.API.Services.Adults;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Repositories;
using TSIC.Domain.Entities;

namespace TSIC.API.Services.Metadata;

/// <summary>
/// Orchestrates migration of player profile metadata from GitHub POCOs to Jobs.PlayerProfileMetadataJson
/// </summary>
public class ProfileMetadataMigrationService : IProfileMetadataMigrationService
{
    private const string UnknownValue = "Unknown";
    // Common tokens and literals centralized to satisfy analyzers and improve readability
    private const string VisibilityHidden = "hidden";
    private const string VisibilityPublic = "public";
    private const string VisibilityAdminOnly = "adminOnly";
    private const string InputTypeHidden = "HIDDEN";
    private const string InputTypeSelect = "SELECT";
    private const string InputTypeNumber = "NUMBER";
    private const string InputTypeEmail = "EMAIL";
    private const string TokenList = "list";
    private const string TokenListSizes = "listsizes";
    private const string TokenSizes = "sizes";

    private readonly IProfileMetadataRepository _repo;
    private readonly IGitHubProfileFetcher _githubFetcher;
    private readonly CSharpToMetadataParser _parser;
    private readonly ILogger<ProfileMetadataMigrationService> _logger;

    // Profiles to be hidden from summaries and batch operations derived from summaries
    // Case-insensitive to avoid casing mismatches from upstream data
    private static readonly HashSet<string> ExcludedProfileTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "PP1_Player_Regform"
    };

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
        IProfileMetadataRepository repo,
        IGitHubProfileFetcher githubFetcher,
        CSharpToMetadataParser parser,
        ILogger<ProfileMetadataMigrationService> logger)
    {
        _repo = repo;
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
        var startTime = DateTime.UtcNow;
        var results = new List<MigrationResult>();
        var globalWarnings = new List<string>();
        int successCount = 0;
        int failureCount = 0;
        int warningCount = 0;
        int skippedCount = 0;

        try
        {
            // Get all jobs with a CoreRegformPlayer setting
            var jobs = await _repo.GetJobsWithCoreRegformPlayerAsync();

            foreach (var job in jobs)
            {
                try
                {
                    // Extract profile type from CoreRegformPlayer (e.g., "PP10|BYGRADYEAR|ALLOWPIF" -> "PP10")
                    var profileType = ExtractProfileType(job.CoreRegformPlayer);

                    if (string.IsNullOrEmpty(profileType))
                    {
                        skippedCount++;
                        globalWarnings.Add($"Job {job.JobName}: Could not extract profile type from '{job.CoreRegformPlayer}'");
                        continue;
                    }

                    // Apply filter if specified
                    if (profileTypeFilter != null && profileTypeFilter.Count > 0 && !profileTypeFilter.Contains(profileType))
                    {
                        skippedCount++;
                        continue;
                    }

                    var result = await MigrateJobAsync(job.JobId, dryRun);
                    results.Add(result);

                    if (result.Success)
                        successCount++;
                    else
                        failureCount++;

                    if (result.Warnings.Count > 0)
                        warningCount += result.Warnings.Count;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error migrating job {JobId}", job.JobId);
                    results.Add(new MigrationResult
                    {
                        JobId = job.JobId,
                        JobName = job.JobName ?? UnknownValue,
                        ProfileType = UnknownValue,
                        Success = false,
                        ErrorMessage = ex.Message,
                        Warnings = new(),
                        FieldCount = 0,
                        GeneratedMetadata = null
                    });
                    failureCount++;
                }
            }

            var completedAt = DateTime.UtcNow;

            _logger.LogInformation(
                "Migration {Mode} completed: {Success} succeeded, {Failed} failed, {Skipped} skipped, {Warnings} warnings",
                dryRun ? "preview" : "execution",
                successCount,
                failureCount,
                skippedCount,
                warningCount);

            return new MigrationReport
            {
                StartedAt = startTime,
                CompletedAt = completedAt,
                SuccessCount = successCount,
                FailureCount = failureCount,
                WarningCount = warningCount,
                SkippedCount = skippedCount,
                Results = results,
                GlobalWarnings = globalWarnings
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Migration failed");
            globalWarnings.Add($"Migration failed: {ex.Message}");

            return new MigrationReport
            {
                StartedAt = startTime,
                CompletedAt = DateTime.UtcNow,
                SuccessCount = successCount,
                FailureCount = failureCount,
                WarningCount = warningCount,
                SkippedCount = skippedCount,
                Results = results,
                GlobalWarnings = globalWarnings
            };
        }
    }

    /// <summary>
    /// Migrate a single job
    /// </summary>
    private async Task<MigrationResult> MigrateJobAsync(Guid jobId, bool dryRun)
    {
        var job = await _repo.GetJobBasicInfoAsync(jobId);

        if (job == null)
        {
            return new MigrationResult
            {
                JobId = jobId,
                JobName = UnknownValue,
                ProfileType = UnknownValue,
                Success = false,
                ErrorMessage = "Job not found",
                Warnings = new List<string>(),
                FieldCount = 0,
                GeneratedMetadata = null
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
                ErrorMessage = $"Could not extract profile type from '{job.CoreRegformPlayer}'",
                Warnings = new List<string>(),
                FieldCount = 0,
                GeneratedMetadata = null
            };
        }

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
            ApplyCanonicalFieldCorrections(metadata);

            // Normalize: any hidden field must use inputType = HIDDEN
            metadata.Fields
                .Where(f => string.Equals(f.Visibility, VisibilityHidden, StringComparison.OrdinalIgnoreCase))
                .ToList()
                .ForEach(f => f.InputType = InputTypeHidden);

            // Renumber and reorder by visibility groups to match UI grouping (Hidden -> Public -> Admin Only)
            var publics = metadata.Fields.Where(f => string.Equals(f.Visibility, VisibilityPublic, StringComparison.OrdinalIgnoreCase))
                                         .OrderBy(f => f.Order)
                                         .ToList();
            var admins = metadata.Fields.Where(f => string.Equals(f.Visibility, VisibilityAdminOnly, StringComparison.OrdinalIgnoreCase))
                                         .OrderBy(f => f.Order)
                                         .ToList();
            var hiddens = metadata.Fields.Where(f => string.Equals(f.Visibility, VisibilityHidden, StringComparison.OrdinalIgnoreCase))
                                         .OrderBy(f => f.Order)
                                         .ToList();

            var counter = 1;
            foreach (var f in hiddens) f.Order = counter++;
            foreach (var f in publics) f.Order = counter++;
            foreach (var f in admins) f.Order = counter++;

            metadata.Fields = hiddens.Concat(publics).Concat(admins).ToList();

            // Serialize to JSON
            var metadataJson = JsonSerializer.Serialize(metadata, s_IndentedCamelCase);

            if (!dryRun)
            {
                await _repo.UpdateJobPlayerMetadataAsync(jobId, metadataJson);

                _logger.LogInformation(
                    "Updated PlayerProfileMetadataJson for job {JobName} with {FieldCount} fields",
                    job.JobName, metadata.Fields.Count);
            }

            // Collect warnings
            var warnings = new List<string>();
            if (metadata.Fields.Exists(f => string.IsNullOrEmpty(f.DataSource) && string.Equals(f.InputType, InputTypeSelect, StringComparison.OrdinalIgnoreCase)))
            {
                warnings.Add("Some SELECT fields are missing dataSource mapping");
            }

            return new MigrationResult
            {
                JobId = jobId,
                JobName = job.JobName ?? UnknownValue,
                ProfileType = profileType,
                Success = true,
                ErrorMessage = null,
                Warnings = warnings,
                FieldCount = metadata.Fields.Count,
                GeneratedMetadata = metadata
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to migrate job {JobId}", jobId);
            return new MigrationResult
            {
                JobId = jobId,
                JobName = job.JobName ?? UnknownValue,
                ProfileType = profileType,
                Success = false,
                ErrorMessage = ex.Message,
                Warnings = new(),
                FieldCount = 0,
                GeneratedMetadata = null
            };
        }
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
        var jobs = await _repo.GetJobsForProfileSummaryAsync();

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
        var rows = await _repo.GetJobsForKnownProfileTypesAsync();

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
    /// Generate SQL script to apply profile migrations to production database
    /// </summary>
    public async Task<string> GenerateMigrationSqlScriptAsync()
    {
        var jobs = await _repo.GetJobsWithProfileMetadataAsync();

        var jobsWithMetadata = jobs.Where(j => !string.IsNullOrEmpty(j.PlayerProfileMetadataJson)).ToList();

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("-- =====================================================");
        sb.AppendLine("-- Profile Migration SQL Export");
        sb.AppendLine($"-- Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine($"-- Jobs with metadata: {jobsWithMetadata.Count}");
        sb.AppendLine("-- Idempotent: safe to run multiple times");
        sb.AppendLine("-- Only touches: [Jobs].[Jobs].[PlayerProfileMetadataJson]");
        sb.AppendLine("-- =====================================================");
        sb.AppendLine();
        sb.AppendLine("SET NOCOUNT ON;");
        sb.AppendLine("SET XACT_ABORT ON;");
        sb.AppendLine("BEGIN TRANSACTION;");
        sb.AppendLine();

        foreach (var job in jobsWithMetadata)
        {
            var escapedJson = job.PlayerProfileMetadataJson!.Replace("'", "''");
            sb.AppendLine($"-- {job.JobName ?? "Unnamed"} (Job ID: {job.JobId})");
            sb.AppendLine("UPDATE [Jobs].[Jobs]");
            sb.AppendLine($"SET [PlayerProfileMetadataJson] = '{escapedJson}'");
            sb.AppendLine($"WHERE [jobID] = '{job.JobId}';");
            sb.AppendLine();
        }

        sb.AppendLine("COMMIT TRANSACTION;");
        sb.AppendLine();
        sb.AppendLine($"PRINT 'Profile migration complete: {jobsWithMetadata.Count} jobs updated';");
        sb.AppendLine();
        sb.AppendLine("-- Verify results:");
        sb.AppendLine("SELECT COUNT(*) AS [Jobs With Metadata] FROM [Jobs].[Jobs] WHERE [PlayerProfileMetadataJson] IS NOT NULL;");

        return sb.ToString();
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
            ApplyCanonicalFieldCorrections(metadata);

            // Normalize: any hidden field must use inputType = HIDDEN
            metadata.Fields
                .Where(f => string.Equals(f.Visibility, VisibilityHidden, StringComparison.OrdinalIgnoreCase))
                .ToList()
                .ForEach(f => f.InputType = InputTypeHidden);

            // Renumber and reorder by visibility groups to match UI grouping (Hidden -> Public -> Admin Only)
            var publics = metadata.Fields.Where(f => string.Equals(f.Visibility, VisibilityPublic, StringComparison.OrdinalIgnoreCase))
                                         .OrderBy(f => f.Order)
                                         .ToList();
            var admins = metadata.Fields.Where(f => string.Equals(f.Visibility, VisibilityAdminOnly, StringComparison.OrdinalIgnoreCase))
                                         .OrderBy(f => f.Order)
                                         .ToList();
            var hiddens = metadata.Fields.Where(f => string.Equals(f.Visibility, VisibilityHidden, StringComparison.OrdinalIgnoreCase))
                                         .OrderBy(f => f.Order)
                                         .ToList();

            var counter = 1;
            foreach (var f in hiddens) f.Order = counter++;
            foreach (var f in publics) f.Order = counter++;
            foreach (var f in admins) f.Order = counter++;

            metadata.Fields = hiddens.Concat(publics).Concat(admins).ToList();

            // 3. Find ALL jobs using this profile
            var jobs = await _repo.GetJobsByProfileTypeAsync(profileType);

            // Build warnings list
            var warnings = new List<string>();
            if (jobs.Count == 0)
            {
                warnings.Add($"No jobs found using profile type {profileType}");
                return new ProfileMigrationResult
                {
                    ProfileType = profileType,
                    Success = true,
                    FieldCount = metadata.Fields.Count,
                    JobsAffected = 0,
                    AffectedJobIds = new List<Guid>(),
                    AffectedJobNames = new List<string>(),
                    AffectedJobYears = new List<string>(),
                    GeneratedMetadata = metadata,
                    Warnings = warnings,
                    ErrorMessage = null
                };
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

                await _repo.UpdateMultipleJobsPlayerMetadataAsync(jobs);

                _logger.LogInformation(
                    "Applied {ProfileType} metadata ({FieldCount} fields) to {JobCount} jobs with job-specific dropdown options",
                    profileType, metadata.Fields.Count, jobs.Count);
            }

            // Add warnings if any
            if (metadata.Fields.Exists(f => string.IsNullOrEmpty(f.DataSource) && string.Equals(f.InputType, InputTypeSelect, StringComparison.OrdinalIgnoreCase)))
            {
                warnings.Add("Some SELECT fields are missing dataSource mapping");
            }

            return new ProfileMigrationResult
            {
                ProfileType = profileType,
                Success = true,
                FieldCount = metadata.Fields.Count,
                JobsAffected = jobs.Count,
                AffectedJobIds = jobs.Select(j => j.JobId).ToList(),
                AffectedJobNames = jobs.Select(j => j.JobName ?? "Unnamed Job").ToList(),
                AffectedJobYears = jobs.Select(j => j.Year ?? "").ToList(),
                GeneratedMetadata = metadata,
                Warnings = warnings,
                ErrorMessage = null
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to migrate profile {ProfileType}", profileType);
            return new ProfileMigrationResult
            {
                ProfileType = profileType,
                Success = false,
                ErrorMessage = ex.Message,
                FieldCount = 0,
                JobsAffected = 0,
                AffectedJobIds = new List<Guid>(),
                AffectedJobNames = new List<string>(),
                AffectedJobYears = new List<string>(),
                GeneratedMetadata = null,
                Warnings = new List<string>()
            };
        }
    }

    /// <summary>
    /// Migrate multiple profiles (or all profiles if filter is null/empty)
    /// </summary>
    public async Task<ProfileBatchMigrationReport> MigrateMultipleProfilesAsync(
        bool dryRun = false,
        List<string>? profileTypeFilter = null)
    {
        var startedAt = DateTime.UtcNow;
        var results = new List<ProfileMigrationResult>();
        var globalWarnings = new List<string>();

        try
        {
            // Get all unique profile types
            var summaries = await GetProfileSummariesAsync();

            // Apply filter if specified
            var profilesToMigrate = summaries
                .Where(s => profileTypeFilter == null || profileTypeFilter.Count == 0 || profileTypeFilter.Contains(s.ProfileType))
                .Select(s => s.ProfileType)
                .ToList();

            _logger.LogInformation(
                "Starting batch profile migration: {Count} profiles (DryRun: {DryRun})",
                profilesToMigrate.Count, dryRun);

            // Migrate each profile
            foreach (var profileType in profilesToMigrate)
            {
                try
                {
                    var result = await MigrateProfileAsync(profileType, dryRun);
                    results.Add(result);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error migrating profile {ProfileType}", profileType);
                    results.Add(new ProfileMigrationResult
                    {
                        ProfileType = profileType,
                        Success = false,
                        ErrorMessage = ex.Message,
                        FieldCount = 0,
                        JobsAffected = 0,
                        AffectedJobIds = new List<Guid>(),
                        AffectedJobNames = new List<string>(),
                        AffectedJobYears = new List<string>(),
                        GeneratedMetadata = null,
                        Warnings = new List<string>()
                    });
                }
            }

            var successCount = results.Count(r => r.Success);
            var failureCount = results.Count - successCount;
            var totalJobsAffected = results.Where(r => r.Success).Sum(r => r.JobsAffected);

            _logger.LogInformation(
                "Batch profile migration completed: {Success} succeeded, {Failed} failed, {TotalJobs} total jobs affected",
                successCount, failureCount, totalJobsAffected);

            return new ProfileBatchMigrationReport
            {
                StartedAt = startedAt,
                CompletedAt = DateTime.UtcNow,
                TotalProfiles = profilesToMigrate.Count,
                SuccessCount = successCount,
                FailureCount = failureCount,
                TotalJobsAffected = totalJobsAffected,
                Results = results,
                GlobalWarnings = globalWarnings
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Batch profile migration failed");
            globalWarnings.Add($"Migration failed: {ex.Message}");

            return new ProfileBatchMigrationReport
            {
                StartedAt = startedAt,
                CompletedAt = DateTime.UtcNow,
                TotalProfiles = 0,
                SuccessCount = 0,
                FailureCount = 0,
                TotalJobsAffected = 0,
                Results = results,
                GlobalWarnings = globalWarnings
            };
        }
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
        var job = await _repo.GetJobWithPlayerMetadataAsync(profileType);

        if (string.IsNullOrEmpty(job?.PlayerProfileMetadataJson))
        {
            return null;
        }

        var metadata = JsonSerializer.Deserialize<ProfileMetadata>(job.PlayerProfileMetadataJson, s_CaseInsensitive);

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
        var job = await _repo.GetJobWithJsonOptionsAsync(jobId);

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
        var jobData = await _repo.GetJobDataForRegistrationAsync(regId);

        if (jobData == null)
        {
            return (null, null);
        }

        var profileType = ExtractProfileType(jobData.CoreRegformPlayer);
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
        var jobData = await _repo.GetJobDataForRegistrationAsync(regId);

        var optionSets = new List<OptionSet>();
        if (jobData == null || string.IsNullOrWhiteSpace(jobData.JsonOptions))
        {
            return optionSets; // empty
        }

        try
        {
            var json = jobData.JsonOptions!;
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
        var jobData = await _repo.GetJobDataForRegistrationAsync(regId);

        if (jobData == null)
            return null;

        Dictionary<string, JsonElement>? dict = null;
        if (!string.IsNullOrWhiteSpace(jobData.JsonOptions))
        {
            try
            {
                dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(jobData.JsonOptions, s_CaseInsensitive);
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

        var newJsonOptions = JsonSerializer.Serialize(dict, s_IndentedCamelCase);
        await _repo.UpdateJobJsonOptionsAsync(jobData.JobId, newJsonOptions);

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
        var jsonList = await _repo.GetAllJobsPlayerMetadataJsonAsync();

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
                DefaultVisibility = MostFrequent(agg.visibilities, VisibilityPublic),
                SeenInProfiles = agg.total
            };
            list.Add(item);
        }

        // Order by visibility then name for readability
        // Order by visibility (Hidden -> Public -> other) using a clearer mapping function
        static int VisibilityRank(string v)
            => v.Equals(VisibilityHidden, StringComparison.OrdinalIgnoreCase) ? 0
             : v.Equals(VisibilityPublic, StringComparison.OrdinalIgnoreCase) ? 1
             : 2;

        list = list
            .OrderBy(i => VisibilityRank(i.DefaultVisibility))
            .ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return list;
    }

    public async Task<bool> DeleteCurrentJobOptionSetAsync(Guid regId, string key)
    {
        var jobData = await _repo.GetJobDataForRegistrationAsync(regId);

        if (jobData == null)
            return false;

        Dictionary<string, JsonElement>? dict = null;
        if (!string.IsNullOrWhiteSpace(jobData.JsonOptions))
        {
            try
            {
                dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(jobData.JsonOptions, s_CaseInsensitive);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to parse Jobs.JsonOptions for delete (regId {RegId})", regId);
            }
        }

        if (dict == null || !dict.Remove(key))
            return false;

        var newJsonOptions = JsonSerializer.Serialize(dict, s_IndentedCamelCase);
        await _repo.UpdateJobJsonOptionsAsync(jobData.JobId, newJsonOptions);
        return true;
    }

    public async Task<bool> RenameCurrentJobOptionSetAsync(Guid regId, string oldKey, string newKey)
    {
        var jobData = await _repo.GetJobDataForRegistrationAsync(regId);

        if (jobData == null)
            return false;

        Dictionary<string, JsonElement>? dict = null;
        if (!string.IsNullOrWhiteSpace(jobData.JsonOptions))
        {
            try
            {
                dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(jobData.JsonOptions, s_CaseInsensitive);
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

        var newJsonOptions = JsonSerializer.Serialize(dict, s_IndentedCamelCase);
        await _repo.UpdateJobJsonOptionsAsync(jobData.JobId, newJsonOptions);
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
        var jobId = await _repo.GetRegistrationJobIdAsync(regId);
        if (jobId == null)
        {
            return results;
        }

        // Gather candidate fields: SELECT types with a dbColumn to map to Registrations
        var selectFields = metadata.Fields
            .Where(f => !string.IsNullOrWhiteSpace(f.DbColumn)
                        && !string.IsNullOrWhiteSpace(f.Name)
                        && string.Equals(f.InputType, InputTypeSelect, StringComparison.OrdinalIgnoreCase))
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
                // Dynamically select the registration property using repository
                var values = await _repo.GetDistinctRegistrationColumnValuesAsync(jobId.Value, column);

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
        try
        {
            _logger.LogInformation("Updating metadata for profile {ProfileType}", profileType);

            // Find ALL jobs using this profile
            var jobs = await _repo.GetJobsByProfileTypeAsync(profileType);

            if (jobs.Count == 0)
            {
                return new ProfileMigrationResult
                {
                    ProfileType = profileType,
                    Success = false,
                    ErrorMessage = $"No jobs found using profile type {profileType}",
                    FieldCount = 0,
                    JobsAffected = 0,
                    AffectedJobIds = new List<Guid>(),
                    AffectedJobNames = new List<string>(),
                    AffectedJobYears = new List<string>(),
                    GeneratedMetadata = null,
                    Warnings = new List<string>()
                };
            }

            // Normalize field order/visibility/HIDDEN + stamp source (shared with adult save path)
            NormalizeMetadataInPlace(metadata);

            // Serialize metadata
            var metadataJson = JsonSerializer.Serialize(metadata, s_IndentedCamelCase);

            // Apply to ALL jobs
            foreach (var job in jobs)
            {
                job.PlayerProfileMetadataJson = metadataJson;
            }

            await _repo.UpdateMultipleJobsPlayerMetadataAsync(jobs);

            _logger.LogInformation(
                "Updated metadata for {ProfileType}: {FieldCount} fields applied to {JobCount} jobs",
                profileType, metadata.Fields.Count, jobs.Count);

            return new ProfileMigrationResult
            {
                ProfileType = profileType,
                Success = true,
                FieldCount = metadata.Fields.Count,
                JobsAffected = jobs.Count,
                AffectedJobIds = jobs.Select(j => j.JobId).ToList(),
                AffectedJobNames = jobs.Select(j => j.JobName ?? "Unnamed Job").ToList(),
                AffectedJobYears = jobs.Select(j => j.Year ?? "").ToList(),
                GeneratedMetadata = metadata,
                Warnings = new List<string>(),
                ErrorMessage = null
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update metadata for profile {ProfileType}", profileType);
            return new ProfileMigrationResult
            {
                ProfileType = profileType,
                Success = false,
                ErrorMessage = ex.Message,
                FieldCount = 0,
                JobsAffected = 0,
                AffectedJobIds = new List<Guid>(),
                AffectedJobNames = new List<string>(),
                AffectedJobYears = new List<string>(),
                GeneratedMetadata = null,
                Warnings = new List<string>()
            };
        }
    }

    /// <summary>
    /// Normalizes a metadata block in place, shared by the player and adult save paths so the two
    /// never drift: forces hidden fields to inputType HIDDEN, renumbers Order consecutively by
    /// visibility group (Hidden -> Public -> AdminOnly), reorders the Fields list to match, and
    /// stamps source tracking.
    /// </summary>
    private static void NormalizeMetadataInPlace(ProfileMetadata metadata, string migratedBy = "ProfileEditor")
    {
        // ensure hidden fields use inputType = HIDDEN
        metadata.Fields
            .Where(f => string.Equals(f.Visibility, VisibilityHidden, StringComparison.OrdinalIgnoreCase))
            .ToList()
            .ForEach(f => f.InputType = InputTypeHidden);

        // Renumber orders consistently by visibility groups: Hidden -> Public -> AdminOnly
        var publics = metadata.Fields.Where(f => string.Equals(f.Visibility, VisibilityPublic, StringComparison.OrdinalIgnoreCase))
                                     .OrderBy(f => f.Order)
                                     .ToList();
        var admins = metadata.Fields.Where(f => string.Equals(f.Visibility, VisibilityAdminOnly, StringComparison.OrdinalIgnoreCase))
                                     .OrderBy(f => f.Order)
                                     .ToList();
        var hiddens = metadata.Fields.Where(f => string.Equals(f.Visibility, VisibilityHidden, StringComparison.OrdinalIgnoreCase))
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
        metadata.Source.MigratedBy = migratedBy;
    }

    /// <summary>
    /// Reads the current job's role-keyed adult metadata (all three adult roles) for the registrant
    /// resolved from the JWT regId. Absent roles come back as an empty { fields: [] } block.
    /// </summary>
    public async Task<AdultRoleMetadataSet?> GetCurrentJobAdultMetadataAsync(Guid regId)
    {
        var jobData = await _repo.GetJobDataForRegistrationAsync(regId);
        if (jobData == null) return null;

        var json = jobData.AdultProfileMetadataJson;
        return new AdultRoleMetadataSet
        {
            UnassignedAdult = ParseAdultRoleOrEmpty(json, AdultMetadataRoleKeys.UnassignedAdult),
            Referee = ParseAdultRoleOrEmpty(json, AdultMetadataRoleKeys.Referee),
            Recruiter = ParseAdultRoleOrEmpty(json, AdultMetadataRoleKeys.Recruiter)
        };
    }

    /// <summary>
    /// Replaces ONE adult role's field set in the current job's AdultProfileMetadataJson, preserving
    /// the other roles' sub-objects byte-for-byte. Returns the normalized metadata that was persisted,
    /// or null when the job cannot be resolved.
    /// </summary>
    public async Task<ProfileMetadata?> UpdateCurrentJobAdultRoleMetadataAsync(Guid regId, string roleKey, ProfileMetadata metadata)
    {
        if (!AdultMetadataRoleKeys.IsValid(roleKey))
            throw new ArgumentException($"Invalid adult role key '{roleKey}'.", nameof(roleKey));

        var jobData = await _repo.GetJobDataForRegistrationAsync(regId);
        if (jobData == null) return null;

        // 1) Normalize incoming role metadata exactly like the player save.
        NormalizeMetadataInPlace(metadata, "AdultProfileEditor");

        // 2) Parse the existing root as a mutable JsonObject so untouched roles are copied verbatim.
        var root = string.IsNullOrWhiteSpace(jobData.AdultProfileMetadataJson)
            ? new JsonObject()
            : (JsonNode.Parse(jobData.AdultProfileMetadataJson) as JsonObject) ?? new JsonObject();

        // 3) Replace ONLY this role (round-trip the role node through camelCase serialization).
        var roleJson = JsonSerializer.Serialize(metadata, s_IndentedCamelCase);
        root[roleKey] = JsonNode.Parse(roleJson);

        // 4) Persist the whole root by jobId.
        var newRootJson = root.ToJsonString(s_IndentedCamelCase);
        await _repo.UpdateJobAdultMetadataAsync(jobData.JobId, newRootJson);

        return metadata;
    }

    /// <summary>
    /// Copy another job's player and/or adult (coach) form definition onto the current job (resolved
    /// from <paramref name="regId"/>). Form-JSON only: the runtime renders from the materialized
    /// metadata, so the copied form works immediately with its baked-in options. Template pointers
    /// (CoreRegformPlayer / RegformNameCoach) and JsonOptions size customizations are intentionally NOT
    /// carried. Validates the requested form(s) exist on the source BEFORE writing — never a partial copy.
    /// </summary>
    public async Task<CopyJobFormsResult> CopyFormsToCurrentJobAsync(Guid regId, CopyJobFormsRequest request)
    {
        if (!request.IncludePlayer && !request.IncludeCoach)
            return Fail("Select at least the player or the coach form to copy.");

        var target = await _repo.GetJobDataForRegistrationAsync(regId);
        if (target == null)
            return Fail("Current job could not be resolved.");

        if (request.SourceJobId == target.JobId)
            return Fail("The source job is the same as the current job.");

        var source = await _repo.GetJobBasicInfoAsync(request.SourceJobId);
        if (source == null)
            return Fail("Source job not found.");

        if (request.IncludePlayer && string.IsNullOrWhiteSpace(source.PlayerProfileMetadataJson))
            return Fail($"'{source.JobName}' has no player form to copy.");
        if (request.IncludeCoach && string.IsNullOrWhiteSpace(source.AdultProfileMetadataJson))
            return Fail($"'{source.JobName}' has no coach/adult form to copy.");

        var playerCopied = false;
        var coachCopied = false;

        if (request.IncludePlayer)
        {
            await _repo.UpdateJobPlayerMetadataAsync(target.JobId, source.PlayerProfileMetadataJson!);
            playerCopied = true;
        }

        if (request.IncludeCoach)
        {
            await _repo.UpdateJobAdultMetadataAsync(target.JobId, source.AdultProfileMetadataJson!);
            coachCopied = true;
        }

        _logger.LogInformation(
            "Copied forms from job {SourceJobId} onto current job {TargetJobId} (player={PlayerCopied}, coach={CoachCopied})",
            request.SourceJobId, target.JobId, playerCopied, coachCopied);

        return new CopyJobFormsResult
        {
            Success = true,
            PlayerCopied = playerCopied,
            CoachCopied = coachCopied,
            SourceJobName = source.JobName,
            ErrorMessage = null
        };

        static CopyJobFormsResult Fail(string message) => new()
        {
            Success = false,
            PlayerCopied = false,
            CoachCopied = false,
            SourceJobName = string.Empty,
            ErrorMessage = message
        };
    }

    /// <summary>
    /// List every job that can serve as a copy source for <see cref="CopyFormsToCurrentJobAsync"/>,
    /// flagged with which form(s) it carries. Composes the two summary reads the migration tooling
    /// already exposes (player + adult) — no new repository query. The current job (resolved from
    /// <paramref name="regId"/>) is excluded so it can't be picked as its own source.
    /// </summary>
    public async Task<List<CopyFormSourceDto>> GetCopyFormSourcesAsync(Guid regId)
    {
        // Resolve the current job so it can be filtered out of the candidate list.
        var current = await _repo.GetJobDataForRegistrationAsync(regId);
        var currentJobId = current?.JobId ?? Guid.Empty;

        // Sequential awaits — both reads share the same scoped DbContext (never Task.WhenAll).
        var playerJobs = await _repo.GetJobsForProfileSummaryAsync();
        var adultJobs = await _repo.GetJobsForAdultProfileSummaryAsync();

        var byJob = new Dictionary<Guid, CopyFormSourceDto>();

        foreach (var p in playerJobs)
        {
            byJob[p.JobId] = new CopyFormSourceDto
            {
                JobId = p.JobId,
                JobName = p.JobName,
                Year = null,
                HasPlayerForm = !string.IsNullOrWhiteSpace(p.PlayerProfileMetadataJson),
                HasCoachForm = false
            };
        }

        foreach (var a in adultJobs)
        {
            var hasCoach = !string.IsNullOrWhiteSpace(a.AdultProfileMetadataJson);
            byJob[a.JobId] = byJob.TryGetValue(a.JobId, out var existing)
                ? existing with { Year = a.Year, HasCoachForm = hasCoach }
                : new CopyFormSourceDto
                {
                    JobId = a.JobId,
                    JobName = a.JobName,
                    Year = a.Year,
                    HasPlayerForm = false,
                    HasCoachForm = hasCoach
                };
        }

        return byJob.Values
            .Where(j => j.JobId != currentJobId && (j.HasPlayerForm || j.HasCoachForm))
            .OrderBy(j => j.JobName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(j => j.Year)
            .ToList();
    }

    private static ProfileMetadata ParseAdultRoleOrEmpty(string? rootJson, string roleKey)
    {
        if (!string.IsNullOrWhiteSpace(rootJson))
        {
            using var doc = JsonDocument.Parse(rootJson);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty(roleKey, out var roleEl)
                && roleEl.ValueKind == JsonValueKind.Object)
            {
                var meta = JsonSerializer.Deserialize<ProfileMetadata>(roleEl.GetRawText(), s_CaseInsensitive);
                if (meta != null)
                {
                    meta.Fields ??= new List<ProfileMetadataField>();
                    return meta;
                }
            }
        }
        return new ProfileMetadata { Fields = new List<ProfileMetadataField>() };
    }

    // ============================================================================
    // ADULT PROFILE MIGRATION (materializes role-keyed Jobs.AdultProfileMetadataJson).
    // Mirrors the player profile-centric methods; canonical profiles (AC1/AC2) are OUR
    // nomenclature mapped from legacy RegformName_Coach via AdultFormCatalog, with USLax
    // as an orthogonal per-job capability (a required sportAssnId), never a separate form.
    // ============================================================================

    /// <summary>
    /// Summarize the canonical adult profiles (AC1/AC2) across all jobs: job counts, how many carry the
    /// USLax capability, and migration status. Adult analog of <see cref="GetProfileSummariesAsync"/>.
    /// </summary>
    public async Task<List<AdultProfileSummary>> GetAdultProfileSummariesAsync()
    {
        var jobs = await _repo.GetJobsForAdultProfileSummaryAsync();

        return jobs
            .Select(j => new
            {
                j.JobName,
                Map = AdultFormCatalog.MapLegacy(j.RegformNameCoach),
                HasMetadata = !string.IsNullOrEmpty(j.AdultProfileMetadataJson)
            })
            .GroupBy(j => j.Map.Profile)
            .Select(g => new AdultProfileSummary
            {
                Profile = g.Key,
                DisplayName = AdultFormCatalog.DisplayName(g.Key),
                JobCount = g.Count(),
                UsLaxJobCount = g.Count(j => j.Map.RequiresUsLax),
                MigratedJobCount = g.Count(j => j.HasMetadata),
                AllJobsMigrated = g.All(j => j.HasMetadata),
                SampleJobNames = g.Take(5).Select(j => j.JobName ?? "Unnamed Job").ToList()
            })
            .OrderBy(p => p.Profile)
            .ToList();
    }

    /// <summary>Preview (dry run) materialization for a single adult profile — shows the full scope + metadata.</summary>
    public async Task<AdultProfileMigrationResult> PreviewAdultProfileMigrationAsync(string profile)
        => await MigrateAdultProfileAsync(profile, dryRun: true, force: true);

    /// <summary>
    /// Materialize one canonical adult profile across all its jobs: for each job,
    /// <c>MapLegacy</c> → <c>BuildRoleSet</c> → seed apparel option sets (AC2) → inject job options →
    /// normalize each role → write the full three-role object. Idempotent: skips already-materialized jobs
    /// unless <paramref name="force"/>.
    /// </summary>
    public async Task<AdultProfileMigrationResult> MigrateAdultProfileAsync(string profile, bool dryRun = false, bool force = false)
    {
        profile = AdultFormCatalog.Canonical(profile);
        if (!AdultFormCatalog.IsKnownProfile(profile))
            return FailedAdultResult(profile, $"Unknown adult profile '{profile}'");

        try
        {
            var allJobs = await _repo.GetJobsForAdultMigrationAsync();
            var target = allJobs
                .Where(j => string.Equals(AdultFormCatalog.MapLegacy(j.RegformNameCoach).Profile, profile, StringComparison.OrdinalIgnoreCase))
                .ToList();

            var affected = new List<Jobs>();
            var uslaxAffected = 0;

            foreach (var job in target)
            {
                if (!force && !string.IsNullOrEmpty(job.AdultProfileMetadataJson))
                    continue; // already materialized — idempotent skip

                var (_, requiresUsLax) = AdultFormCatalog.MapLegacy(job.RegformNameCoach);
                if (!dryRun)
                    job.AdultProfileMetadataJson = MaterializeAdultForJob(job, profile, requiresUsLax);

                affected.Add(job);
                if (requiresUsLax) uslaxAffected++;
            }

            if (!dryRun && affected.Count > 0)
                await _repo.UpdateMultipleJobsAdultMetadataAsync(allJobs);

            var anyUsLax = target.Exists(j => AdultFormCatalog.MapLegacy(j.RegformNameCoach).RequiresUsLax);

            _logger.LogInformation(
                "Adult profile {Profile}: materialized {Count} jobs ({UsLax} with USLax), DryRun={DryRun}, Force={Force}",
                profile, affected.Count, uslaxAffected, dryRun, force);

            return new AdultProfileMigrationResult
            {
                Profile = profile,
                DisplayName = AdultFormCatalog.DisplayName(profile),
                Success = true,
                JobsAffected = affected.Count,
                UsLaxJobsAffected = uslaxAffected,
                AffectedJobIds = affected.Select(j => j.JobId).ToList(),
                AffectedJobNames = affected.Select(j => j.JobName ?? "Unnamed Job").ToList(),
                AffectedJobYears = affected.Select(j => j.Year ?? "").ToList(),
                GeneratedMetadata = AdultFormCatalog.BuildRoleSet(profile, requiresUsLax: false),
                GeneratedMetadataUsLax = anyUsLax ? AdultFormCatalog.BuildRoleSet(profile, requiresUsLax: true) : null,
                Warnings = new List<string>(),
                ErrorMessage = null
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to materialize adult profile {Profile}", profile);
            return FailedAdultResult(profile, ex.Message);
        }
    }

    /// <summary>Materialize multiple adult profiles (or all if no filter). Adult analog of <see cref="MigrateMultipleProfilesAsync"/>.</summary>
    public async Task<AdultProfileBatchMigrationReport> MigrateAllAdultProfilesAsync(bool dryRun = false, bool force = false, List<string>? profiles = null)
    {
        var startedAt = DateTime.UtcNow;
        var results = new List<AdultProfileMigrationResult>();

        var targets = AdultFormCatalog.AllProfiles
            .Where(p => profiles == null || profiles.Count == 0
                || profiles.Exists(f => string.Equals(f, p, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        foreach (var profile in targets)
            results.Add(await MigrateAdultProfileAsync(profile, dryRun, force));

        var successCount = results.Count(r => r.Success);

        return new AdultProfileBatchMigrationReport
        {
            StartedAt = startedAt,
            CompletedAt = DateTime.UtcNow,
            TotalProfiles = targets.Count,
            SuccessCount = successCount,
            FailureCount = results.Count - successCount,
            TotalJobsAffected = results.Where(r => r.Success).Sum(r => r.JobsAffected),
            Results = results,
            GlobalWarnings = new List<string>()
        };
    }

    /// <summary>
    /// SQL export for adult metadata — idempotent, touches ONLY [AdultProfileMetadataJson]. Inline apparel
    /// options make each row self-contained, so no JsonOptions rows are exported (never clobbers a job's options).
    /// </summary>
    public async Task<string> GenerateAdultMigrationSqlScriptAsync()
    {
        var jobs = await _repo.GetJobsForAdultProfileSummaryAsync();
        var withMetadata = jobs.Where(j => !string.IsNullOrEmpty(j.AdultProfileMetadataJson)).ToList();

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("-- =====================================================");
        sb.AppendLine("-- Adult Profile Migration SQL Export");
        sb.AppendLine($"-- Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine($"-- Jobs with adult metadata: {withMetadata.Count}");
        sb.AppendLine("-- Idempotent: safe to run multiple times");
        sb.AppendLine("-- Only touches: [Jobs].[Jobs].[AdultProfileMetadataJson]");
        sb.AppendLine("-- =====================================================");
        sb.AppendLine();
        sb.AppendLine("SET NOCOUNT ON;");
        sb.AppendLine("SET XACT_ABORT ON;");
        sb.AppendLine("BEGIN TRANSACTION;");
        sb.AppendLine();

        foreach (var job in withMetadata)
        {
            var escapedJson = job.AdultProfileMetadataJson!.Replace("'", "''");
            sb.AppendLine($"-- {job.JobName ?? "Unnamed"} (Job ID: {job.JobId})");
            sb.AppendLine("UPDATE [Jobs].[Jobs]");
            sb.AppendLine($"SET [AdultProfileMetadataJson] = '{escapedJson}'");
            sb.AppendLine($"WHERE [jobID] = '{job.JobId}';");
            sb.AppendLine();
        }

        sb.AppendLine("COMMIT TRANSACTION;");
        sb.AppendLine();
        sb.AppendLine($"PRINT 'Adult profile migration complete: {withMetadata.Count} jobs updated';");
        sb.AppendLine();
        sb.AppendLine("-- Verify results:");
        sb.AppendLine("SELECT COUNT(*) AS [Jobs With Adult Metadata] FROM [Jobs].[Jobs] WHERE [AdultProfileMetadataJson] IS NOT NULL;");

        return sb.ToString();
    }

    /// <summary>
    /// Materializes one job's full three-role adult metadata for the given profile/USLax, seeding apparel
    /// option sets into the job's JsonOptions (upsert-if-absent) for AC2. Mutates <paramref name="job"/>.JsonOptions.
    /// </summary>
    private string MaterializeAdultForJob(Jobs job, string profile, bool requiresUsLax)
    {
        var roleSet = AdultFormCatalog.BuildRoleSet(profile, requiresUsLax);

        // Seed the apparel ListSizes_* into Jobs.JsonOptions so sizes are admin-editable via the option-set
        // editor — but ONLY the sets this profile's coach form actually references (AC2 → 4 sets, AC3 →
        // jersey+shoe, AC1 → none). Derived from the built coach fields so profiles never over-seed.
        // Upsert-if-absent — never clobber a job's existing custom size list.
        var apparelKeys = roleSet.UnassignedAdult.Fields
            .Where(f => !string.IsNullOrWhiteSpace(f.DataSource)
                        && AdultFormCatalog.ApparelOptionSets.ContainsKey(f.DataSource!))
            .Select(f => f.DataSource!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (apparelKeys.Count > 0)
            job.JsonOptions = UpsertApparelOptionSets(job.JsonOptions, apparelKeys);

        // Inject the job's JsonOptions into each role's SELECT fields (custom sizes win over inline defaults),
        // then normalize exactly like the player save (order/visibility/HIDDEN + source stamp).
        foreach (var role in new[] { roleSet.UnassignedAdult, roleSet.Referee, roleSet.Recruiter })
        {
            InjectJobOptionsIntoMetadata(role, job.JsonOptions);
            NormalizeMetadataInPlace(role, "AdultFormCatalog");
        }

        var root = new JsonObject
        {
            [AdultMetadataRoleKeys.UnassignedAdult] = JsonNode.Parse(JsonSerializer.Serialize(roleSet.UnassignedAdult, s_IndentedCamelCase)),
            [AdultMetadataRoleKeys.Referee] = JsonNode.Parse(JsonSerializer.Serialize(roleSet.Referee, s_IndentedCamelCase)),
            [AdultMetadataRoleKeys.Recruiter] = JsonNode.Parse(JsonSerializer.Serialize(roleSet.Recruiter, s_IndentedCamelCase))
        };
        return root.ToJsonString(s_IndentedCamelCase);
    }

    /// <summary>
    /// Per-job coach-form swap: rebuild ONLY the coach (UnassignedAdult) role from a chosen profile + USLax,
    /// preserving Referee/Recruiter when a blob already exists. Falls back to a full three-role materialize
    /// when the job has no (or malformed) existing blob. Mutates <paramref name="job"/>.JsonOptions; does not
    /// persist. See the interface doc for the caller contract.
    /// </summary>
    public string ComputeCoachFormSwap(Jobs job, string profile, bool requiresUsLax)
    {
        profile = AdultFormCatalog.Canonical(profile);

        // No usable existing blob → full three-role build (also seeds apparel JsonOptions).
        JsonObject? existing = null;
        if (!string.IsNullOrWhiteSpace(job.AdultProfileMetadataJson))
        {
            try { existing = JsonNode.Parse(job.AdultProfileMetadataJson) as JsonObject; }
            catch { existing = null; /* malformed — rebuild from scratch */ }
        }
        if (existing is null)
            return MaterializeAdultForJob(job, profile, requiresUsLax);

        // Existing blob → rebuild the coach role only, leaving Referee/Recruiter sub-objects verbatim.
        var coach = AdultFormCatalog.BuildRoleSet(profile, requiresUsLax).UnassignedAdult;

        // Seed only the apparel sets this coach form references (upsert-if-absent; AC1 seeds none).
        var apparelKeys = coach.Fields
            .Where(f => !string.IsNullOrWhiteSpace(f.DataSource)
                        && AdultFormCatalog.ApparelOptionSets.ContainsKey(f.DataSource!))
            .Select(f => f.DataSource!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (apparelKeys.Count > 0)
            job.JsonOptions = UpsertApparelOptionSets(job.JsonOptions, apparelKeys);

        InjectJobOptionsIntoMetadata(coach, job.JsonOptions);
        NormalizeMetadataInPlace(coach, "AdultFormCatalog");

        existing[AdultMetadataRoleKeys.UnassignedAdult] =
            JsonNode.Parse(JsonSerializer.Serialize(coach, s_IndentedCamelCase));
        return existing.ToJsonString(s_IndentedCamelCase);
    }

    /// <summary>Adds the named apparel option sets to a job's JsonOptions if the key is absent (case-insensitive).</summary>
    private static string UpsertApparelOptionSets(string? jsonOptions, IEnumerable<string> keys)
    {
        Dictionary<string, JsonElement>? dict = null;
        if (!string.IsNullOrWhiteSpace(jsonOptions))
        {
            try { dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(jsonOptions, s_CaseInsensitive); }
            catch { /* ignore malformed — start fresh */ }
        }
        dict ??= new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);

        foreach (var key in keys)
        {
            if (!AdultFormCatalog.ApparelOptionSets.TryGetValue(key, out var values))
                continue; // not a known apparel set
            if (dict.Keys.Any(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase)))
                continue; // upsert-if-absent

            // Seed in the legacy Jobs.JsonOptions item shape { "Text", "Value" } (PascalCase) — the same
            // convention every other option set uses and the ONLY shape DdlOptionsService (Configure Job
            // Dropdowns) reads. Serializing ProfileFieldOption directly emits { "value", "label" }, which
            // that editor's case-sensitive { Text, Value } binder silently drops (dropdown shows no values).
            var legacyItems = values.Select(o => new { Text = o.Label ?? o.Value, o.Value });
            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(legacyItems));
            dict[key] = doc.RootElement.Clone();
        }

        return JsonSerializer.Serialize(dict, s_IndentedCamelCase);
    }

    // ============================================================================
    // ADULT PROFILE EDITOR (type-scoped, mirrors the player profile editor). Edits a canonical
    // profile's substantive role fields; USLax stays a per-job capability re-composed from the
    // immutable RegformName_Coach, so the coach role's sportAssnId is never edited directly.
    // ============================================================================

    /// <summary>
    /// Type-scoped READ: the role-keyed metadata for a profile, sampled from a representative already-migrated
    /// job (coach block stripped of the capability sportAssnId so it shows only substantive fields). Falls back
    /// to the catalog base template when no job of the profile is materialized yet.
    /// </summary>
    public async Task<AdultRoleMetadataSet> GetAdultProfileMetadataAsync(string profile)
    {
        profile = AdultFormCatalog.Canonical(profile);
        if (!AdultFormCatalog.IsKnownProfile(profile))
            return new AdultRoleMetadataSet();

        var jobs = await _repo.GetJobsForAdultProfileSummaryAsync();
        var rep = jobs.FirstOrDefault(j =>
            !string.IsNullOrEmpty(j.AdultProfileMetadataJson)
            && string.Equals(AdultFormCatalog.MapLegacy(j.RegformNameCoach).Profile, profile, StringComparison.OrdinalIgnoreCase));

        if (rep?.AdultProfileMetadataJson is { } json && !string.IsNullOrWhiteSpace(json))
        {
            return new AdultRoleMetadataSet
            {
                UnassignedAdult = StripCapabilityFields(ParseAdultRoleOrEmpty(json, AdultMetadataRoleKeys.UnassignedAdult)),
                Referee = ParseAdultRoleOrEmpty(json, AdultMetadataRoleKeys.Referee),
                Recruiter = ParseAdultRoleOrEmpty(json, AdultMetadataRoleKeys.Recruiter)
            };
        }

        return AdultFormCatalog.BuildRoleSet(profile, requiresUsLax: false);
    }

    /// <summary>
    /// Type-scoped WRITE: replace ONE role's field set across all already-materialized jobs of a profile,
    /// preserving the other two roles per job. For the coach role, the incoming sportAssnId is stripped and
    /// re-composed per job from RegformName_Coach (USLax jobs keep their required sportAssnId).
    /// </summary>
    public async Task<AdultProfileMigrationResult> UpdateAdultProfileRoleAsync(string profile, string roleKey, ProfileMetadata metadata)
    {
        profile = AdultFormCatalog.Canonical(profile);
        if (!AdultFormCatalog.IsKnownProfile(profile))
            return FailedAdultResult(profile, $"Unknown adult profile '{profile}'");
        if (!AdultMetadataRoleKeys.IsValid(roleKey))
            return FailedAdultResult(profile, $"Invalid adult role key '{roleKey}'");

        try
        {
            var isCoach = string.Equals(roleKey, AdultMetadataRoleKeys.UnassignedAdult, StringComparison.Ordinal);
            var baseMeta = isCoach ? StripCapabilityFields(metadata) : metadata;

            var allJobs = await _repo.GetJobsForAdultMigrationAsync();
            var target = allJobs
                .Where(j => string.Equals(AdultFormCatalog.MapLegacy(j.RegformNameCoach).Profile, profile, StringComparison.OrdinalIgnoreCase)
                            && !string.IsNullOrEmpty(j.AdultProfileMetadataJson)) // only already-materialized jobs
                .ToList();

            var affected = new List<Jobs>();
            var uslaxAffected = 0;

            foreach (var job in target)
            {
                var (_, requiresUsLax) = AdultFormCatalog.MapLegacy(job.RegformNameCoach);
                var roleMeta = CloneMetadata(baseMeta);

                if (isCoach && requiresUsLax)
                {
                    roleMeta.Fields.Insert(0, AdultFormCatalog.UsLaxField());
                    uslaxAffected++;
                }

                // Renumber by list position so the capability field sorts first, then inject options + normalize.
                for (var i = 0; i < roleMeta.Fields.Count; i++) roleMeta.Fields[i].Order = i + 1;
                InjectJobOptionsIntoMetadata(roleMeta, job.JsonOptions);
                NormalizeMetadataInPlace(roleMeta, "AdultProfileEditor");

                var root = string.IsNullOrWhiteSpace(job.AdultProfileMetadataJson)
                    ? new JsonObject()
                    : (JsonNode.Parse(job.AdultProfileMetadataJson) as JsonObject) ?? new JsonObject();
                root[roleKey] = JsonNode.Parse(JsonSerializer.Serialize(roleMeta, s_IndentedCamelCase));
                job.AdultProfileMetadataJson = root.ToJsonString(s_IndentedCamelCase);

                affected.Add(job);
            }

            if (affected.Count > 0)
                await _repo.UpdateMultipleJobsAdultMetadataAsync(allJobs);

            _logger.LogInformation("Adult editor: updated role {Role} on {Count} jobs of profile {Profile}",
                roleKey, affected.Count, profile);

            return new AdultProfileMigrationResult
            {
                Profile = profile,
                DisplayName = AdultFormCatalog.DisplayName(profile),
                Success = true,
                JobsAffected = affected.Count,
                UsLaxJobsAffected = isCoach ? uslaxAffected : 0,
                AffectedJobIds = affected.Select(j => j.JobId).ToList(),
                AffectedJobNames = affected.Select(j => j.JobName ?? "Unnamed Job").ToList(),
                AffectedJobYears = affected.Select(j => j.Year ?? "").ToList(),
                GeneratedMetadata = null,
                GeneratedMetadataUsLax = null,
                Warnings = affected.Count == 0
                    ? new List<string> { "No materialized jobs for this profile yet — run the migration first." }
                    : new List<string>(),
                ErrorMessage = null
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update adult profile {Profile} role {Role}", profile, roleKey);
            return FailedAdultResult(profile, ex.Message);
        }
    }

    /// <summary>Deep-clone a role's metadata without the USLax capability fields (they are per-job, not substantive).</summary>
    private ProfileMetadata StripCapabilityFields(ProfileMetadata metadata)
    {
        var clone = CloneMetadata(metadata);
        clone.Fields = clone.Fields
            .Where(f => !AdultFormCatalog.UsLaxCapabilityFieldNames.Contains(f.Name ?? string.Empty))
            .ToList();
        return clone;
    }

    private static AdultProfileMigrationResult FailedAdultResult(string profile, string error) => new()
    {
        Profile = profile,
        DisplayName = AdultFormCatalog.DisplayName(profile),
        Success = false,
        JobsAffected = 0,
        UsLaxJobsAffected = 0,
        AffectedJobIds = new List<Guid>(),
        AffectedJobNames = new List<string>(),
        AffectedJobYears = new List<string>(),
        GeneratedMetadata = null,
        GeneratedMetadataUsLax = null,
        Warnings = new List<string>(),
        ErrorMessage = error
    };

    /// <summary>
    /// Test field validation rules without saving
    /// </summary>
    public ValidationTestResult TestFieldValidation(ProfileMetadataField field, string testValue)
    {
        var isValid = true;
        var messages = new List<string>();

        // Run validation rules if present
        if (field.Validation != null)
        {
            ValidateRequired(field, testValue, ref isValid, messages);
            ValidateRequiredTrue(field, testValue, ref isValid, messages);
            ValidateLength(field, testValue, ref isValid, messages);
            ValidateNumericRange(field, testValue, ref isValid, messages);
            ValidatePattern(field, testValue, ref isValid, messages);
            ValidateEmail(field, testValue, ref isValid, messages);
        }

        if (isValid && messages.Count == 0)
        {
            messages.Add("✓ Validation passed");
        }

        return new ValidationTestResult
        {
            FieldName = field.Name,
            TestValue = testValue,
            IsValid = isValid,
            Messages = messages
        };
    }

    private static void ValidateRequired(ProfileMetadataField field, string testValue, ref bool isValid, List<string> messages)
    {
        if (field.Validation?.Required == true && string.IsNullOrWhiteSpace(testValue))
        {
            isValid = false;
            messages.Add("Field is required");
        }
    }

    private static void ValidateRequiredTrue(ProfileMetadataField field, string testValue, ref bool isValid, List<string> messages)
    {
        if (field.Validation?.RequiredTrue == true && (!bool.TryParse(testValue, out var boolValue) || !boolValue))
        {
            isValid = false;
            messages.Add("Checkbox must be checked (value must be true)");
        }
    }

    private static void ValidateLength(ProfileMetadataField field, string testValue, ref bool isValid, List<string> messages)
    {
        if (field.Validation?.MinLength.HasValue == true && testValue.Length < field.Validation.MinLength.Value)
        {
            isValid = false;
            messages.Add($"Value too short (min: {field.Validation.MinLength})");
        }
        if (field.Validation?.MaxLength.HasValue == true && testValue.Length > field.Validation.MaxLength.Value)
        {
            isValid = false;
            messages.Add($"Value too long (max: {field.Validation.MaxLength})");
        }
    }

    private static void ValidateNumericRange(ProfileMetadataField field, string testValue, ref bool isValid, List<string> messages)
    {
        if (string.Equals(field.InputType, InputTypeNumber, StringComparison.OrdinalIgnoreCase) && double.TryParse(testValue, out var numValue))
        {
            if (field.Validation?.Min.HasValue == true && numValue < field.Validation.Min.Value)
            {
                isValid = false;
                messages.Add($"Value too small (min: {field.Validation.Min})");
            }
            if (field.Validation?.Max.HasValue == true && numValue > field.Validation.Max.Value)
            {
                isValid = false;
                messages.Add($"Value too large (max: {field.Validation.Max})");
            }
        }
    }

    private static void ValidatePattern(ProfileMetadataField field, string testValue, ref bool isValid, List<string> messages)
    {
        if (!string.IsNullOrEmpty(field.Validation?.Pattern))
        {
            var regex = new System.Text.RegularExpressions.Regex(field.Validation.Pattern);
            if (!regex.IsMatch(testValue))
            {
                isValid = false;
                messages.Add($"Value does not match required pattern: {field.Validation.Pattern}");
            }
        }
    }

    private static void ValidateEmail(ProfileMetadataField field, string testValue, ref bool isValid, List<string> messages)
    {
        if (field.Validation?.Email == true && string.Equals(field.InputType, InputTypeEmail, StringComparison.OrdinalIgnoreCase))
        {
            var emailRegex = new System.Text.RegularExpressions.Regex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$");
            if (!emailRegex.IsMatch(testValue))
            {
                isValid = false;
                messages.Add("Invalid email format");
            }
        }
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
        try
        {
            // Get the source profile metadata
            var sourceMetadata = await GetProfileMetadataAsync(sourceProfileType);
            if (sourceMetadata == null)
            {
                return new CloneProfileResult
                {
                    SourceProfileType = sourceProfileType,
                    Success = false,
                    ErrorMessage = $"Source profile '{sourceProfileType}' not found",
                    NewProfileType = string.Empty,
                    FieldCount = 0
                };
            }

            // Get the job data from the registration
            var jobData = await _repo.GetJobDataForRegistrationAsync(regId);

            if (jobData == null)
            {
                return new CloneProfileResult
                {
                    SourceProfileType = sourceProfileType,
                    Success = false,
                    ErrorMessage = $"Registration with ID '{regId}' not found or has no job",
                    NewProfileType = string.Empty,
                    FieldCount = 0
                };
            }

            // Determine the next profile id for the family (PP or CAC) based on global max across Jobs
            var newProfileType = await ComputeNextProfileTypeAsync(sourceProfileType);

            // Create metadata for new profile (clone from source)
            var newMetadata = CloneMetadata(sourceMetadata);
            var metadataJson = JsonSerializer.Serialize(newMetadata);

            // Update the current job's CoreRegformPlayer preserving pipe-delimited structure
            var updatedCoreRegform = UpdateCoreRegformPlayer(jobData.CoreRegformPlayer, newProfileType);
            await _repo.UpdateJobCoreRegformAndMetadataAsync(jobData.JobId, updatedCoreRegform, metadataJson);

            _logger.LogInformation(
                "Created new profile type: {NewProfileType} from {SourceProfileType} for job {JobName} (JobId: {JobId})",
                newProfileType, sourceProfileType, jobData.JobName, jobData.JobId);

            return new CloneProfileResult
            {
                SourceProfileType = sourceProfileType,
                Success = true,
                NewProfileType = newProfileType,
                FieldCount = newMetadata.Fields.Count,
                ErrorMessage = null
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cloning profile {SourceProfileType}", sourceProfileType);
            return new CloneProfileResult
            {
                SourceProfileType = sourceProfileType,
                Success = false,
                ErrorMessage = ex.Message,
                NewProfileType = string.Empty,
                FieldCount = 0
            };
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
        var all = await _repo.GetJobsCoreRegformValuesAsync();

        int maxNum = 0;
        foreach (var val in all)
        {
            foreach (var part in SplitCoreRegform(val).Where(p => p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            {
                var n = ExtractTrailingNumber(part);
                if (n.HasValue && n.Value > maxNum)
                    maxNum = n.Value;
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
    private static (string? ProfileType, string? TeamConstraint) ParseCoreRegformParts(string? coreRegformPlayer)
    {
        if (string.IsNullOrWhiteSpace(coreRegformPlayer) || coreRegformPlayer == "0" || coreRegformPlayer == "1")
            return (null, null);

        var parts = coreRegformPlayer.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
            return (null, null);

        static bool IsProfileType(string p) =>
            !string.IsNullOrWhiteSpace(p) && (p.StartsWith("PP", StringComparison.OrdinalIgnoreCase) || p.StartsWith("CAC", StringComparison.OrdinalIgnoreCase));

        // Identify profile type as the first PP##/CAC## segment
        string? profileType = Array.Find(parts, IsProfileType);

        // Team constraint: first non-empty, non-profileType, non-ALLOWPIF segment
        string? teamConstraint = Array.Find(parts, p =>
            !string.IsNullOrWhiteSpace(p)
            && !IsProfileType(p)
        );

        return (profileType, teamConstraint);
    }

    private static string BuildCoreRegform(string profileType, string teamConstraint)
    {
        var list = new List<string> { profileType };
        if (!string.IsNullOrWhiteSpace(teamConstraint))
        {
            list.Add(teamConstraint);
        }
        return string.Join('|', list);
    }

    public async Task<(string? ProfileType, string? TeamConstraint, string Raw, ProfileMetadata? Metadata)> GetCurrentJobProfileConfigAsync(Guid regId)
    {
        var jobData = await _repo.GetJobDataForRegistrationAsync(regId);
        if (jobData == null)
        {
            return (null, null, string.Empty, null);
        }

        var raw = jobData.CoreRegformPlayer ?? string.Empty;
        var (pt, constraint) = ParseCoreRegformParts(raw);
        ProfileMetadata? metadata = null;
        if (!string.IsNullOrEmpty(pt))
        {
            metadata = await GetProfileMetadataAsync(pt);
        }
        return (pt, constraint, raw, metadata);
    }

    public async Task<(string ProfileType, string TeamConstraint, string Raw, ProfileMetadata? Metadata)>
        UpdateCurrentJobProfileConfigAsync(Guid regId, string profileType, string teamConstraint)
    {
        var jobData = await _repo.GetJobDataForRegistrationAsync(regId);
        if (jobData == null)
        {
            throw new InvalidOperationException("Current job not found for supplied regId");
        }

        // Build and persist CoreRegformPlayer
        var newCore = BuildCoreRegform(profileType, teamConstraint);

        // Refresh PlayerProfileMetadataJson to match the selected type
        var metadata = await GetProfileMetadataAsync(profileType);
        var metadataJsonToSave = metadata != null
            ? JsonSerializer.Serialize(metadata)
            : jobData.PlayerProfileMetadataJson ?? string.Empty;

        await _repo.UpdateJobCoreRegformAndMetadataAsync(jobData.JobId, newCore, metadataJsonToSave);

        return (profileType, teamConstraint, newCore, metadata);
    }

    /// <summary>
    /// Canonical post-parse corrections to the migrated field set, applied at every parse
    /// chokepoint so a one-time production migrate lands the corrected values.
    ///
    /// The legacy player POCOs encoded the pre-2016 SAT model — separate 200–800 Verbal and
    /// Writing sections. Modern SAT is Evidence-Based Reading &amp; Writing (200–800) plus a
    /// 400–1600 composite Total. We keep the legacy DB columns (SatVerbal / SatWriting) — field
    /// names ARE the physical columns and cannot change — but relabel and re-bound them:
    ///   SatVerbal  → "SAT (Reading &amp; Writing)", 200–800 (label/message only)
    ///   SatWriting → "SAT (Total)",                 400–1600
    /// Matched by DbColumn (case-insensitive); profiles without these fields are untouched.
    /// </summary>
    private static void ApplyCanonicalFieldCorrections(ProfileMetadata metadata)
    {
        foreach (var f in metadata.Fields)
        {
            if (string.Equals(f.DbColumn, "SatVerbal", StringComparison.OrdinalIgnoreCase))
            {
                f.DisplayName = "SAT (Reading & Writing)";
                if (f.Validation != null)
                {
                    f.Validation.Min = 200;
                    f.Validation.Max = 800;
                    f.Validation.Message = "SAT (Reading & Writing) must be between 200 and 800";
                }
            }
            else if (string.Equals(f.DbColumn, "SatWriting", StringComparison.OrdinalIgnoreCase))
            {
                f.DisplayName = "SAT (Total)";
                if (f.Validation != null)
                {
                    f.Validation.Min = 400;
                    f.Validation.Max = 1600;
                    f.Validation.Message = "SAT (Total) must be between 400 and 1600";
                }
            }
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
            foreach (var field in metadata.Fields.Where(f => string.Equals(f.InputType, InputTypeSelect, StringComparison.OrdinalIgnoreCase)))
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
        var ds = dataSource ?? string.Empty;

        // Direct exact, prefix, and contains search using original strings first (fast path)
        var exact = jsonOptions.Keys.FirstOrDefault(k => k.Equals(ds, StringComparison.OrdinalIgnoreCase));
        if (exact != null) return exact;

        var withList = jsonOptions.Keys.FirstOrDefault(k => k.Equals($"List_{ds}", StringComparison.OrdinalIgnoreCase));
        if (withList != null) return withList;

        var contains = jsonOptions.Keys.FirstOrDefault(k => k.Contains(ds, StringComparison.OrdinalIgnoreCase));
        if (contains != null) return contains;

        // Fallback: normalized fuzzy matching using candidate variants
        var candidates = GenerateDataSourceCandidates(ds);
        foreach (var key in jsonOptions.Keys)
        {
            var nk = NormalizeKey(key);
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

    // Normalization helper: lowercase and remove non-alphanumerics for flexible matching
    private static string NormalizeKey(string s)
        => new string((s ?? string.Empty).ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    private static string StripPrefixNormalized(string s, string prefixNorm)
        => s.StartsWith(prefixNorm, StringComparison.Ordinal) ? s.Substring(prefixNorm.Length) : s;

    private static HashSet<string> GenerateDataSourceCandidates(string? dataSource)
    {
        var candidates = new HashSet<string>(StringComparer.Ordinal);
        var ds = dataSource ?? string.Empty;
        var dsNorm = NormalizeKey(ds);
        candidates.Add(dsNorm);

        // Strip common prefixes
        var dsNoList = StripPrefixNormalized(dsNorm, TokenList);
        candidates.Add(dsNoList);

        var dsNoListSizes = StripPrefixNormalized(dsNorm, TokenListSizes);
        candidates.Add(dsNoListSizes);

        // Add prefixed forms
        candidates.Add(TokenList + dsNoList);
        candidates.Add(TokenListSizes + dsNoList);

        // Handle Sizes_ reordering: ListSizes_Jersey <-> List_JerseySizes
        int sizesIdx = dsNorm.IndexOf(TokenSizes, StringComparison.Ordinal);
        if (sizesIdx >= 0)
        {
            var before = dsNorm.Substring(0, sizesIdx);
            var after = dsNorm.Substring(sizesIdx + TokenSizes.Length);
            before = NormalizeKey(before);
            after = NormalizeKey(after);

            if (!string.IsNullOrEmpty(after))
            {
                candidates.Add(before + after + TokenSizes); // listjerseysizes
                candidates.Add(TokenList + after + TokenSizes);
                candidates.Add(after + TokenSizes);
            }
        }

        return candidates;
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


