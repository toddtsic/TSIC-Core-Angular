using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Repositories;

namespace TSIC.API.Services.Teams;

public class TeamLookupService : ITeamLookupService
{
    private readonly ITeamRepository _teamRepo;
    private readonly IRegistrationRepository _registrationRepo;
    private readonly IJobRepository _jobRepo;
    private readonly ILogger<TeamLookupService> _logger;

    public TeamLookupService(
        ITeamRepository teamRepo,
        IRegistrationRepository registrationRepo,
        IJobRepository jobRepo,
        ILogger<TeamLookupService> logger)
    {
        _teamRepo = teamRepo;
        _registrationRepo = registrationRepo;
        _jobRepo = jobRepo;
        _logger = logger;
    }

    public async Task<IReadOnlyList<AvailableTeamDto>> GetAvailableTeamsForJobAsync(Guid jobId)
    {
        var jobUsesWaitlists = await _jobRepo.Query()
            .Where(j => j.JobId == jobId)
            .Select(j => j.BUseWaitlists)
            .SingleOrDefaultAsync();

        var teamsRaw = await _teamRepo.GetAvailableTeamsQueryResultsAsync(jobId);

        if (teamsRaw.Count == 0)
        {
            _logger.LogInformation("No self-rostering teams found for job {JobId}", jobId);
            return Array.Empty<AvailableTeamDto>();
        }

        var teamIds = teamsRaw.Select(t => t.TeamId).ToList();
        var rosterCounts = await _registrationRepo.GetRosterCountsByTeamAsync(teamIds);

        var dtos = teamsRaw.Select(t =>
        {
            var current = rosterCounts.TryGetValue(t.TeamId, out var c) ? c : 0;
            var rosterFull = current >= t.MaxCount && t.MaxCount > 0;

            var fee = ComputePerRegistrantFee(
                t.RawPerRegistrantFee,
                t.RawTeamFee,
                t.RawRosterFee,
                t.LeaguePlayerFeeOverride,
                t.AgegroupPlayerFeeOverride);
            var deposit = ComputePerRegistrantDeposit(
                t.RawPerRegistrantDeposit,
                t.RawTeamFee,
                t.RawRosterFee);

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
        var data = await _teamRepo.GetTeamFeeDataAsync(teamId);

        if (data == null)
        {
            _logger.LogInformation("ResolvePerRegistrantAsync: team {TeamId} not found; returning zeros.", teamId);
            return (0m, 0m);
        }

        var fee = ComputePerRegistrantFee(
            data.PerRegistrantFee,
            data.TeamFee,
            data.RosterFee,
            data.LeaguePlayerFeeOverride,
            data.AgegroupPlayerFeeOverride);
        var deposit = ComputePerRegistrantDeposit(
            data.PerRegistrantDeposit,
            data.TeamFee,
            data.RosterFee);
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