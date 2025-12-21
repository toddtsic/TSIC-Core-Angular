using TSIC.Domain.Entities;

namespace TSIC.Contracts.Repositories;

/// <summary>
/// Repository for managing JobLeagues entity data access.
/// </summary>
public interface IJobLeagueRepository
{
    /// <summary>
    /// Get the primary league for a job (Primary=true).
    /// </summary>
    Task<JobLeagues?> GetPrimaryLeagueForJobAsync(
        Guid jobId,
        CancellationToken cancellationToken = default);
}
