using TSIC.Domain.Entities;

namespace TSIC.Contracts.Repositories;

/// <summary>
/// Repository for profile metadata migration operations on Jobs and Registrations
/// </summary>
public interface IProfileMetadataRepository
{
    // ============ JOBS READ OPERATIONS ============

    /// <summary>
    /// Get all jobs with non-empty CoreRegformPlayer (excluding legacy marker)
    /// </summary>
    Task<List<Jobs>> GetJobsWithCoreRegformPlayerAsync();

    /// <summary>
    /// Get full jobs matching a profile type (for bulk updates)
    /// </summary>
    Task<List<Jobs>> GetJobsByProfileTypeAsync(string profileType);

    /// <summary>
    /// Get basic job info (projection)
    /// </summary>
    Task<JobBasicInfo?> GetJobBasicInfoAsync(Guid jobId);

    /// <summary>
    /// Get job with JsonOptions (for profile editor)
    /// </summary>
    Task<JobWithJsonOptions?> GetJobWithJsonOptionsAsync(Guid jobId);

    /// <summary>
    /// Get jobs for profile summary display
    /// </summary>
    Task<List<JobForProfileSummary>> GetJobsForProfileSummaryAsync();

    /// <summary>
    /// Get jobs for known profile types analysis
    /// </summary>
    Task<List<JobKnownProfileType>> GetJobsForKnownProfileTypesAsync();

    /// <summary>
    /// Get first job with player metadata for a profile type (representative sample)
    /// </summary>
    Task<JobWithPlayerMetadata?> GetJobWithPlayerMetadataAsync(string profileType);

    /// <summary>
    /// Get all PlayerProfileMetadataJson values (for allowed field domain building)
    /// </summary>
    Task<List<string>> GetAllJobsPlayerMetadataJsonAsync();

    /// <summary>
    /// Get CoreRegformPlayer values for next profile type computation
    /// </summary>
    Task<List<string>> GetJobsCoreRegformValuesAsync();

    // ============ JOBS WRITE OPERATIONS ============

    /// <summary>
    /// Update PlayerProfileMetadataJson for a single job
    /// </summary>
    Task UpdateJobPlayerMetadataAsync(Guid jobId, string metadataJson);

    /// <summary>
    /// Bulk update PlayerProfileMetadataJson for multiple jobs
    /// </summary>
    Task UpdateMultipleJobsPlayerMetadataAsync(List<Jobs> jobs);

    /// <summary>
    /// Update CoreRegformPlayer and PlayerProfileMetadataJson atomically
    /// </summary>
    Task UpdateJobCoreRegformAndMetadataAsync(Guid jobId, string coreRegformPlayer, string metadataJson);

    /// <summary>
    /// Update JsonOptions for a job
    /// </summary>
    Task UpdateJobJsonOptionsAsync(Guid jobId, string jsonOptions);

    // ============ REGISTRATIONS READ OPERATIONS ============

    /// <summary>
    /// Get registration with Job navigation included
    /// </summary>
    Task<Registrations?> GetRegistrationWithJobAsync(Guid regId);

    /// <summary>
    /// Get registration JobId only (lightweight projection)
    /// </summary>
    Task<Guid?> GetRegistrationJobIdAsync(Guid regId);

    /// <summary>
    /// Get distinct non-empty values from a Registration column (dynamic dropdown options)
    /// </summary>
    Task<List<string>> GetDistinctRegistrationColumnValuesAsync(Guid jobId, string columnName);
}

// ============ RESULT RECORDS ============

public record JobBasicInfo(
    string JobName,
    string? CustomerName,
    string? CoreRegformPlayer,
    string? PlayerProfileMetadataJson,
    string? AdultProfileMetadataJson);

public record JobWithJsonOptions(
    Guid JobId,
    string JobName,
    string? CustomerName,
    string? JsonOptions);

public record JobForProfileSummary(
    Guid JobId,
    string JobName,
    string? CoreRegformPlayer,
    string? PlayerProfileMetadataJson);

public record JobKnownProfileType(
    string? CoreRegformPlayer);

public record JobWithPlayerMetadata(
    string? PlayerProfileMetadataJson);
