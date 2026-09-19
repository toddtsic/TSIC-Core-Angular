using TSIC.Contracts.Dtos.PoolAssignment;
using TSIC.Contracts.Dtos.Teams;
using TSIC.Contracts.Payments;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Constants;
using Entities = TSIC.Domain.Entities;

namespace TSIC.API.Services.Admin;

/// <summary>
/// Service for the Pool Assignment admin tool.
/// Handles team transfers between divisions with:
/// - Fee recalculation when agegroup changes
/// - Club rep financial sync
/// - Schedule-aware symmetrical swap enforcement
/// - Auto-deactivation for "Dropped Teams" divisions
/// - DivRank renumbering
/// </summary>
public sealed class PoolAssignmentService : IPoolAssignmentService
{
    private readonly ITeamRepository _teamRepo;
    private readonly IDivisionRepository _divRepo;
    private readonly IScheduleRepository _scheduleRepo;
    private readonly IRegistrationRepository _registrationRepo;
    private readonly IAgeGroupRepository _agegroupRepo;
    private readonly IFeeResolutionService _feeService;
    private readonly IPaymentStateService _paymentState;
    private readonly ITeamSeatingService _teamSeating;
    private readonly ILeagueRepository _leagueRepo;

    public PoolAssignmentService(
        ITeamRepository teamRepo,
        IDivisionRepository divRepo,
        IScheduleRepository scheduleRepo,
        IRegistrationRepository registrationRepo,
        IAgeGroupRepository agegroupRepo,
        IFeeResolutionService feeService,
        IPaymentStateService paymentState,
        ITeamSeatingService teamSeating,
        ILeagueRepository leagueRepo)
    {
        _teamRepo = teamRepo;
        _divRepo = divRepo;
        _scheduleRepo = scheduleRepo;
        _registrationRepo = registrationRepo;
        _agegroupRepo = agegroupRepo;
        _feeService = feeService;
        _paymentState = paymentState;
        _teamSeating = teamSeating;
        _leagueRepo = leagueRepo;
    }

    public async Task<List<PoolDivisionOptionDto>> GetDivisionOptionsAsync(Guid jobId, CancellationToken ct = default)
    {
        return await _divRepo.GetPoolAssignmentOptionsAsync(jobId, ct);
    }

    public async Task<List<PoolTeamDto>> GetTeamsAsync(Guid divId, Guid jobId, CancellationToken ct = default)
    {
        if (!await _divRepo.BelongsToJobAsync(divId, jobId, ct))
            throw new ArgumentException("Division does not belong to this job.");

        return await _teamRepo.GetPoolAssignmentTeamsAsync(divId, jobId, ct);
    }

