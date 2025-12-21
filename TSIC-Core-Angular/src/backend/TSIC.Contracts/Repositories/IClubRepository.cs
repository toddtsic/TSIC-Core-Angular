using TSIC.Domain.Entities;

namespace TSIC.Contracts.Repositories;

/// <summary>
/// Repository for managing Clubs entity data access.
/// </summary>
public interface IClubRepository
{
    /// <summary>
    /// Get club by ID.
    /// </summary>
    Task<Clubs?> GetByIdAsync(
        int clubId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get club by name (case-insensitive exact match if supported by DB collation).
    /// </summary>
    Task<Clubs?> GetByNameAsync(
        string clubName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get candidate clubs for search matching, including state and team counts.
    /// </summary>
    Task<List<ClubSearchCandidate>> GetSearchCandidatesAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Add new club (does NOT call SaveChanges).
    /// </summary>
    void Add(Clubs club);

    /// <summary>
    /// Persist all changes to the database.
    /// </summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
