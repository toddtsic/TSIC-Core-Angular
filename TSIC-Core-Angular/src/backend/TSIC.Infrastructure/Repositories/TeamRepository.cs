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
            .Select(tr => new RegisteredTeamInfo
            {
                TeamId = tr.Team.TeamId,
                TeamName = tr.Team.TeamName ?? string.Empty,
                AgeGroupId = tr.Team.AgegroupId,
                AgeGroupName = tr.Team.Agegroup!.AgegroupName ?? string.Empty,
                LevelOfPlay = tr.Team.LevelOfPlay,
                FeeBase = tr.Team.FeeBase ?? 0,
                FeeProcessing = tr.Team.FeeProcessing ?? 0,
                FeeTotal = (tr.Team.FeeBase ?? 0) + (tr.Team.FeeProcessing ?? 0),
                PaidTotal = tr.Team.PaidTotal ?? 0,
                OwedTotal = ((tr.Team.FeeBase ?? 0) + (tr.Team.FeeProcessing ?? 0)) - (tr.Team.PaidTotal ?? 0),
                // DepositDue: RosterFee - PaidTotal (0 if already paid deposit)
                DepositDue = (tr.Team.PaidTotal >= tr.Team.Agegroup.RosterFee) ? 0 : (tr.Team.Agegroup.RosterFee ?? 0) - (tr.Team.PaidTotal ?? 0),
                // AdditionalDue: TeamFee (0 if already fully paid or if full payment required upfront)
                AdditionalDue = (tr.Team.OwedTotal == 0 && (tr.Team.Job.BTeamsFullPaymentRequired ?? false)) ? 0 : (tr.Team.Agegroup.TeamFee ?? 0),
                RegistrationTs = tr.Team.Createdate,
                BWaiverSigned3 = tr.Registration.BWaiverSigned3
            })
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
            .Select(t => new AvailableTeamQueryResult
            {
                TeamId = t.TeamId,
                Name = t.TeamName ?? t.DisplayName ?? "(Unnamed Team)",
                AgegroupId = t.AgegroupId,
                AgegroupName = t.Agegroup.AgegroupName,
                DivisionId = t.DivId,
                DivisionName = t.Div != null ? t.Div.DivName : null,
                MaxCount = t.MaxCount,
                RawPerRegistrantFee = t.PerRegistrantFee,
                RawPerRegistrantDeposit = t.PerRegistrantDeposit,
                RawTeamFee = t.Agegroup.TeamFee,
                RawRosterFee = t.Agegroup.RosterFee,
                TeamAllowsSelfRostering = t.BAllowSelfRostering,
                AgegroupAllowsSelfRostering = t.Agegroup.BAllowSelfRostering,
                LeaguePlayerFeeOverride = t.League.PlayerFeeOverride,
                AgegroupPlayerFeeOverride = t.Agegroup.PlayerFeeOverride
            })
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
            .Select(t => new TeamFeeData
            {
                PerRegistrantFee = t.PerRegistrantFee,
                PerRegistrantDeposit = t.PerRegistrantDeposit,
                TeamFee = t.Agegroup.TeamFee,
                RosterFee = t.Agegroup.RosterFee,
                LeaguePlayerFeeOverride = t.League.PlayerFeeOverride,
                AgegroupPlayerFeeOverride = t.Agegroup.PlayerFeeOverride
            })
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

    public async Task<List<Teams>> GetTeamsWithJobAndCustomerAsync(
        Guid jobId,
        IReadOnlyCollection<Guid> teamIds,
        CancellationToken cancellationToken = default)
    {
        if (teamIds.Count == 0)
        {
            return new List<Teams>();
        }

        return await _context.Teams
            .Include(t => t.Job)
                .ThenInclude(j => j.Customer)
            .Where(t => t.JobId == jobId && teamIds.Contains(t.TeamId))
            .ToListAsync(cancellationToken);
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
            .Select(t => new RegisteredTeamInfo
            {
                TeamId = t.TeamId,
                TeamName = t.TeamName ?? string.Empty,
                AgeGroupId = t.AgegroupId,
                AgeGroupName = t.Agegroup.AgegroupName ?? string.Empty,
                LevelOfPlay = t.LevelOfPlay,
                FeeBase = t.FeeBase ?? 0,
                FeeProcessing = t.FeeProcessing ?? 0,
                FeeTotal = t.FeeTotal ?? 0,
                PaidTotal = t.PaidTotal ?? 0,
                OwedTotal = t.OwedTotal ?? 0,
                // DepositDue: RosterFee - PaidTotal (0 if already paid deposit)
                DepositDue = (t.PaidTotal >= t.Agegroup.RosterFee) ? 0 : (t.Agegroup.RosterFee ?? 0) - (t.PaidTotal ?? 0),
                // AdditionalDue: TeamFee (0 if already fully paid or if full payment required upfront)
                AdditionalDue = (t.OwedTotal == 0 && (t.Job.BTeamsFullPaymentRequired ?? false)) ? 0 : (t.Agegroup.TeamFee ?? 0),
                RegistrationTs = t.Createdate,
                BWaiverSigned3 = t.ClubrepRegistration.BWaiverSigned3
            })
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public async Task UpdateTeamFeesAsync(List<Teams> teams, CancellationToken cancellationToken = default)
    {
        _context.Teams.UpdateRange(teams);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<List<RegisteredTeamInfo>> GetRegisteredTeamsForUserAndJobAsync(
        Guid jobId,
        string userId,
        CancellationToken cancellationToken = default)
    {
        return await (from t in _context.Teams
                      join ag in _context.Agegroups on t.AgegroupId equals ag.AgegroupId
                      join reg in _context.Registrations on t.ClubrepRegistrationid equals reg.RegistrationId
                      join j in _context.Jobs on t.JobId equals j.JobId
                      where t.JobId == jobId && reg.UserId == userId
                            && t.Active == true
                            && !ag.AgegroupName!.Contains("DROPPED")
                      orderby ag.AgegroupName, t.TeamName
                      select new RegisteredTeamInfo
                      {
                          TeamId = t.TeamId,
                          TeamName = t.TeamName ?? string.Empty,
                          AgeGroupId = ag.AgegroupId,
                          AgeGroupName = ag.AgegroupName ?? string.Empty,
                          LevelOfPlay = t.LevelOfPlay,
                          FeeBase = t.FeeBase ?? 0,
                          FeeProcessing = t.FeeProcessing ?? 0,
                          FeeTotal = (t.FeeBase ?? 0) + (t.FeeProcessing ?? 0),
                          PaidTotal = t.PaidTotal ?? 0,
                          OwedTotal = ((t.FeeBase ?? 0) + (t.FeeProcessing ?? 0)) - (t.PaidTotal ?? 0),
                          DepositDue = (t.PaidTotal >= ag.RosterFee) ? 0 : (ag.RosterFee ?? 0) - (t.PaidTotal ?? 0),
                          AdditionalDue = (t.OwedTotal == 0 && (j.BTeamsFullPaymentRequired ?? false)) ? 0 : (ag.TeamFee ?? 0),
                          RegistrationTs = t.Createdate,
                          BWaiverSigned3 = reg.BWaiverSigned3
                      })
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public async Task<List<TeamWithRegistrationInfo>> GetTeamsByClubExcludingRegistrationAsync(
        Guid jobId,
        int clubId,
        Guid? excludeRegistrationId = null,
        CancellationToken cancellationToken = default)
    {
        var query = from t in _context.Teams
                    join reg in _context.Registrations on t.ClubrepRegistrationid equals reg.RegistrationId
                    where t.JobId == jobId
                      && t.ClubrepRegistrationid != null
                      && _context.ClubReps.Any(cr => cr.ClubRepUserId == reg.UserId && cr.ClubId == clubId)
                    select new TeamWithRegistrationInfo
                    {
                        TeamId = t.TeamId,
                        TeamName = t.TeamName ?? string.Empty,
                        Username = reg.User != null ? reg.User.UserName : null,
                        ClubrepRegistrationid = t.ClubrepRegistrationid
                    };

        if (excludeRegistrationId.HasValue)
        {
            query = query.Where(t => t.ClubrepRegistrationid != excludeRegistrationId.Value);
        }

        return await query
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public async Task<List<HistoricalTeamInfo>> GetHistoricalTeamsForClubAsync(
        string userId,
        string clubName,
        int previousYear,
        CancellationToken cancellationToken = default)
    {
        return await (from t in _context.Teams
                      join j in _context.Jobs on t.JobId equals j.JobId
                      join ag in _context.Agegroups on t.AgegroupId equals ag.AgegroupId
                      join reg in _context.Registrations on t.ClubrepRegistrationid equals reg.RegistrationId
                      where reg.UserId == userId
                        && reg.ClubName == clubName
                        && j.Season == previousYear.ToString()
                        && t.TeamName != null
                      orderby t.TeamName
                      select new HistoricalTeamInfo
                      {
                          TeamId = t.TeamId,
                          TeamName = t.TeamName ?? string.Empty,
                          AgegroupName = ag.AgegroupName,
                          Createdate = t.Createdate
                      })
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public async Task<Dictionary<Guid, int>> GetRegistrationCountsByAgeGroupAsync(
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        return await _context.Teams
            .Where(t => t.JobId == jobId && (t.Active == true))
            .GroupBy(t => t.AgegroupId)
            .Select(g => new { AgegroupId = g.Key, Count = g.Count() })
            .AsNoTracking()
            .ToDictionaryAsync(x => x.AgegroupId, x => x.Count, cancellationToken);
    }

    public async Task<bool> HasTeamsForClubRepAsync(
        string userId,
        int clubId,
        CancellationToken cancellationToken = default)
    {
        return await (from t in _context.Teams
                      join reg in _context.Registrations on t.ClubrepRegistrationid equals reg.RegistrationId
                      where _context.ClubReps.Any(cr => cr.ClubRepUserId == reg.UserId && cr.ClubId == clubId)
                      select t)
            .AnyAsync(cancellationToken);
    }

    public async Task<Guid?> GetTeamJobIdAsync(Guid teamId, CancellationToken cancellationToken = default)
    {
        return await _context.Teams
            .AsNoTracking()
            .Where(t => t.TeamId == teamId)
            .Select(t => (Guid?)t.JobId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<List<Teams>> GetTeamsWithDetailsForJobAsync(
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        return await _context.Teams
            .Include(t => t.Job)
            .Include(t => t.Agegroup)
            .Where(t => t.JobId == jobId)
            .ToListAsync(cancellationToken);
    }

    public async Task<Guid?> GetTeamAgeGroupIdAsync(Guid teamId, CancellationToken cancellationToken = default)
    {
        return await _context.Teams
            .AsNoTracking()
            .Where(t => t.TeamId == teamId)
            .Select(t => (Guid?)t.AgegroupId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    // ── LADT Admin methods ──

    public async Task<List<Teams>> GetByDivisionIdAsync(Guid divId, CancellationToken cancellationToken = default)
    {
        return await _context.Teams
            .AsNoTracking()
            .Where(t => t.DivId == divId)
            .OrderBy(t => t.DivRank)
            .ThenBy(t => t.TeamName)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<Teams>> GetByAgegroupIdAsync(Guid agegroupId, CancellationToken cancellationToken = default)
    {
        return await _context.Teams
            .AsNoTracking()
            .Where(t => t.AgegroupId == agegroupId)
            .OrderBy(t => t.DivRank)
            .ThenBy(t => t.TeamName)
            .ToListAsync(cancellationToken);
    }

    public async Task<Teams?> GetByIdReadOnlyAsync(Guid teamId, CancellationToken cancellationToken = default)
    {
        return await _context.Teams
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.TeamId == teamId, cancellationToken);
    }

    public async Task<int> GetMaxDivRankAsync(Guid divId, CancellationToken cancellationToken = default)
    {
        var maxRank = await _context.Teams
            .AsNoTracking()
            .Where(t => t.DivId == divId)
            .Select(t => (int?)t.DivRank)
            .MaxAsync(cancellationToken);

        return maxRank ?? 0;
    }

    public async Task<bool> HasRosteredPlayersAsync(Guid teamId, CancellationToken cancellationToken = default)
    {
        return await _context.Registrations
            .AsNoTracking()
            .AnyAsync(r => r.AssignedTeamId == teamId && r.BActive == true, cancellationToken);
    }

    public async Task<int> GetPlayerCountAsync(Guid teamId, CancellationToken cancellationToken = default)
    {
        return await _context.Registrations
            .AsNoTracking()
            .CountAsync(r => r.AssignedTeamId == teamId && r.BActive == true, cancellationToken);
    }

    public async Task<Dictionary<Guid, int>> GetPlayerCountsByTeamAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        return await _context.Registrations
            .AsNoTracking()
            .Where(r => r.JobId == jobId && r.BActive == true && r.AssignedTeamId != null)
            .GroupBy(r => r.AssignedTeamId!.Value)
            .Select(g => new { TeamId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TeamId, x => x.Count, cancellationToken);
    }

    public async Task<bool> BelongsToJobAsync(Guid teamId, Guid jobId, CancellationToken cancellationToken = default)
    {
        return await _context.Teams
            .AsNoTracking()
            .AnyAsync(t => t.TeamId == teamId && t.JobId == jobId, cancellationToken);
    }
}

