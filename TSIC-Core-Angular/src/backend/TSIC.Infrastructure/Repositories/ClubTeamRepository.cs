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
        return await _context.ClubTeams
            .Where(ct => ct.ClubId == clubId)
            .AsNoTracking()
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

    public void Add(ClubTeams clubTeam)
    {
        _context.ClubTeams.Add(clubTeam);
    }

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return await _context.SaveChangesAsync(cancellationToken);
    }
}
