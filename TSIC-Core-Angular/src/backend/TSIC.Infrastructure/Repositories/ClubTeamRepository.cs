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
            .Where(ct => ct.ClubId == clubId && ct.Active == true)
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

    public async Task<List<ClubTeamManagementDto>> GetClubTeamsWithMetadataAsync(int clubId, CancellationToken cancellationToken = default)
    {
        // Get all active club teams for this club (filter inactive teams)
        var teams = await _context.ClubTeams
            .Include(ct => ct.Club)
            .Where(ct => ct.ClubId == clubId && ct.Active == true)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        // For each team, check if it's been used
        var result = new List<ClubTeamManagementDto>();
        foreach (var team in teams)
        {
            var hasBeenUsed = await _context.Teams
                .AnyAsync(t => t.ClubTeamId == team.ClubTeamId, cancellationToken);

            result.Add(new ClubTeamManagementDto
            {
                ClubTeamId = team.ClubTeamId,
                ClubId = team.ClubId,
                ClubName = team.Club.ClubName,
                ClubTeamName = team.ClubTeamName,
                ClubTeamGradYear = team.ClubTeamGradYear,
                ClubTeamLevelOfPlay = team.ClubTeamLevelOfPlay,
                IsActive = team.Active ?? false,
                HasBeenUsed = hasBeenUsed,
                HasBeenRegisteredForAnyEvent = hasBeenUsed // Same check - any Teams record means it's been registered
            });
        }

        return result.OrderBy(ct => ct.ClubTeamName).ToList();
    }

    public async Task<bool> HasBeenUsedAsync(int clubTeamId, CancellationToken cancellationToken = default)
    {
        return await _context.Teams
            .AnyAsync(t => t.ClubTeamId == clubTeamId, cancellationToken);
    }

    public async Task<ClubTeams?> GetByIdAsync(int clubTeamId, CancellationToken cancellationToken = default)
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

    public async Task<bool> ExistsByNameExcludingIdAsync(int clubId, string teamName, int excludeClubTeamId, CancellationToken cancellationToken = default)
    {
        return await _context.ClubTeams
            .AnyAsync(ct => ct.ClubId == clubId && ct.ClubTeamName == teamName && ct.ClubTeamId != excludeClubTeamId, cancellationToken);
    }

    public void Add(ClubTeams clubTeam)
    {
        _context.ClubTeams.Add(clubTeam);
    }

    public void Update(ClubTeams clubTeam)
    {
        _context.ClubTeams.Update(clubTeam);
    }

    public void Remove(ClubTeams clubTeam)
    {
        _context.ClubTeams.Remove(clubTeam);
    }

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return await _context.SaveChangesAsync(cancellationToken);
    }
}
