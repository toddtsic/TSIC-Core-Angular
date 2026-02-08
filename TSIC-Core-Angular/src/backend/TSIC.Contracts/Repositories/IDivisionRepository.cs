using TSIC.Domain.Entities;

namespace TSIC.Contracts.Repositories;

/// <summary>
/// Repository for managing Divisions entity data access (LADT admin).
/// </summary>
public interface IDivisionRepository
{
    /// <summary>
    /// Get all divisions for an agegroup.
    /// </summary>
    Task<List<Divisions>> GetByAgegroupIdAsync(Guid agegroupId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a single division by ID (tracked for updates).
    /// </summary>
    Task<Divisions?> GetByIdAsync(Guid divId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a single division by ID (read-only).
    /// </summary>
    Task<Divisions?> GetByIdReadOnlyAsync(Guid divId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if a division has any teams.
    /// </summary>
    Task<bool> HasTeamsAsync(Guid divId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if a division belongs to a job (via Agegroup → League → JobLeagues).
    /// </summary>
    Task<bool> BelongsToJobAsync(Guid divId, Guid jobId, CancellationToken cancellationToken = default);

    void Add(Divisions division);
    void Remove(Divisions division);
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
