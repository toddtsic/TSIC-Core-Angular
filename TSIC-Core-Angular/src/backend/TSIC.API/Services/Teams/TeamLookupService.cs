using TSIC.Contracts.Dtos;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Constants;

namespace TSIC.API.Services.Teams;

public class TeamLookupService : ITeamLookupService
{
    private readonly ITeamRepository _teamRepo;
    private readonly IRegistrationRepository _registrationRepo;
    private readonly IJobRepository _jobRepo;
    private readonly IFeeResolutionService _feeService;
    private readonly ILogger<TeamLookupService> _logger;

    public TeamLookupService(
        ITeamRepository teamRepo,
        IRegistrationRepository registrationRepo,
        IJobRepository jobRepo,
        IFeeResolutionService feeService,
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

        // Batch-resolve player fees for all teams in one query
        var resolvedFees = await _feeService.ResolveFeesByTeamIdsAsync(
            jobId, RoleConstants.Player, teamIds);

        // Evaluate active modifiers per team so the dropdown can show an accurate
        // right-now price (base + late fee − discount), not just the base fee.
        // Sequential awaits — scoped DbContext forbids Task.WhenAll on repo calls.
        var asOf = DateTime.UtcNow;
        var modifiersByTeam = new Dictionary<Guid, ResolvedModifiers>();
        foreach (var t in teamsRaw)
        {
            modifiersByTeam[t.TeamId] = await _feeService.EvaluateModifiersAsync(
                jobId, RoleConstants.Player, t.AgegroupId, t.TeamId, asOf);
        }

        var dtos = teamsRaw.Select(t =>
        {
            var current = rosterCounts.TryGetValue(t.TeamId, out var c) ? c : 0;
            var rosterFull = current >= t.MaxCount && t.MaxCount > 0;

            var resolved = resolvedFees.TryGetValue(t.TeamId, out var rf) ? rf : null;
            var fee = resolved?.EffectiveBalanceDue ?? 0m;
            var deposit = resolved?.EffectiveDeposit ?? 0m;
            // If deposit equals balance due, there's no separate deposit concept
            if (deposit == fee) deposit = 0m;

            var mods = modifiersByTeam.TryGetValue(t.TeamId, out var m) ? m : null;
            var effectiveFee = fee + (mods?.TotalLateFee ?? 0m) - (mods?.TotalDiscount ?? 0m);
            if (effectiveFee < 0m) effectiveFee = 0m;

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
                Fee = fee,
                Deposit = deposit,
                EffectiveFee = effectiveFee,
                JobUsesWaitlists = jobUsesWaitlists,
                WaitlistTeamId = null,
                StartDate = t.StartDate,
                EndDate = t.EndDate,
                PerRegistrantFee = t.PerRegistrantFee,
                ClubName = t.ClubName
            };
        }).ToList();

        // Populate WaitlistTeamId for full teams when job uses waitlists
        if (jobUsesWaitlists)
        {
            var fullTeamNames = dtos
                .Where(d => d.RosterIsFull)
                .Select(d => $"WAITLIST - {d.TeamName}")
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (fullTeamNames.Count > 0)
            {
                // Find existing waitlist team mirrors by name
                var allTeams = await _teamRepo.GetTeamsForJobByNamesAsync(jobId, fullTeamNames);
                var waitlistLookup = allTeams.ToDictionary(
                    t => t.TeamName ?? string.Empty,
                    t => t.TeamId,
                    StringComparer.OrdinalIgnoreCase);

                foreach (var dto in dtos.Where(d => d.RosterIsFull))
                {
                    var wlName = $"WAITLIST - {dto.TeamName}";
                    if (waitlistLookup.TryGetValue(wlName, out var wlTeamId))
                    {
                        dto.WaitlistTeamId = wlTeamId;
                    }
                }
            }
        }

        return dtos;
    }

    public async Task<(decimal Fee, decimal Deposit)> ResolvePerRegistrantAsync(Guid teamId)
    {
        // Get team's job and agegroup context
        var teamContext = await _teamRepo.GetTeamWithFeeContextAsync(teamId);
        if (teamContext == null)
        {
            _logger.LogInformation("ResolvePerRegistrantAsync: team {TeamId} not found; returning zeros.", teamId);
            return (0m, 0m);
        }

        var (team, _) = teamContext.Value;
        var resolved = await _feeService.ResolveFeeAsync(
            team.JobId, RoleConstants.Player, team.AgegroupId, team.TeamId);

        if (resolved == null) return (0m, 0m);

        var fee = resolved.EffectiveBalanceDue;
        var deposit = resolved.EffectiveDeposit;
        if (deposit == fee) deposit = 0m;

        return (fee, deposit);
    }
}
