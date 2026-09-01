using TSIC.Domain.Entities;

namespace TSIC.Contracts.Repositories;

public record ClubWithUsageInfo
{
    public required int ClubId { get; init; }
    public required string ClubName { get; init; }
    public required bool IsInUse { get; init; }
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
