using System.Security.Claims;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Dtos.Bulletin;

namespace TSIC.API.Services.Shared.Bulletins;

/// <summary>
/// Service for managing bulletin business logic including URL translation and token substitution.
/// </summary>
public interface IBulletinService
{
    /// <summary>
    /// Get active bulletins for a job with processed text (token replacement).
    /// Used by public-facing widget.
    /// </summary>
    Task<List<BulletinDto>> GetActiveBulletinsForJobAsync(string jobPath, ClaimsPrincipal? user, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get ALL bulletins for a job (admin view — no token substitution, no date filter).
    /// </summary>
    Task<List<BulletinAdminDto>> GetAllBulletinsForJobAsync(Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Create a new bulletin.
    /// </summary>
    Task<BulletinAdminDto> CreateBulletinAsync(Guid jobId, string userId, CreateBulletinRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Update an existing bulletin. Verifies bulletin belongs to job.
    /// </summary>
    Task<BulletinAdminDto> UpdateBulletinAsync(Guid bulletinId, Guid jobId, string userId, UpdateBulletinRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete a bulletin. Verifies bulletin belongs to job.
    /// </summary>
    Task<bool> DeleteBulletinAsync(Guid bulletinId, Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Batch update active status for all bulletins in a job.
    /// </summary>
    Task<int> BatchUpdateStatusAsync(Guid jobId, bool active, CancellationToken cancellationToken = default);
}
