using TSIC.Contracts.Dtos.DdlOptions;

namespace TSIC.Contracts.Services;

/// <summary>
/// Service for managing per-job dropdown list options (Jobs.JsonOptions).
/// </summary>
public interface IDdlOptionsService
{
    /// <summary>
    /// Get all 20 dropdown categories for the specified job.
    /// Returns empty lists if the job has no configured options.
    /// </summary>
    Task<JobDdlOptionsDto> GetOptionsAsync(Guid jobId, CancellationToken ct = default);

    /// <summary>
    /// Save all 20 dropdown categories. Sanitizes input (trim, dedup, remove blanks).
    /// </summary>
    Task SaveOptionsAsync(Guid jobId, JobDdlOptionsDto dto, CancellationToken ct = default);
}
