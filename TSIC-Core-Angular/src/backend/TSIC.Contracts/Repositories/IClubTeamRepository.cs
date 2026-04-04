using TSIC.Domain.Entities;

namespace TSIC.Contracts.Repositories;

/// <summary>
/// Repository for managing ClubTeams entity data access.
/// </summary>
public interface IClubTeamRepository
{
    /// <summary>
    /// Get all ClubTeams for a given club.
    /// </summary>
    Task<List<ClubTeams>> GetByClubIdAsync(
        int clubId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a single ClubTeam by its ID.
    /// </summary>
    Task<ClubTeams?> GetByIdAsync(
        int clubTeamId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Find an existing ClubTeam by identity (club + name + grad year).
    /// Returns the row with the highest LOP if duplicates exist.
    /// </summary>
    Task<ClubTeams?> FindByIdentityAsync(
        int clubId, string clubTeamName, string clubTeamGradYear,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Add new ClubTeam (does NOT call SaveChanges).
    /// </summary>
    void Add(ClubTeams clubTeam);

    /// <summary>
    /// Persist all changes to the database.
    /// </summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
