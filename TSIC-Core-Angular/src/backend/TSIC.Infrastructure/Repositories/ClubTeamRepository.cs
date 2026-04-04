using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Repositories;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Data.SqlDbContext;

namespace TSIC.Infrastructure.Repositories;

/// <summary>
/// Repository for ClubTeams entity using Entity Framework Core.
/// </summary>
public class ClubTeamRepository : IClubTeamRepository
{
    private readonly SqlDbContext _context;

    public ClubTeamRepository(SqlDbContext context)
    {
        _context = context;
    }

    public async Task<List<ClubTeams>> GetByClubIdAsync(
        int clubId,
        CancellationToken cancellationToken = default)
    {
        // Deduplicate: same team entered at different LOPs across events creates separate rows.
        // Group by identity (ClubId + Name + GradYear), take the row with the highest LOP.
        return await _context.ClubTeams
            .Where(ct => ct.ClubId == clubId)
            .AsNoTracking()
            .GroupBy(ct => new { ct.ClubId, ct.ClubTeamName, ct.ClubTeamGradYear })
            .Select(g => g.OrderByDescending(ct => ct.ClubTeamLevelOfPlay).First())
            .ToListAsync(cancellationToken);
    }

    public async Task<ClubTeams?> GetByIdAsync(
        int clubTeamId,
        CancellationToken cancellationToken = default)
    {
        return await _context.ClubTeams
            .Where(ct => ct.ClubTeamId == clubTeamId)
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<ClubTeams?> FindByIdentityAsync(
        int clubId, string clubTeamName, string clubTeamGradYear,
        CancellationToken cancellationToken = default)
    {
        return await _context.ClubTeams
            .Where(ct => ct.ClubId == clubId
                && ct.ClubTeamName == clubTeamName
                && ct.ClubTeamGradYear == clubTeamGradYear)
            .OrderByDescending(ct => ct.ClubTeamLevelOfPlay)
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken);
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
