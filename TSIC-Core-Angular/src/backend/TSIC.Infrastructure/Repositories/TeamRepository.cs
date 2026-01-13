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
        // Join Teams → Registrations → ClubReps to filter by ClubId
        return await _context.Teams
            .Where(t => t.JobId == jobId && t.ClubrepRegistrationid != null)
            .Join(_context.Registrations,
                t => t.ClubrepRegistrationid,
                reg => reg.RegistrationId,
                (t, reg) => new { Team = t, Registration = reg })
            .Where(tr => _context.ClubReps.Any(cr => cr.ClubRepUserId == tr.Registration.UserId && cr.ClubId == clubId))
            .Select(tr => new RegisteredTeamInfo(
                tr.Team.TeamId,
                tr.Team.TeamName ?? string.Empty,
                tr.Team.AgegroupId,
                tr.Team.Agegroup!.AgegroupName ?? string.Empty,
                tr.Team.LevelOfPlay,
                tr.Team.FeeBase ?? 0,
                tr.Team.FeeProcessing ?? 0,
                (tr.Team.FeeBase ?? 0) + (tr.Team.FeeProcessing ?? 0),
                tr.Team.PaidTotal ?? 0,
                ((tr.Team.FeeBase ?? 0) + (tr.Team.FeeProcessing ?? 0)) - (tr.Team.PaidTotal ?? 0),
                // DepositDue: RosterFee - PaidTotal (0 if already paid deposit)
                (tr.Team.PaidTotal >= tr.Team.Agegroup.RosterFee) ? 0 : (tr.Team.Agegroup.RosterFee ?? 0) - (tr.Team.PaidTotal ?? 0),
                // AdditionalDue: TeamFee (0 if already fully paid or if full payment required upfront)
                (tr.Team.OwedTotal == 0 && (tr.Team.Job.BTeamsFullPaymentRequired ?? false)) ? 0 : (tr.Team.Agegroup.TeamFee ?? 0),
                tr.Team.Createdate,
                tr.Registration.BWaiverSigned3
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

    public async Task<Teams?> GetTeamFromTeamId(
        Guid teamId,
        CancellationToken cancellationToken = default)
    {
        return await _context.Teams.FindAsync(new object[] { teamId }, cancellationToken);
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

    public async Task<List<RegisteredTeamInfo>> GetRegisteredTeamsForPaymentAsync(
        Guid jobId,
        Guid clubRepRegId,
        CancellationToken cancellationToken = default)
    {
        return await _context.Teams
            .Where(t => t.JobId == jobId && t.ClubrepRegistrationid == clubRepRegId)
            .Where(t => t.Active == true)
            .Where(t => (t.FeeTotal ?? 0) > 0)
            .Where(t => string.IsNullOrEmpty(t.ViPolicyId))
            .Include(t => t.Agegroup)
            .Include(t => t.Job)
            .Include(t => t.ClubrepRegistration)
            .Select(t => new RegisteredTeamInfo(
                t.TeamId,
                t.TeamName ?? string.Empty,
                t.AgegroupId,
                t.Agegroup.AgegroupName ?? string.Empty,
                t.LevelOfPlay,
                t.FeeBase ?? 0,
                t.FeeProcessing ?? 0,
                t.FeeTotal ?? 0,
                t.PaidTotal ?? 0,
                t.OwedTotal ?? 0,
                // DepositDue: RosterFee - PaidTotal (0 if already paid deposit)
                (t.PaidTotal >= t.Agegroup.RosterFee) ? 0 : (t.Agegroup.RosterFee ?? 0) - (t.PaidTotal ?? 0),
                // AdditionalDue: TeamFee (0 if already fully paid or if full payment required upfront)
                (t.OwedTotal == 0 && (t.Job.BTeamsFullPaymentRequired ?? false)) ? 0 : (t.Agegroup.TeamFee ?? 0),
                t.Createdate,
                t.ClubrepRegistration.BWaiverSigned3
            ))
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }
}

