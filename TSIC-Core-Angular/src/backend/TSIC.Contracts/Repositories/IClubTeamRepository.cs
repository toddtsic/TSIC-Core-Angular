using TSIC.Contracts.Dtos;
using TSIC.Domain.Entities;

namespace TSIC.Contracts.Repositories;

public interface IClubTeamRepository
{
    Task<List<ClubTeamDto>> GetClubTeamsForClubAsync(int clubId, CancellationToken cancellationToken = default);
    Task<ClubTeams?> GetByIdAsync(Guid clubTeamId, CancellationToken cancellationToken = default);
    Task<bool> ExistsByNameAsync(int clubId, string teamName, CancellationToken cancellationToken = default);
    void Add(ClubTeams clubTeam);
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