    public async Task<PoolTransferPreviewResponse> PreviewTransferAsync(
        Guid jobId, PoolTransferPreviewRequest request, CancellationToken ct = default)
    {
        if (request.SourceDivId == request.TargetDivId)
            throw new ArgumentException("Source and target divisions must be different.");

        var sourceDivision = await _divRepo.GetByIdReadOnlyAsync(request.SourceDivId, ct)
            ?? throw new ArgumentException("Source division not found.");
        var targetDivision = await _divRepo.GetByIdReadOnlyAsync(request.TargetDivId, ct)
            ?? throw new ArgumentException("Target division not found.");

        // The SAME gate the execute path runs. Previously this method validated nothing and
        // happily rendered a move the executor then refused on confirm.
        await EnsurePoolMovementAllowedAsync(
            jobId, sourceDivision, targetDivision,
            request.SourceTeamIds.Count, request.TargetTeamIds.Count, ct);

        var sourcePoolScheduled = await _scheduleRepo.IsPoolScheduledAsync(request.SourceDivId, jobId, ct);
        var targetPoolScheduled = await _scheduleRepo.IsPoolScheduledAsync(request.TargetDivId, jobId, ct);
        bool agegroupChanges = sourceDivision.AgegroupId != targetDivision.AgegroupId;

        // Load agegroup context for fee calculations
        var sourceAgegroup = await _agegroupRepo.GetByIdAsync(sourceDivision.AgegroupId, ct);
        var targetAgegroup = agegroupChanges
            ? await _agegroupRepo.GetByIdAsync(targetDivision.AgegroupId, ct)
            : sourceAgegroup;

        // Load a source team to get Job context for fee calculation
        Entities.Jobs? job = null;
        if (agegroupChanges && request.SourceTeamIds.Count > 0)
        {
            var sampleTeams = await _teamRepo.GetTeamsForPoolTransferAsync(
                new List<Guid> { request.SourceTeamIds[0] }, jobId, ct);
            if (sampleTeams.Count > 0) job = sampleTeams[0].Job;
        }

        var previews = new List<PoolTransferPreviewDto>();
        var affectedClubRepIds = new HashSet<Guid>();
        bool hasScheduledTeams = false;
        // Always false now, and kept only so the contract does not change shape mid-release. The
        // gate above THROWS on a move that would need a counter-team, so a preview that returns at
        // all is already a permitted move. Nothing downstream should branch on it.
        const bool requiresSymmetrical = false;

        // Load all teams at once (more efficient than one-by-one)
        var allSourceTeams = await _teamRepo.GetTeamsForPoolTransferAsync(request.SourceTeamIds, jobId, ct);
        var allTargetTeams = request.IsSymmetricalSwap && request.TargetTeamIds.Count > 0
            ? await _teamRepo.GetTeamsForPoolTransferAsync(request.TargetTeamIds, jobId, ct)
            : new List<Entities.Teams>();

        // Set job from first available team if not already set
        job ??= allSourceTeams.FirstOrDefault()?.Job ?? allTargetTeams.FirstOrDefault()?.Job;

        // Same arrival check the executor runs, for the same reason the pool gate is run here:
        // a preview that renders is a promise the Confirm can be kept.
        EnsureArrivalsCanBeSeated(
            allSourceTeams, targetPoolScheduled, IsDroppedTeams(sourceAgegroup),
            string.IsNullOrWhiteSpace(targetDivision.DivName) ? "the target pool" : targetDivision.DivName);
        EnsureArrivalsCanBeSeated(
            allTargetTeams, sourcePoolScheduled, IsDroppedTeams(targetAgegroup),
            string.IsNullOrWhiteSpace(sourceDivision.DivName) ? "the source pool" : sourceDivision.DivName);

        // Preview source teams → target division
        foreach (var team in allSourceTeams)
        {
            // Pool state, not team state — a team in a scheduled pool is frozen whether or not
            // any game row happens to carry its id.
            var isScheduled = sourcePoolScheduled;
            if (isScheduled) hasScheduledTeams = true;

            decimal newFeeBase = team.FeeBase ?? 0m;
            decimal newFeeTotal = team.FeeTotal ?? 0m;
            decimal feeDelta = 0m;
            string? warning = null;

            if (agegroupChanges && targetAgegroup != null && job != null)
            {
                // Team tier included (matched on TeamId alone): the executed move repoints the
                // team's own fee row onto the target agegroup, so a team with team-level pricing
                // keeps it. Resolving the agegroup tier only would preview a price the move does
                // not produce.
                var resolved = await _feeService.ResolveFeeForTeamAtAgegroupAsync(
                    job.JobId, RoleConstants.ClubRep, targetAgegroup.AgegroupId, team.TeamId, ct);
                var deposit = resolved?.EffectiveDeposit ?? 0m;
                newFeeBase = ResolvedFee.ResolveFullPaymentPhase(resolved)
                    ? (resolved?.FullPrice ?? 0m) : deposit;
                // Derived through FeeMath — the SAME formula RecalcTotals stamps on execute
                // (ApplyTeamSwapFees) — so the director approves the number the move produces.
                // Execute resolves through this same ResolveFeeForTeamAtAgegroupAsync, so preview
                // and stamp now share the resolver too, not just the arithmetic.
                // The move freezes the team's modifiers, so they carry forward onto the new base;
                // FeeProcessing carries forward too (execute re-derives it, so proc is the one term
                // still estimated). Previously this was `newFeeTotal = newFeeBase`, which silently
                // dropped processing, BOTH discounts, the late fee and the donation.
                newFeeTotal = FeeMath.ComputeFeeTotal(
                    newFeeBase, team.FeeProcessing ?? 0m, team.FeeDiscount ?? 0m, team.FeeDiscountMp ?? 0m,
                    team.FeeDonation ?? 0m, team.FeeLatefee ?? 0m);
                feeDelta = newFeeTotal - (team.FeeTotal ?? 0m);
            }

            if (IsDroppedTeams(targetAgegroup))
                warning = "Team will be deactivated (moved to Dropped Teams).";
            else if (IsDroppedTeams(sourceAgegroup) && !(team.Active ?? true))
                warning = "Team will be reactivated (moved out of Dropped Teams).";

            if (team.ClubrepRegistrationid.HasValue)
                affectedClubRepIds.Add(team.ClubrepRegistrationid.Value);

            previews.Add(new PoolTransferPreviewDto
            {
                TeamId = team.TeamId,
                TeamName = team.TeamName ?? "Unnamed",
                Direction = "source-to-target",
                AgegroupChanges = agegroupChanges,
                CurrentFeeBase = team.FeeBase ?? 0m,
                CurrentFeeTotal = team.FeeTotal ?? 0m,
                NewFeeBase = newFeeBase,
                NewFeeTotal = newFeeTotal,
                FeeDelta = feeDelta,
                IsScheduled = isScheduled,
                RequiresSymmetricalSwap = false,
                Warning = warning
            });
        }

        // Preview target teams → source division (for symmetrical swap)
        foreach (var team in allTargetTeams)
        {
            var isScheduled = targetPoolScheduled;
            if (isScheduled) hasScheduledTeams = true;

            decimal newFeeBase = team.FeeBase ?? 0m;
            decimal newFeeTotal = team.FeeTotal ?? 0m;
            decimal feeDelta = 0m;
            string? warning = null;

            if (agegroupChanges && sourceAgegroup != null && job != null)
            {
                // Same team-tier inclusion as the source-to-target leg above.
                var resolved = await _feeService.ResolveFeeForTeamAtAgegroupAsync(
                    job.JobId, RoleConstants.ClubRep, sourceAgegroup.AgegroupId, team.TeamId, ct);
                var deposit = resolved?.EffectiveDeposit ?? 0m;
                newFeeBase = ResolvedFee.ResolveFullPaymentPhase(resolved)
                    ? (resolved?.FullPrice ?? 0m) : deposit;
                // Same FeeMath derivation as the source-to-target leg above.
                newFeeTotal = FeeMath.ComputeFeeTotal(
                    newFeeBase, team.FeeProcessing ?? 0m, team.FeeDiscount ?? 0m, team.FeeDiscountMp ?? 0m,
                    team.FeeDonation ?? 0m, team.FeeLatefee ?? 0m);
                feeDelta = newFeeTotal - (team.FeeTotal ?? 0m);
            }

            if (IsDroppedTeams(sourceAgegroup))
                warning = "Team will be deactivated (moved to Dropped Teams).";
            else if (IsDroppedTeams(targetAgegroup) && !(team.Active ?? true))
                warning = "Team will be reactivated (moved out of Dropped Teams).";

            if (team.ClubrepRegistrationid.HasValue)
                affectedClubRepIds.Add(team.ClubrepRegistrationid.Value);

            previews.Add(new PoolTransferPreviewDto
            {
                TeamId = team.TeamId,
                TeamName = team.TeamName ?? "Unnamed",
                Direction = "target-to-source",
                AgegroupChanges = agegroupChanges,
                CurrentFeeBase = team.FeeBase ?? 0m,
                CurrentFeeTotal = team.FeeTotal ?? 0m,
                NewFeeBase = newFeeBase,
                NewFeeTotal = newFeeTotal,
                FeeDelta = feeDelta,
                IsScheduled = isScheduled,
                RequiresSymmetricalSwap = false,
                Warning = warning
            });
        }

        // Build club rep impact summary
        var clubRepImpacts = new List<PoolClubRepImpactDto>();
        if (affectedClubRepIds.Count > 0 && agegroupChanges)
        {
            var allTeams = allSourceTeams.Concat(allTargetTeams).ToList();

            foreach (var clubRepId in affectedClubRepIds)
            {
                var teamsForRep = allTeams.Where(t => t.ClubrepRegistrationid == clubRepId).ToList();
                if (teamsForRep.Count == 0) continue;

                var clubName = await _teamRepo.GetClubNameForTeamAsync(teamsForRep[0].TeamId, ct) ?? "Unknown Club";
                var currentTotal = teamsForRep.Sum(t => t.FeeTotal ?? 0m);
                var deltaTotal = previews
                    .Where(p => teamsForRep.Any(t => t.TeamId == p.TeamId))
                    .Sum(p => p.FeeDelta);

                clubRepImpacts.Add(new PoolClubRepImpactDto
                {
                    ClubName = clubName,
                    ClubRepRegistrationId = clubRepId,
                    CurrentTotal = currentTotal,
                    NewTotal = currentTotal + deltaTotal,
                    Delta = deltaTotal
                });
            }
        }

        return new PoolTransferPreviewResponse
        {
            Teams = previews,
            ClubRepImpacts = clubRepImpacts,
            HasScheduledTeams = hasScheduledTeams,
            RequiresSymmetricalSwap = requiresSymmetrical
        };
    }

