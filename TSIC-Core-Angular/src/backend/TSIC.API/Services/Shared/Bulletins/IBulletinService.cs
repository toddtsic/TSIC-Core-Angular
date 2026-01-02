using TSIC.Contracts.Dtos;

namespace TSIC.API.Services.Shared.Bulletins;

/// <summary>
/// Service for managing bulletin business logic including URL translation and token substitution.
/// </summary>
public interface IBulletinService
{
    /// <summary>
    /// Get active bulletins for a job with processed text (token replacement and URL translation).
    /// </summary>
    /// <param name="jobPath">The job path to fetch bulletins for</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of processed bulletin DTOs with translated URLs and replaced tokens</returns>
    Task<List<BulletinDto>> GetActiveBulletinsForJobAsync(string jobPath, CancellationToken cancellationToken = default);
}
