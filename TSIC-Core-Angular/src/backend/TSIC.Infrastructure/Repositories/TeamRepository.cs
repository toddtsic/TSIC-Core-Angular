using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Repositories;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Data.SqlDbContext;

namespace TSIC.Infrastructure.Repositories;

/// <summary>
/// Repository for Teams entity using Entity Framework Core.
/// </summary>
public class TeamRepository : ITeamRepository
{
    private readonly SqlDbContext _context;

    public TeamRepository(SqlDbContext context)
    {
        _context = context;
    }

    public IQueryable<Teams> Query()
    {
        return _context.Teams.AsQueryable();
    }

    public async Task<List<Teams>> GetTeamsForJobAsync(Guid jobId, IReadOnlyCollection<Guid> teamIds, CancellationToken cancellationToken = default)
    {
        return await _context.Teams
            .Where(t => t.JobId == jobId && teamIds.Contains(t.TeamId))
            .ToListAsync(cancellationToken);
    }

    public async Task<(decimal? FeeBase, decimal? PerRegistrantFee)> GetTeamFeeInfoAsync(Guid teamId, CancellationToken cancellationToken = default)
    {
        var data = await _context.Teams
            .Where(t => t.TeamId == teamId)
            .Select(t => new { t.FeeBase, t.PerRegistrantFee })
            .FirstOrDefaultAsync(cancellationToken);

        return data == null
            ? (null, null)
            : (data.FeeBase, data.PerRegistrantFee);
    }

    public async Task<List<RegisteredTeamInfo>> GetRegisteredTeamsForClubAndJobAsync(
        Guid jobId,
        int clubId,
        CancellationToken cancellationToken = default)
    {
        return await _context.Teams
            .Where(t => t.JobId == jobId
                     && t.ClubrepRegistration != null
                     && t.ClubrepRegistration.ClubReps.Any(cr => cr.ClubId == clubId))
            .Select(t => new RegisteredTeamInfo(
                t.TeamId,
                t.ClubTeamId ?? Guid.Empty,
                t.ClubTeam != null ? t.ClubTeam.ClubTeamName! : t.TeamName ?? string.Empty,
                t.ClubTeam != null ? t.ClubTeam.ClubTeamGradYear : t.Agegroup!.AgegroupName ?? string.Empty,
                t.ClubTeam != null ? t.ClubTeam.ClubTeamLevelOfPlay : t.LevelOfPlay,
                t.AgegroupId,
                t.Agegroup!.AgegroupName ?? string.Empty,
                t.FeeBase ?? 0,
                t.FeeProcessing ?? 0,
                (t.FeeBase ?? 0) + (t.FeeProcessing ?? 0),
                t.PaidTotal ?? 0,
                ((t.FeeBase ?? 0) + (t.FeeProcessing ?? 0)) - (t.PaidTotal ?? 0)
            ))
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public async Task<int> GetRegisteredCountForAgegroupAsync(
        Guid jobId,
        Guid agegroupId,
        CancellationToken cancellationToken = default)
    {
        return await _context.Teams
            .CountAsync(t => t.JobId == jobId && t.AgegroupId == agegroupId, cancellationToken);
    }

    public async Task<Teams?> GetTeamWithDetailsAsync(
        Guid teamId,
        CancellationToken cancellationToken = default)
    {
        return await _context.Teams
            .Where(t => t.TeamId == teamId)
            .Include(t => t.ClubTeam)
            .Include(t => t.Agegroup)
            .SingleOrDefaultAsync(cancellationToken);
    }

    public void Add(Teams team)
    {
        _context.Teams.Add(team);
    }

    public void Remove(Teams team)
    {
        _context.Teams.Remove(team);
    }

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<List<AvailableTeamQueryResult>> GetAvailableTeamsQueryResultsAsync(
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        return await _context.Teams
            .AsNoTracking()
            .Include(t => t.Agegroup)
            .Include(t => t.Div)
            .Where(t => t.JobId == jobId)
            .Where(t => (t.Active ?? true))
            .Where(t => (t.BAllowSelfRostering ?? false) || (t.Agegroup.BAllowSelfRostering ?? false))
            .Where(t => (t.Effectiveasofdate == null || t.Effectiveasofdate <= now)
                        && (t.Expireondate == null || t.Expireondate >= now))
            .Select(t => new AvailableTeamQueryResult(
                t.TeamId,
                t.TeamName ?? t.DisplayName ?? "(Unnamed Team)",
                t.AgegroupId,
                t.Agegroup.AgegroupName,
                t.DivId,
                t.Div != null ? t.Div.DivName : null,
                t.MaxCount,
                t.PerRegistrantFee,
                t.PerRegistrantDeposit,
                t.Agegroup.TeamFee,
                t.Agegroup.RosterFee,
                t.BAllowSelfRostering,
                t.Agegroup.BAllowSelfRostering,
                t.League.PlayerFeeOverride,
                t.Agegroup.PlayerFeeOverride
            ))
            .ToListAsync(cancellationToken);
    }

    public async Task<TeamFeeData?> GetTeamFeeDataAsync(
        Guid teamId,
        CancellationToken cancellationToken = default)
    {
        return await _context.Teams
            .AsNoTracking()
            .Include(t => t.Agegroup)
            .Where(t => t.TeamId == teamId)
            .Select(t => new TeamFeeData(
                t.PerRegistrantFee,
                t.PerRegistrantDeposit,
                t.Agegroup.TeamFee,
                t.Agegroup.RosterFee,
                t.League.PlayerFeeOverride,
                t.Agegroup.PlayerFeeOverride
            ))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<Dictionary<Guid, string>> GetTeamNameMapAsync(
        Guid jobId,
        IReadOnlyCollection<Guid> teamIds,
        CancellationToken cancellationToken = default)
    {
        if (teamIds.Count == 0) return new Dictionary<Guid, string>();

        return await _context.Teams
            .Where(t => t.JobId == jobId && teamIds.Contains(t.TeamId))
            .ToDictionaryAsync(t => t.TeamId, t => t.TeamName ?? string.Empty, cancellationToken);
    }
}