    public async Task<PoolTransferResultDto> ExecuteTransferAsync(
        Guid jobId, string adminUserId, PoolTransferRequest request, CancellationToken ct = default)
    {
        if (request.SourceDivId == request.TargetDivId)
            throw new ArgumentException("Source and target divisions must be different.");

        var sourceDivision = await _divRepo.GetByIdReadOnlyAsync(request.SourceDivId, ct)
            ?? throw new ArgumentException("Source division not found.");
        var targetDivision = await _divRepo.GetByIdReadOnlyAsync(request.TargetDivId, ct)
            ?? throw new ArgumentException("Target division not found.");

        // ONE transaction over the whole transfer: the team moves, the fee recalculation, the
        // club-rep accounting AND the schedule re-seat. Half of this landing is the worst
        // outcome the tool can produce — a director sees teams moved and fees charged while the
        // board still shows the old pools, with nothing on screen saying so. All of it, or none.
        await using var tx = await _leagueRepo.BeginTransactionAsync(ct);

        bool agegroupChanges = sourceDivision.AgegroupId != targetDivision.AgegroupId;
        var now = DateTime.Now;

        if (request.IsSymmetricalSwap && request.SourceTeamIds.Count != request.TargetTeamIds.Count)
            throw new ArgumentException("Symmetrical swap requires equal numbers of source and target teams.");

        var sourceTeams = await _teamRepo.GetTeamsForPoolTransferAsync(request.SourceTeamIds, jobId, ct);
        if (sourceTeams.Count == 0)
            throw new ArgumentException("No valid source teams found for transfer.");

        // POOL-level gate. Throws with the pool the director has to tear down, or permits the
        // one-for-one equal-size swap. Inside the transaction: a throw here disposes it unwritten.
        await EnsurePoolMovementAllowedAsync(
            jobId, sourceDivision, targetDivision,
            request.SourceTeamIds.Count, request.TargetTeamIds.Count, ct);

        // Sequential awaits — these share one scoped DbContext.
        bool sourcePoolScheduled = await _scheduleRepo.IsPoolScheduledAsync(request.SourceDivId, jobId, ct);
        bool targetPoolScheduled = await _scheduleRepo.IsPoolScheduledAsync(request.TargetDivId, jobId, ct);
        bool anyPoolScheduled = sourcePoolScheduled || targetPoolScheduled;

        // Load agegroup context
        var targetAgegroup = await _agegroupRepo.GetByIdAsync(targetDivision.AgegroupId, ct);
        var sourceAgegroup = agegroupChanges
            ? await _agegroupRepo.GetByIdAsync(sourceDivision.AgegroupId, ct)
            : targetAgegroup;

        bool isTargetDropped = IsDroppedTeams(targetAgegroup);
        bool isSourceDropped = IsDroppedTeams(sourceAgegroup);

        int teamsMoved = 0;
        int feesRecalculated = 0;
        int teamsDeactivated = 0;
        int teamsReactivated = 0;
        int scheduleRecordsUpdated = 0;
        var affectedClubRepIds = new HashSet<Guid>();

        // For symmetrical swap: capture old DivRanks before any changes.
        // Incoming team inherits outgoing team's rank so schedule pairings
        // (T1No/T2No based on DivRank) remain valid.
        List<Entities.Teams> targetTeams = new();
        var sourceOldRanks = new Dictionary<Guid, int>();
        var targetOldRanks = new Dictionary<Guid, int>();

        if (request.IsSymmetricalSwap && request.TargetTeamIds.Count > 0)
        {
            targetTeams = await _teamRepo.GetTeamsForPoolTransferAsync(request.TargetTeamIds, jobId, ct);

            foreach (var t in sourceTeams)
                sourceOldRanks[t.TeamId] = t.DivRank;
            foreach (var t in targetTeams)
                targetOldRanks[t.TeamId] = t.DivRank;
        }

        // Every requested target team must have resolved. The counts were checked above, but
        // not existence — an id that resolves to nothing would fault the move loop below on
        // targetOldRanks[...], AFTER the fee-scope flush has committed, leaving rows repointed
        // to an agegroup the team never reached. Fail here instead, with the other pre-flight
        // validations, so the flush is only ever reached by a move that can complete.
        if (request.IsSymmetricalSwap && targetTeams.Count != request.TargetTeamIds.Count)
            throw new ArgumentException("One or more target teams were not found for this job.");

        // Both legs, before anything is written: a rank in a scheduled pool has to end up with an
        // active occupant or the re-seat quietly leaves the old one there.
        var targetDivName = string.IsNullOrWhiteSpace(targetDivision.DivName)
            ? "the target pool" : targetDivision.DivName;
        var sourceDivName = string.IsNullOrWhiteSpace(sourceDivision.DivName)
            ? "the source pool" : sourceDivision.DivName;

        EnsureArrivalsCanBeSeated(sourceTeams, targetPoolScheduled, isSourceDropped, targetDivName);
        EnsureArrivalsCanBeSeated(targetTeams, sourcePoolScheduled, isTargetDropped, sourceDivName);

        // Team-scoped fee rows travel WITH the team. A team-scoped fees.JobFees row is keyed
        // (JobId, RoleId, TeamId) in meaning but stores AgegroupId too, and the cascade's team
        // tier matches the (AgegroupId, TeamId) PAIR — so a row left behind on the old agegroup
        // is invisible and the team silently reverts to the target agegroup's price.
        //
        // Staged TRACKED, deliberately not flushed: it commits with the team move in the single
        // SaveChanges below, so a fault anywhere in this method can never leave fee rows
        // repointed to an agegroup the teams did not reach. The fee stamping further down is
        // written against readers that match the team tier by TeamId ALONE precisely so this
        // repoint never needs to be committed ahead of the move.
        if (agegroupChanges)
        {
            foreach (var team in sourceTeams)
                await _feeService.RepointTeamScopedFeesAsync(
                    team.TeamId, targetDivision.AgegroupId, adminUserId, ct);
            foreach (var team in targetTeams)
                await _feeService.RepointTeamScopedFeesAsync(
                    team.TeamId, sourceDivision.AgegroupId, adminUserId, ct);
        }

        // Resolve processing rate once for this job (all teams share jobId)
        var processingRate = await _feeService.GetEffectiveProcessingRateAsync(jobId, ct);

        // Move source teams → target division
        foreach (var team in sourceTeams)
        {
            team.DivId = request.TargetDivId;
            team.AgegroupId = targetDivision.AgegroupId;
            team.Modified = now;
            team.LebUserId = adminUserId;

            if (request.IsSymmetricalSwap)
            {
                // Inherit the paired target team's rank to preserve schedule pairings
                var sourceIdx = request.SourceTeamIds.IndexOf(team.TeamId);
                var pairedTargetId = request.TargetTeamIds[sourceIdx];
                team.DivRank = targetOldRanks[pairedTargetId];
            }
            else
            {
                var nextRank = await _teamRepo.GetNextDivRankAsync(request.TargetDivId, ct);
                team.DivRank = nextRank;
            }

            if (isTargetDropped && (team.Active ?? true))
            {
                team.Active = false;
                teamsDeactivated++;
            }
            else if (isSourceDropped && !isTargetDropped && !(team.Active ?? true))
            {
                // The exact reverse of the drop: a team leaving Dropped Teams for a live
                // agegroup comes back on. Gated on the SOURCE being dropped, not merely on the
                // target being live — a team a director deactivated by hand inside a normal
                // agegroup must keep that state when it is shuffled between pools.
                team.Active = true;
                teamsReactivated++;
            }

            if (agegroupChanges && targetAgegroup != null)
            {
                // Pre-hydrated applier, NOT ApplyTeamSwapFeesAsync — that one re-resolves the
                // cascade against the DATABASE, which would force the fee repoint above to be
                // committed ahead of the move and cost this method its atomicity. Both reads
                // here match the team tier by TeamId ALONE (ResolveFeeForTeamAtAgegroupAsync
                // explicitly; PaymentState internally via GetResolvedFeesByTeamIdsAsync), so
                // each sees the team's own pricing whether or not the repoint has been written.
                // It is also the SAME resolver the preview above uses, so the number the
                // director approved and the number stamped here share one code path.
                var resolved = await _feeService.ResolveFeeForTeamAtAgegroupAsync(
                    team.JobId, RoleConstants.ClubRep, targetAgegroup.AgegroupId, team.TeamId, ct);
                var state = await _paymentState.ForTeamAsync(team.TeamId, team.JobId, ct);
                _feeService.ApplyTeamSwapFees(
                    team, resolved, state,
                    new TeamFeeApplicationContext
                    {
                        AddProcessingFees = team.Job.BAddProcessingFees,
                        ApplyProcessingFeesToDeposit = team.Job.BApplyProcessingFeesToTeamDeposit ?? false,
                        ProcessingFeePercent = processingRate
                    });
                feesRecalculated++;
            }

            if (team.ClubrepRegistrationid.HasValue)
                affectedClubRepIds.Add(team.ClubrepRegistrationid.Value);

            teamsMoved++;
        }

        // Symmetrical swap: move target teams → source division
        if (request.IsSymmetricalSwap && targetTeams.Count > 0)
        {
            foreach (var team in targetTeams)
            {
                team.DivId = request.SourceDivId;
                team.AgegroupId = sourceDivision.AgegroupId;
                team.Modified = now;
                team.LebUserId = adminUserId;

                // Inherit the paired source team's rank to preserve schedule pairings
                var targetIdx = request.TargetTeamIds.IndexOf(team.TeamId);
                var pairedSourceId = request.SourceTeamIds[targetIdx];
                team.DivRank = sourceOldRanks[pairedSourceId];

                if (isSourceDropped && (team.Active ?? true))
                {
                    team.Active = false;
                    teamsDeactivated++;
                }
                else if (isTargetDropped && !isSourceDropped && !(team.Active ?? true))
                {
                    // Same reactivation rule as the source-to-target leg above, with the two
                    // agegroups swapped: this team's ORIGIN is the target agegroup.
                    team.Active = true;
                    teamsReactivated++;
                }

                if (agegroupChanges && sourceAgegroup != null)
                {
                    // Same pre-hydrated, TeamId-alone path as the source-to-target leg above.
                    var resolved = await _feeService.ResolveFeeForTeamAtAgegroupAsync(
                        team.JobId, RoleConstants.ClubRep, sourceAgegroup.AgegroupId, team.TeamId, ct);
                    var state = await _paymentState.ForTeamAsync(team.TeamId, team.JobId, ct);
                    _feeService.ApplyTeamSwapFees(
                        team, resolved, state,
                        new TeamFeeApplicationContext
                        {
                            AddProcessingFees = team.Job.BAddProcessingFees,
                            ApplyProcessingFeesToDeposit = team.Job.BApplyProcessingFeesToTeamDeposit ?? false,
                            ProcessingFeePercent = processingRate
                        });
                    feesRecalculated++;
                }

                if (team.ClubrepRegistrationid.HasValue)
                    affectedClubRepIds.Add(team.ClubrepRegistrationid.Value);

                teamsMoved++;
            }
        }

        // Persist all team changes in one transaction
        await _teamRepo.SaveChangesAsync(ct);

        // Schedule sync for moved teams.
        //
        // A game belongs to the division whose pairing matrix it was built from — NOT to whichever
        // team happens to be sitting in it. The removed writer here retagged a game's
        // agegroup/div to the mover's NEW division, and only on rows where the mover was T1, so a
        // swap left one pool holding seven games and the other five, with matrix ranks pointing at
        // a division they did not come from. A later re-seat of that division would then have
        // dropped a stranger into the game. Games stay put; the SEATS follow the ranks.
        foreach (var team in sourceTeams.Concat(targetTeams))
        {
            if (!anyPoolScheduled) continue;

            // Name unchanged — the team moved divisions. Re-source its schedule name rows
            // (club:team) via the canonical writer.
            await _scheduleRepo.RecomposeScheduleNamesForJobAsync(
                jobId, team: (team.TeamId, team.TeamName ?? ""), ct: ct);
        }

        // Ranks were set above. A symmetrical swap inherits the outgoing team's rank on purpose,
        // so renumbering it would undo the very thing that keeps the matrix intact — re-seat only.
        // A non-symmetrical move appends at the end and leaves a gap behind, so that one compacts
        // first. Either way the re-seat is what makes the incoming team play the games of the
        // rank it now holds.
        if (request.IsSymmetricalSwap)
        {
            scheduleRecordsUpdated += await _teamSeating.ReseatDivisionsAsync(
                jobId, new[] { request.SourceDivId, request.TargetDivId }, adminUserId, ct);
        }
        else
        {
            scheduleRecordsUpdated += await _teamSeating.RenumberAndReseatAsync(
                request.SourceDivId, jobId, adminUserId, ct);
            scheduleRecordsUpdated += await _teamSeating.RenumberAndReseatAsync(
                request.TargetDivId, jobId, adminUserId, ct);
        }

        // Club rep financial sync
        foreach (var clubRepId in affectedClubRepIds)
        {
            await _registrationRepo.SynchronizeClubRepFinancialsAsync(clubRepId, adminUserId, ct);
        }

        await _leagueRepo.CommitTransactionAsync(ct);

        var parts = new List<string>();
        if (teamsMoved > 0) parts.Add($"{teamsMoved} team(s) moved");
        if (feesRecalculated > 0) parts.Add($"{feesRecalculated} fee(s) recalculated");
        if (teamsDeactivated > 0) parts.Add($"{teamsDeactivated} team(s) deactivated");
        if (teamsReactivated > 0) parts.Add($"{teamsReactivated} team(s) reactivated");
        if (scheduleRecordsUpdated > 0) parts.Add($"{scheduleRecordsUpdated} schedule record(s) updated");

        return new PoolTransferResultDto
        {
            TeamsMoved = teamsMoved,
            FeesRecalculated = feesRecalculated,
            TeamsDeactivated = teamsDeactivated,
            TeamsReactivated = teamsReactivated,
            ScheduleRecordsUpdated = scheduleRecordsUpdated,
            Message = parts.Count > 0 ? string.Join(", ", parts) + "." : "No changes made."
        };
    }

