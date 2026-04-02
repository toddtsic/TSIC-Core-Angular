using TSIC.Contracts.Dtos;
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
    /// Add new ClubTeam (does NOT call SaveChanges).
    /// </summary>
    void Add(ClubTeams clubTeam);

    /// <summary>
    /// Persist all changes to the database.
    /// </summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the full team library for a club, including cross-event history
    /// (W-L-T, goals, standings rank, division) for every event each team entered.
    /// </summary>
    Task<ClubTeamLibraryResponse> GetLibraryWithHistoryAsync(
        int clubId,
        CancellationToken cancellationToken = default);
}
