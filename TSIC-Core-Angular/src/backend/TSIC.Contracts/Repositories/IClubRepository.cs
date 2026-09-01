using TSIC.Contracts.Dtos;
using TSIC.Domain.Entities;

namespace TSIC.Contracts.Repositories;

/// <summary>
/// Repository for managing Clubs entity data access.
/// </summary>
public interface IClubRepository
{
    /// <summary>
    /// Jobs that hold at least one team belonging to this club (via Teams.ClubTeamId → ClubTeams),
    /// with job name and team count. Drives the admin club-rename impact preview and the per-job
    /// schedule recompose. Ordered by job name.
    /// </summary>
    Task<List<ClubAffectedJob>> GetJobsWithTeamsForClubAsync(
        int clubId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get club by ID.
    /// </summary>
    Task<Clubs?> GetByIdAsync(
        int clubId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get club by name (case-insensitive exact match if supported by DB collation).
    /// ClubName is NOT unique, so this collapses a set to one row and cannot tell a caller
    /// that it did. Prefer <see cref="GetAllByNameAsync"/> anywhere the difference between
    /// "one club" and "several clubs that share a name" changes what the caller should do —
    /// which is every write.
    /// </summary>
    Task<Clubs?> GetByNameAsync(
        string clubName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Every club carrying this name. Lets the caller decide by result-set SIZE: one is an
    /// answer, several is an ambiguity to refuse rather than a coin to flip.
    /// </summary>
    Task<List<Clubs>> GetAllByNameAsync(
        string clubName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get candidate clubs for search matching, including state and team counts.
    /// </summary>
    Task<List<ClubSearchCandidate>> GetSearchCandidatesAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get candidate clubs for search matching, optionally filtered by state.
    /// </summary>
    Task<List<ClubSearchCandidate>> GetSearchCandidatesAsync(
        string? state,
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