    public async Task ToggleTeamActiveAsync(
        Guid teamId, Guid jobId, bool active, string adminUserId, CancellationToken ct = default)
    {
        var team = await _teamRepo.GetTeamFromTeamId(teamId, ct)
            ?? throw new KeyNotFoundException("Team not found.");

        if (team.JobId != jobId)
            throw new ArgumentException("Team does not belong to this job.");

        // Inactivating is removing: the schedule recalculation seats only ACTIVE teams, so a
        // switched-off team's rank falls out of the map and its games resolve to nobody. Same
        // rule as delete and drop. Switching a team back ON is always allowed — it can only
        // return a rank to the map.
        if (!active)
            await _teamSeating.EnsureTeamMayLeavePoolAsync(teamId, jobId, "inactivated", ct);

        team.Active = active;
        team.Modified = DateTime.Now;
        team.LebUserId = adminUserId;
        await _teamRepo.SaveChangesAsync(ct);

        if (team.ClubrepRegistrationid.HasValue)
            await _registrationRepo.SynchronizeClubRepFinancialsAsync(
                team.ClubrepRegistrationid.Value, adminUserId, ct);
    }

    public async Task<TeamSeatingResultDto> UpdateTeamDivRankAsync(
        Guid teamId, Guid jobId, int divRank, string adminUserId, CancellationToken ct = default)
    {
        // Swapping two teams' ranks here IS swapping their schedules — the technique directors
        // use to trade two teams' games. This screen used to write the rank and stop, so the
        // ranks traded and the games did not; ITeamSeatingService owns both halves.
        return await _teamSeating.ApplyRankChangeAsync(
            teamId, jobId, divRank, newName: null, adminUserId, ct);
    }

