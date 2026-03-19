using TSIC.Contracts.Dtos;
using TSIC.Contracts.Repositories;
using TSIC.API.Services.Players;

namespace TSIC.API.Services.Teams;

public class TeamLookupService : ITeamLookupService
{
    private readonly ITeamRepository _teamRepo;
    private readonly IRegistrationRepository _registrationRepo;
    private readonly IJobRepository _jobRepo;
    private readonly IPlayerRegistrationFeeService _feeService;
    private readonly ILogger<TeamLookupService> _logger;

    public TeamLookupService(
        ITeamRepository teamRepo,
        IRegistrationRepository registrationRepo,
        IJobRepository jobRepo,
        IPlayerRegistrationFeeService feeService,
        ILogger<TeamLookupService> logger)
    {
        _teamRepo = teamRepo;
        _registrationRepo = registrationRepo;
        _jobRepo = jobRepo;
        _feeService = feeService;
        _logger = logger;
    }

    public async Task<IReadOnlyList<AvailableTeamDto>> GetAvailableTeamsForJobAsync(Guid jobId)
    {
        var jobUsesWaitlists = await _jobRepo.GetUsesWaitlistsAsync(jobId);

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

            // Delegate fee resolution to the unified single source of truth
            var fee = _feeService.ResolveBaseFee(new TeamFeeData
            {
                PerRegistrantFee = t.RawPerRegistrantFee,
                TeamFee = t.RawTeamFee,
                RosterFee = t.RawRosterFee,
                LeaguePlayerFeeOverride = t.LeaguePlayerFeeOverride,
                AgegroupPlayerFeeOverride = t.AgegroupPlayerFeeOverride,
                JobTypeId = t.JobTypeId
            });
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

        var fee = _feeService.ResolveBaseFee(data);
        var deposit = ComputePerRegistrantDeposit(
            data.PerRegistrantDeposit,
            data.TeamFee,
            data.RosterFee);
        return (fee, deposit);
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
