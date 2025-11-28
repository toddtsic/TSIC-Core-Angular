using Microsoft.EntityFrameworkCore;
using TSIC.API.DTOs;
using TSIC.Infrastructure.Data.SqlDbContext;

namespace TSIC.API.Services;

public class TeamLookupService : ITeamLookupService
{
    private readonly SqlDbContext _context;
    private readonly ILogger<TeamLookupService> _logger;

    public TeamLookupService(SqlDbContext context, ILogger<TeamLookupService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<IReadOnlyList<AvailableTeamDto>> GetAvailableTeamsForJobAsync(Guid jobId)
    {
        var now = DateTime.UtcNow;

        var jobUsesWaitlists = await _context.Jobs
            .Where(j => j.JobId == jobId)
            .Select(j => j.BUseWaitlists)
            .SingleOrDefaultAsync();

        var baseQuery = _context.Teams
            .AsNoTracking()
            .Include(t => t.Agegroup)
            .Include(t => t.Div)
            .Where(t => t.JobId == jobId)
            .Where(t => (t.Active ?? true))
            .Where(t => (t.BAllowSelfRostering ?? false) || (t.Agegroup.BAllowSelfRostering ?? false))
            .Where(t => (t.Effectiveasofdate == null || t.Effectiveasofdate <= now)
                        && (t.Expireondate == null || t.Expireondate >= now));

        var teamsRaw = await baseQuery
            .Select(t => new
            {
                t.TeamId,
                Name = t.TeamName ?? t.DisplayName ?? "(Unnamed Team)",
                t.AgegroupId,
                AgegroupName = t.Agegroup.AgegroupName,
                DivisionId = t.DivId,
                DivisionName = t.Div != null ? t.Div.DivName : null,
                t.MaxCount,
                RawPerRegistrantFee = t.PerRegistrantFee,
                RawPerRegistrantDeposit = t.PerRegistrantDeposit,
                RawTeamFee = t.Agegroup.TeamFee,
                RawRosterFee = t.Agegroup.RosterFee,
                TeamAllowsSelfRostering = t.BAllowSelfRostering,
                AgegroupAllowsSelfRostering = t.Agegroup.BAllowSelfRostering,
                LeaguePlayerFeeOverride = t.League.PlayerFeeOverride,
                AgegroupPlayerFeeOverride = t.Agegroup.PlayerFeeOverride
            })
            .ToListAsync();

        if (teamsRaw.Count == 0)
        {
            _logger.LogInformation("No self-rostering teams found for job {JobId}", jobId);
            return Array.Empty<AvailableTeamDto>();
        }

        var teamIds = teamsRaw.Select(t => t.TeamId).ToList();
        var rosterCounts = await _context.Registrations
            .Where(r => r.AssignedTeamId != null && teamIds.Contains(r.AssignedTeamId.Value))
            .GroupBy(r => r.AssignedTeamId!.Value)
            .Select(g => new { TeamId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TeamId, x => x.Count);

        var dtos = teamsRaw.Select(t =>
        {
            var current = rosterCounts.TryGetValue(t.TeamId, out var c) ? c : 0;
            var rosterFull = current >= t.MaxCount && t.MaxCount > 0;

            var fee = ComputePerRegistrantFee(t.RawPerRegistrantFee, t.RawTeamFee, t.RawRosterFee, t.LeaguePlayerFeeOverride, t.AgegroupPlayerFeeOverride);
            var deposit = ComputePerRegistrantDeposit(t.RawPerRegistrantDeposit, t.RawTeamFee, t.RawRosterFee);

            return new AvailableTeamDto
            {
                TeamId = t.TeamId,
                TeamName = t.Name,
                AgegroupId = t.AgegroupId,
                AgegroupName = t.AgegroupName,
                DivisionId = t.DivisionId,
                DivisionName = t.DivisionName,
                MaxRosterSize = t.MaxCount,
                CurrentRosterSize = current,
                RosterIsFull = rosterFull,
                TeamAllowsSelfRostering = t.TeamAllowsSelfRostering,
                AgegroupAllowsSelfRostering = t.AgegroupAllowsSelfRostering,
                PerRegistrantFee = fee,
                PerRegistrantDeposit = deposit,
                JobUsesWaitlists = jobUsesWaitlists,
                WaitlistTeamId = null
            };
        }).ToList();

        return dtos;
    }

    public async Task<(decimal Fee, decimal Deposit)> ResolvePerRegistrantAsync(Guid teamId)
    {
        var data = await _context.Teams
            .AsNoTracking()
            .Include(t => t.Agegroup)
            .Where(t => t.TeamId == teamId)
            .Select(t => new
            {
                t.PerRegistrantFee,
                t.PerRegistrantDeposit,
                TeamFee = t.Agegroup.TeamFee,
                RosterFee = t.Agegroup.RosterFee,
                LeaguePlayerFeeOverride = t.League.PlayerFeeOverride,
                AgegroupPlayerFeeOverride = t.Agegroup.PlayerFeeOverride
            })
            .SingleOrDefaultAsync();

        if (data == null)
        {
            _logger.LogInformation("ResolvePerRegistrantAsync: team {TeamId} not found; returning zeros.", teamId);
            return (0m, 0m);
        }

        var fee = ComputePerRegistrantFee(data.PerRegistrantFee, data.TeamFee, data.RosterFee, data.LeaguePlayerFeeOverride, data.AgegroupPlayerFeeOverride);
        var deposit = ComputePerRegistrantDeposit(data.PerRegistrantDeposit, data.TeamFee, data.RosterFee);
        return (fee, deposit);
    }

    private static decimal ComputePerRegistrantFee(decimal? prFee, decimal? agTeamFee, decimal? agRosterFee, decimal? leaguePlayerFeeOverride, decimal? agegroupPlayerFeeOverride)
    {
        if (agegroupPlayerFeeOverride.HasValue && agegroupPlayerFeeOverride.Value > 0m)
        {
            return agegroupPlayerFeeOverride.Value;
        }

        if (leaguePlayerFeeOverride.HasValue && leaguePlayerFeeOverride.Value > 0m)
        {
            return leaguePlayerFeeOverride.Value;
        }

        var fee = prFee ?? 0m;
        var teamFee = agTeamFee ?? 0m;
        var rosterFee = agRosterFee ?? 0m;

        if (fee > 0m) return fee;
        if (teamFee > 0m && rosterFee > 0m) return teamFee;
        if (rosterFee > 0m) return rosterFee;
        return 0m;
    }

    private static decimal ComputePerRegistrantDeposit(decimal? prDeposit, decimal? agTeamFee, decimal? agRosterFee)
    {
        var deposit = prDeposit ?? 0m;
        var teamFee = agTeamFee ?? 0m;
        var rosterFee = agRosterFee ?? 0m;

        if (deposit > 0m) return deposit;
        if (teamFee > 0m && rosterFee > 0m) return rosterFee;
        return 0m;
    }
}