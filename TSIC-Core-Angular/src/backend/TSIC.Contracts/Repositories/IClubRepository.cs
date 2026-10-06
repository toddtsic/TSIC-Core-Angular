using TSIC.Contracts.Dtos;
using TSIC.Domain.Entities;

namespace TSIC.Contracts.Repositories;

/// <summary>A club's id and library name — the minimum for matching club names in memory.</summary>
public record ClubIdName
{
    public required int ClubId { get; init; }
    public required string ClubName { get; init; }
}

/// <summary>
/// Repository for managing Clubs entity data access.
/// </summary>
public interface IClubRepository
{
    /// <summary>
    /// Every club's id and name, AsNoTracking. Club-name matching is normalized (ClubNameMatcher, an
    /// Application concern), so callers filter this in memory rather than the database matching names.
    /// </summary>
    Task<List<ClubIdName>> GetClubIdNamesAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get club by ID.
    /// </summary>
    Task<Clubs?> GetByIdAsync(
        int clubId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get club by name (case-insensitive exact match if supported by DB collation).
    /// </summary>
    /// <summary>
    /// True only for an EMPTY SHELL club: no reps linked AND no library teams. This is the
    /// sole condition under which the anonymous signup endpoint will attach a caller to an
    /// existing club (<see cref="TSIC.Contracts.Dtos.ClubRepRegistrationRequest.ExistingClubId"/>).
    ///
    /// "No reps" alone is NOT sufficient and must never be used on its own: a club can shed
    /// its last rep through RemoveClubFromRepAsync, whose guard only inspects registered
    /// Teams, not the ClubTeams library. A repless club holding a library would otherwise be
    /// claimable by a stranger who would inherit it. An empty shell has nothing to inherit.
    /// </summary>
    Task<bool> IsUnclaimedEmptyAsync(
        int clubId,
        CancellationToken cancellationToken = default);

    Task<Clubs?> GetByNameAsync(
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
