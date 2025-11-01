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
            if (metadata.Fields.Any(f => string.IsNullOrEmpty(f.DataSource) && f.InputType == "SELECT"))
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
}
