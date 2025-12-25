using TSIC.Contracts.Dtos;

namespace TSIC.Contracts.Repositories;

/// <summary>
/// Repository for managing Bulletins entity data access.
/// </summary>
public interface IBulletinRepository
{
    /// <summary>
    /// Get active bulletins for a job, filtered by date range and sorted by start date descending
    /// </summary>
    /// <param name="jobId">The job ID to filter bulletins</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of active bulletin DTOs</returns>
    Task<List<BulletinDto>> GetActiveBulletinsForJobAsync(
        Guid jobId,
        CancellationToken cancellationToken = default);
}
