using TSIC.Domain.Entities;

namespace TSIC.Contracts.Repositories;

public record ClubWithUsageInfo
{
    public required int ClubId { get; init; }
    public required string ClubName { get; init; }
    public required bool IsInUse { get; init; }
}

/// <summary>
/// Which club a Club Rep registration acts for. <see cref="ClubId"/> is 0 when none can be resolved.
/// <see cref="SpannedClubCount"/> is how many clubs the registration's library-linked teams point at
/// (0 when the club came from the fallback); more than 1 is a data fault the caller should flag.
/// <see cref="SurvivesRename"/> is true when the club was found without reading club_name (by a linked
/// team, or because the user reps exactly one club) — so renaming the registration keeps it resolvable.
/// </summary>
public record ClubRepClubResolution
{
    public required int ClubId { get; init; }
    public required int SpannedClubCount { get; init; }
    public bool SurvivesRename { get; init; }
}

/// <summary>
/// Repository for managing ClubReps entity data access.
/// </summary>
public interface IClubRepRepository
{
    /// <summary>
    /// Get all clubs for a user with IsInUse flag (the club has registered teams, so its name is locked).
    /// </summary>
    Task<List<ClubWithUsageInfo>> GetClubsForUserAsync(
        string userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// THE resolver for which club a Club Rep registration acts for — by id, never by the registration's
    /// club_name alone, which a Director or Superuser may rename for the event.
    ///  1. The registration's teams (every status, dropped included) → ClubTeamId → ClubTeams.ClubId;
    ///     the club most of those library teams belong to wins.
    ///  2. No library-linked team yet (a fresh registration): among the clubs the registration's user
    ///     represents, the one whose name matches club_name (set from that list at registration); when
    ///     several of the user's clubs share that name, the one whose teams the user has registered most;
    ///     otherwise the user's only club. The per-event rename is refused unless the result
    ///     <see cref="ClubRepClubResolution.SurvivesRename"/>, so a renamed registration still resolves —
    ///     unless every linked team later leaves it and its user reps several clubs (then 0).
    /// Never returns a club the registration's user does not represent via step 2.
    /// </summary>
    Task<ClubRepClubResolution> ResolveClubForClubRepRegistrationAsync(
        Guid clubRepRegistrationId,
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
