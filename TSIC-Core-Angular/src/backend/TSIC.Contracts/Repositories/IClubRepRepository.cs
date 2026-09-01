using TSIC.Domain.Entities;

namespace TSIC.Contracts.Repositories;

public record ClubWithUsageInfo
{
    public required int ClubId { get; init; }
    public required string ClubName { get; init; }
    public required bool IsInUse { get; init; }
}

/// <summary>
/// One club a rep is a member of, with the size of its team library. The library count is a
/// raw ClubTeams row count — enough to rank two candidate clubs against each other, NOT the
/// deduped figure the wizard displays.
/// </summary>
public record RepClubLibrary
{
    public required int ClubId { get; init; }
    public required string ClubName { get; init; }
    public required int LibraryTeamCount { get; init; }
}

/// <summary>
/// Repository for managing ClubReps entity data access.
/// </summary>
public interface IClubRepRepository
{
    /// <summary>
    /// Get all clubs for a user with IsInUse flag.
    /// </summary>
    Task<List<ClubWithUsageInfo>> GetClubsForUserAsync(
        string userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lean membership read for club resolution: every club this user reps, with each club's
    /// library size. Deliberately NOT <see cref="GetClubsForUserAsync"/> — that one issues a
    /// per-club scan of Teams⋈Registrations to derive an IsInUse flag this caller has no use
    /// for. One query, no N+1.
    /// </summary>
    Task<List<RepClubLibrary>> GetClubLibrariesForRepAsync(
        string clubRepUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get ClubRep for a specific user and club.
    /// </summary>
    Task<ClubReps?> GetClubRepForUserAndClubAsync(
        string clubRepUserId,
        int clubId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if a club rep already exists for a user and club.
    /// </summary>
    Task<bool> ExistsAsync(
        string clubRepUserId,
        int clubId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Add new club rep (does NOT call SaveChanges).
    /// </summary>
    void Add(ClubReps clubRep);

    /// <summary>
    /// Remove club rep (does NOT call SaveChanges).
    /// </summary>
    void Remove(ClubReps clubRep);

    /// <summary>
    /// Persist all changes to the database.
    /// </summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
