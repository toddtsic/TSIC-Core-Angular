using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Repositories;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Data.SqlDbContext;

namespace TSIC.Infrastructure.Repositories;

/// <summary>
/// Repository for Agegroups entity using Entity Framework Core.
/// </summary>
public class AgeGroupRepository : IAgeGroupRepository
{
    private readonly SqlDbContext _context;

    public AgeGroupRepository(SqlDbContext context)
    {
        _context = context;
    }

    public async Task<(decimal? TeamFee, decimal? RosterFee)?> GetFeeInfoAsync(Guid ageGroupId, CancellationToken cancellationToken = default)
    {
        var result = await _context.Agegroups
            .AsNoTracking()
            .Where(a => a.AgegroupId == ageGroupId)
            .Select(a => new { a.TeamFee, a.RosterFee })
            .FirstOrDefaultAsync(cancellationToken);

        return result != null ? (result.TeamFee, result.RosterFee) : null;
    }

    public async Task<List<AgeGroupForRegistration>> GetByLeagueAndSeasonAsync(
        Guid leagueId,
        string season,
        CancellationToken cancellationToken = default)
    {
        return await _context.Agegroups
            .AsNoTracking()
            .Where(ag => ag.LeagueId == leagueId && ag.Season == season && ag.MaxTeams > 0)
            .OrderBy(ag => ag.AgegroupName)
            .Select(ag => new AgeGroupForRegistration
            {
                AgegroupId = ag.AgegroupId,
                AgegroupName = ag.AgegroupName ?? string.Empty,
                MaxTeams = ag.MaxTeams,
                TeamFee = ag.TeamFee,
                RosterFee = ag.RosterFee
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<AgeGroupValidationInfo?> GetForValidationAsync(
        Guid ageGroupId,
        CancellationToken cancellationToken = default)
    {
        return await _context.Agegroups
            .AsNoTracking()
            .Where(ag => ag.AgegroupId == ageGroupId)
            .Select(ag => new AgeGroupValidationInfo
            {
                AgegroupId = ag.AgegroupId,
                AgegroupName = ag.AgegroupName,
                MaxTeams = ag.MaxTeams,
                TeamFee = ag.TeamFee,
                RosterFee = ag.RosterFee
            })
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<Agegroups?> GetByIdAsync(Guid ageGroupId, CancellationToken cancellationToken = default)
    {
        return await _context.Agegroups.FindAsync(new object[] { ageGroupId }, cancellationToken);
    }
}
