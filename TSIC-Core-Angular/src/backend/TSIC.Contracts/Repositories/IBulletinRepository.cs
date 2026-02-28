using TSIC.Contracts.Dtos;
using TSIC.Contracts.Dtos.Bulletin;
using TSIC.Domain.Entities;

namespace TSIC.Contracts.Repositories;

/// <summary>
/// Repository for managing Bulletins entity data access.
/// </summary>
public interface IBulletinRepository
{
    /// <summary>
    /// Get active bulletins for a job, filtered by date range and sorted by start date descending.
    /// Used by public-facing widget.
    /// </summary>
    Task<List<BulletinDto>> GetActiveBulletinsForJobAsync(
        Guid jobId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get ALL bulletins for a job (no date/active filter). Used by admin editor.
    /// </summary>
    Task<List<BulletinAdminDto>> GetAllBulletinsForJobAsync(
        Guid jobId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a single bulletin by ID (tracked for updates).
    /// </summary>
    Task<Bulletins?> GetByIdAsync(
        Guid bulletinId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Batch update active status for all bulletins in a job.
    /// </summary>
    Task<int> BatchUpdateActiveStatusAsync(
        Guid jobId,
        bool active,
        CancellationToken cancellationToken = default);

    void Add(Bulletins bulletin);
    void Remove(Bulletins bulletin);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