    /// <summary>
    /// The pool-level movement gate. A pool that has been scheduled has frozen membership: the
    /// pairing matrix was built for N ranks and does not care whose id sits in which slot, so
    /// removing ANY team leaves it at N-1 and adding one leaves the arrival with no slot to play.
    ///
    /// The exception is a one-for-one swap, and it is the WHOLE exception. Each team takes the
    /// OTHER team's rank, which already exists in the pool it arrives at, so both pools come out
    /// of the trade with every rank still occupied. That holds at ANY size and whether one side
    /// has a board or both do: an 8-team pool can trade with a 2-team pool and both boards still
    /// resolve completely. Do not reintroduce an equal-size test — it was here, and it was
    /// confusing "both matrices stay intact" (what a swap needs) with "both matrices are
    /// identical" (what nothing needs). All it actually governed was each team inheriting its new
    /// pool's game count, which is a tournament decision and not this gate's business.
    ///
    /// Evaluated per POOL, never per team. "Does this team have game rows" is a question about
    /// seating, which is derived; it answers "no" for a team whose seating is already broken and
    /// so waves through the removal that does the real damage.
    /// </summary>
    private async Task EnsurePoolMovementAllowedAsync(
        Guid jobId,
        Entities.Divisions sourceDivision,
        Entities.Divisions targetDivision,
        int sourceTeamsMoving,
        int targetTeamsMoving,
        CancellationToken ct)
    {
        // Sequential awaits — these share one scoped DbContext.
        var sourceScheduled = await _scheduleRepo.IsPoolScheduledAsync(sourceDivision.DivId, jobId, ct);
        var targetScheduled = await _scheduleRepo.IsPoolScheduledAsync(targetDivision.DivId, jobId, ct);

        // Neither pool has a board. Nothing to protect — move freely, any number, either way.
        if (!sourceScheduled && !targetScheduled)
            return;

        var sourceName = string.IsNullOrWhiteSpace(sourceDivision.DivName)
            ? "The source pool" : sourceDivision.DivName;
        var targetName = string.IsNullOrWhiteSpace(targetDivision.DivName)
            ? "the target pool" : targetDivision.DivName;

        // One team out, one team back — the only shape that leaves both pools whole.
        if (sourceTeamsMoving == 1 && targetTeamsMoving == 1)
            return;

        var scheduled = sourceScheduled && targetScheduled
            ? $"{sourceName} and {targetName} are both scheduled"
            : $"{(sourceScheduled ? sourceName : targetName)} is scheduled";

        var breakDown = sourceScheduled && targetScheduled
            ? "break both schedules down first"
            : $"break down {(sourceScheduled ? sourceName : targetName)}'s schedule first";

        throw new InvalidOperationException(
            $"{scheduled}, so a team can only cross that boundary as a one-for-one swap: the team "
            + "coming the other way takes the departing team's rank and plays its games, which is "
            + "what keeps the pairing matrix whole. Use the swap arrow on a team's row to pick the "
            + $"team it trades places with, or {breakDown}.");
    }

