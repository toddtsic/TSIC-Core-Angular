using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Repositories;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Data.SqlDbContext;

namespace TSIC.Infrastructure.Repositories;

public class ClubTeamRepository : IClubTeamRepository
{
    private readonly SqlDbContext _context;
    public ClubTeamRepository(SqlDbContext context) { _context = context; }

    public async Task<List<ClubTeamDto>> GetClubTeamsForClubAsync(int clubId, CancellationToken cancellationToken = default)
    {
        return await _context.ClubTeams
            .Where(ct => ct.ClubId == clubId)
            .Select(ct => new ClubTeamDto
            {
                ClubTeamId = ct.ClubTeamId,
                ClubTeamName = ct.ClubTeamName,
                ClubTeamGradYear = ct.ClubTeamGradYear,
                ClubTeamLevelOfPlay = ct.ClubTeamLevelOfPlay
            })
            .OrderBy(ct => ct.ClubTeamName)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public async Task<ClubTeams?> GetByIdAsync(Guid clubTeamId, CancellationToken cancellationToken = default)
    {
        return await _context.ClubTeams
            .Where(ct => ct.ClubTeamId == clubTeamId)
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<bool> ExistsByNameAsync(int clubId, string teamName, CancellationToken cancellationToken = default)
    {
        return await _context.ClubTeams
            .AnyAsync(ct => ct.ClubId == clubId && ct.ClubTeamName == teamName, cancellationToken);
    }

    public void Add(ClubTeams clubTeam)
    {
        _context.ClubTeams.Add(clubTeam);
    }

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return await _context.SaveChangesAsync(cancellationToken);
    }
}
