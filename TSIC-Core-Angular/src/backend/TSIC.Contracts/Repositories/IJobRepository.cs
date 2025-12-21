using TSIC.Domain.Entities;

namespace TSIC.Contracts.Repositories;

public record JobPreSubmitMetadata(
    string? PlayerProfileMetadataJson,
    string? JsonOptions,
    string? CoreRegformPlayer);

public record JobPaymentInfo(
    bool? AdnArb,
    int? AdnArbbillingOccurences,
    int? AdnArbintervalLength,
    DateTime? AdnArbstartDate);

public record JobMetadata(
    string? PlayerProfileMetadataJson,
    string? JsonOptions,
    string? CoreRegformPlayer);

/// <summary>
/// Repository for managing Jobs entity data access.
/// </summary>
public interface IJobRepository
{
    /// <summary>
    /// Get a queryable for Job queries
    /// </summary>
    IQueryable<Jobs> Query();

    /// <summary>
    /// Fetch minimal metadata needed for player pre-submit.
    /// </summary>
    Task<JobPreSubmitMetadata?> GetPreSubmitMetadataAsync(Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetch payment configuration for a job (ARB settings).
    /// </summary>
    Task<JobPaymentInfo?> GetJobPaymentInfoAsync(Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetch job metadata fields (PlayerProfileMetadataJson, JsonOptions, CoreRegformPlayer).
    /// </summary>
    Task<JobMetadata?> GetJobMetadataAsync(Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Find job by JobPath (case-insensitive).
    /// </summary>
    Task<Guid?> GetJobIdByPathAsync(string jobPath, CancellationToken cancellationToken = default);
}