    /// <summary>
    /// A team arriving in a SCHEDULED pool has to be active once the move settles. The re-seat
    /// builds its rank map from active teams only, and a rank whose occupant is missing from that
    /// map is deliberately LEFT HOLDING ITS OLD TEAM rather than blanked — so an inactive arrival
    /// produces no error and no re-seat, just the departing team's id still sitting in games it
    /// no longer plays. Unassigned and Dropped Teams both hold inactive teams as a matter of
    /// course, which is why this only becomes reachable once one-sided swaps are permitted.
    ///
    /// A team coming OUT of Dropped Teams to a live pool is reactivated by the move itself, so it
    /// qualifies; that reactivation is the mirror of the drop and happens in the same transaction.
    /// </summary>
    private static void EnsureArrivalsCanBeSeated(
        IEnumerable<Entities.Teams> arrivals,
        bool destinationScheduled,
        bool arrivalsComeFromDropped,
        string destinationName)
    {
        if (!destinationScheduled) return;

        foreach (var team in arrivals)
        {
            if ((team.Active ?? true) || arrivalsComeFromDropped) continue;

            throw new InvalidOperationException(
                $"{team.TeamName} is inactive and cannot take a rank in {destinationName}, which "
                + "is scheduled. The re-seat skips inactive teams, so the rank would keep the "
                + "departing team in games it no longer plays. Reactivate the team first, then "
                + "make the swap.");
        }
    }

    private static bool IsDroppedTeams(Entities.Agegroups? agegroup)
    {
        if (agegroup?.AgegroupName == null) return false;
        return agegroup.AgegroupName.Contains("DROPPED", StringComparison.OrdinalIgnoreCase);
    }
}
