using TSIC.Domain.Entities;

namespace TSIC.Contracts.Repositories;

/// <summary>
/// Repository for managing Leagues entity data access (LADT admin).
/// </summary>
public interface ILeagueRepository
{
    /// <summary>
    /// Get all leagues for a job via JobLeagues join, with Sport navigation.
    /// </summary>
    Task<List<Leagues>> GetLeaguesByJobIdAsync(Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a single league by ID (tracked for updates).
    /// </summary>
    Task<Leagues?> GetByIdAsync(Guid leagueId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a single league by ID with Sport navigation (read-only).
    /// </summary>
    Task<Leagues?> GetByIdWithSportAsync(Guid leagueId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get JobLeagues entries for a job.
    /// </summary>
    Task<List<JobLeagues>> GetJobLeaguesAsync(Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if a league belongs to a job.
    /// </summary>
    Task<bool> BelongsToJobAsync(Guid leagueId, Guid jobId, CancellationToken cancellationToken = default);

    void Add(Leagues league);
    void Remove(Leagues league);
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
