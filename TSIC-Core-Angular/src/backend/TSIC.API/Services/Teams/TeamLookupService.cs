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
        // Job-level phase baseline — the fallback when a team/agegroup/league JobFees row carries
        // no per-scope BFullPaymentRequired override (ResolveFullPaymentPhase precedence).
        var jobPaymentInfo = await _jobRepo.GetJobPaymentInfoAsync(jobId);
        var jobFullPaymentBaseline = jobPaymentInfo?.BPlayersFullPaymentRequired ?? false;

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
                FeeConfigured = resolved?.FeeConfigured ?? false,
                FullPaymentRequired = ResolvedFee.ResolveFullPaymentPhase(resolved, jobFullPaymentBaseline),
                JobUsesWaitlists = jobUsesWaitlists,
                WaitlistTeamId = null,
                StartDate = t.StartDate,
                EndDate = t.EndDate,
                PerRegistrantFee = t.PerRegistrantFee,
                ClubName = t.ClubName
            };
        }).ToList();

        // A full team is emitted with its REAL TeamId and RosterIsFull=true (the UI badges it
        // "WAITLIST" off that flag). We intentionally do NOT swap the entry to the WAITLIST
        // twin's id: waitlisting now happens ENTIRELY at payment (PaymentService cart-split
        // registers on the real team through reserve/PreSubmit, then moves only seat-gone players
        // to the $0 twin when they pay). Swapping the id here broke two things — a resuming
        // player already rostered on a full team could not match their own team in this list
        // (their real id was gone), and a new full-team pick would carry the twin id straight
        // into registration, re-placing them on the twin at selection (the bug the payment-time
        // split was built to avoid). The real id flows through; the twin is a payment-time concern.
        return dtos;
    }

    public async Task<(decimal Fee, decimal Deposit)> ResolvePerRegistrantAsync(Guid teamId)
    {
        var resolved = await ResolveTeamFeeAsync(teamId, RoleConstants.Player);
        if (resolved is not { FeeConfigured: true }) return (0m, 0m);

        var fee = resolved.EffectiveBalanceDue;
        var deposit = resolved.EffectiveDeposit;
        if (deposit == fee) deposit = 0m;

        return (fee, deposit);
    }

    public async Task<decimal> ResolveFullPriceAsync(Guid teamId, string roleId)
    {
        var resolved = await ResolveTeamFeeAsync(teamId, roleId);
        return resolved is { FeeConfigured: true } ? resolved.FullPrice : 0m;
    }

    /// <summary>
    /// Shared cascade resolve for a team: loads the team's job + agegroup context and
    /// resolves the fee for the given role. Returns null when the team is missing.
    /// </summary>
    private async Task<ResolvedFee?> ResolveTeamFeeAsync(Guid teamId, string roleId)
    {
        var teamContext = await _teamRepo.GetTeamWithFeeContextAsync(teamId);
        if (teamContext == null)
        {
            _logger.LogInformation("ResolveTeamFeeAsync: team {TeamId} not found; returning null.", teamId);
            return null;
        }

        var (team, _) = teamContext.Value;
        return await _feeService.ResolveFeeAsync(team.JobId, roleId, team.AgegroupId, team.TeamId);
    }
}
