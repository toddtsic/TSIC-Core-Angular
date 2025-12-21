using TSIC.Contracts.Dtos;
using TSIC.Domain.Entities;

namespace TSIC.Contracts.Repositories;

public interface IClubTeamRepository
{
    /// <summary>
    /// Get active club teams for a club (used for registration)
    /// </summary>
    Task<List<ClubTeamDto>> GetClubTeamsForClubAsync(int clubId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all club teams with metadata for management (active + inactive)
    /// </summary>
    Task<List<ClubTeamManagementDto>> GetClubTeamsWithMetadataAsync(int clubId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if a club team has been used in any event registration
    /// </summary>
    Task<bool> HasBeenUsedAsync(int clubTeamId, CancellationToken cancellationToken = default);

    Task<ClubTeams?> GetByIdAsync(int clubTeamId, CancellationToken cancellationToken = default);
    Task<bool> ExistsByNameAsync(int clubId, string teamName, CancellationToken cancellationToken = default);
    void Add(ClubTeams clubTeam);
    void Remove(ClubTeams clubTeam);
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
