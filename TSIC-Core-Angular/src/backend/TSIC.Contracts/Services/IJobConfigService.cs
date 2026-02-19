using TSIC.Contracts.Dtos.JobConfig;

namespace TSIC.Contracts.Services;

/// <summary>
/// Service for managing job configuration (SuperUser editor).
/// </summary>
public interface IJobConfigService
{
    /// <summary>
    /// Get the full job configuration for the editor.
    /// </summary>
    Task<JobConfigDto?> GetConfigAsync(Guid jobId, CancellationToken ct = default);

    /// <summary>
    /// Get lookup data (JobTypes, Sports, BillingTypes) for the editor dropdowns.
    /// </summary>
    Task<JobConfigLookupsDto> GetLookupsAsync(CancellationToken ct = default);

    /// <summary>
    /// Update job configuration. Returns the updated DTO, or null if concurrency conflict.
    /// </summary>
    Task<JobConfigDto?> UpdateConfigAsync(Guid jobId, UpdateJobConfigRequest request, string userId, CancellationToken ct = default);
}
