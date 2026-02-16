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
    /// Get all jobs with PlayerProfileMetadataJson (for SQL export)
    /// </summary>
    Task<List<Jobs>> GetJobsWithProfileMetadataAsync();

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
    /// Get Job data for a registration as a projected DTO (AsNoTracking). No entity loading.
    /// </summary>
    Task<RegistrationJobProjection?> GetJobDataForRegistrationAsync(Guid regId);

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

public record JobBasicInfo
{
    public required string JobName { get; init; }
    public string? CustomerName { get; init; }
    public string? CoreRegformPlayer { get; init; }
    public string? PlayerProfileMetadataJson { get; init; }
    public string? AdultProfileMetadataJson { get; init; }
}

public record JobWithJsonOptions
{
    public required Guid JobId { get; init; }
    public required string JobName { get; init; }
    public string? CustomerName { get; init; }
    public string? JsonOptions { get; init; }
}

public record JobForProfileSummary
{
    public required Guid JobId { get; init; }
    public required string JobName { get; init; }
    public string? CoreRegformPlayer { get; init; }
    public string? PlayerProfileMetadataJson { get; init; }
}

public record JobKnownProfileType
{
    public string? CoreRegformPlayer { get; init; }
}

public record JobWithPlayerMetadata
{
    public string? PlayerProfileMetadataJson { get; init; }
}

public record RegistrationJobProjection
{
    public required Guid JobId { get; init; }
    public string? JobName { get; init; }
    public string? CoreRegformPlayer { get; init; }
    public string? JsonOptions { get; init; }
    public string? PlayerProfileMetadataJson { get; init; }
}
