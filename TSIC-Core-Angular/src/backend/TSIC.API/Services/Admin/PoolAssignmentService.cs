using TSIC.Contracts.Dtos.PoolAssignment;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Application.Services.Teams;
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
    private readonly ITeamFeeCalculator _teamFeeCalc;

    public PoolAssignmentService(
        ITeamRepository teamRepo,
        IDivisionRepository divRepo,
        IScheduleRepository scheduleRepo,
        IRegistrationRepository registrationRepo,
        IAgeGroupRepository agegroupRepo,
        ITeamFeeCalculator teamFeeCalc)
    {
        _teamRepo = teamRepo;
        _divRepo = divRepo;
        _scheduleRepo = scheduleRepo;
        _registrationRepo = registrationRepo;
        _agegroupRepo = agegroupRepo;
        _teamFeeCalc = teamFeeCalc;
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

        var scheduledTeamIds = await _teamRepo.GetScheduledTeamIdsAsync(jobId, ct);
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
        bool requiresSymmetrical = false;

        // Load all teams at once (more efficient than one-by-one)
        var allSourceTeams = await _teamRepo.GetTeamsForPoolTransferAsync(request.SourceTeamIds, jobId, ct);
        var allTargetTeams = request.IsSymmetricalSwap && request.TargetTeamIds.Count > 0
            ? await _teamRepo.GetTeamsForPoolTransferAsync(request.TargetTeamIds, jobId, ct)
            : new List<Entities.Teams>();

        // Set job from first available team if not already set
        job ??= allSourceTeams.FirstOrDefault()?.Job ?? allTargetTeams.FirstOrDefault()?.Job;

        // Preview source teams → target division
        foreach (var team in allSourceTeams)
        {
            var isScheduled = scheduledTeamIds.Contains(team.TeamId);
            if (isScheduled) hasScheduledTeams = true;
            if (isScheduled && !request.IsSymmetricalSwap)
                requiresSymmetrical = true;

            decimal newFeeBase = team.FeeBase ?? 0m;
            decimal newFeeTotal = team.FeeTotal ?? 0m;
            decimal feeDelta = 0m;
            string? warning = null;

            if (agegroupChanges && targetAgegroup != null && job != null)
            {
                var (calcFeeBase, calcFeeProcessing) = _teamFeeCalc.CalculateTeamFees(
                    targetAgegroup.RosterFee ?? 0m,
                    targetAgegroup.TeamFee ?? 0m,
                    job.BTeamsFullPaymentRequired ?? false,
                    job.BAddProcessingFees,
                    job.BApplyProcessingFeesToTeamDeposit ?? false,
                    job.ProcessingFeePercent,
                    team.PaidTotal ?? 0m,
                    team.FeeTotal ?? 0m);
                newFeeBase = calcFeeBase;
                newFeeTotal = calcFeeBase + calcFeeProcessing;
                feeDelta = newFeeTotal - (team.FeeTotal ?? 0m);
            }

            if (IsDroppedTeams(targetAgegroup))
                warning = "Team will be deactivated (moved to Dropped Teams).";

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
                RequiresSymmetricalSwap = isScheduled && !request.IsSymmetricalSwap,
                Warning = warning
            });
        }

        // Preview target teams → source division (for symmetrical swap)
        foreach (var team in allTargetTeams)
        {
            var isScheduled = scheduledTeamIds.Contains(team.TeamId);

            decimal newFeeBase = team.FeeBase ?? 0m;
            decimal newFeeTotal = team.FeeTotal ?? 0m;
            decimal feeDelta = 0m;
            string? warning = null;

            if (agegroupChanges && sourceAgegroup != null && job != null)
            {
                var (calcFeeBase, calcFeeProcessing) = _teamFeeCalc.CalculateTeamFees(
                    sourceAgegroup.RosterFee ?? 0m,
                    sourceAgegroup.TeamFee ?? 0m,
                    job.BTeamsFullPaymentRequired ?? false,
                    job.BAddProcessingFees,
                    job.BApplyProcessingFeesToTeamDeposit ?? false,
                    job.ProcessingFeePercent,
                    team.PaidTotal ?? 0m,
                    team.FeeTotal ?? 0m);
                newFeeBase = calcFeeBase;
                newFeeTotal = calcFeeBase + calcFeeProcessing;
                feeDelta = newFeeTotal - (team.FeeTotal ?? 0m);
            }

            if (IsDroppedTeams(sourceAgegroup))
                warning = "Team will be deactivated (moved to Dropped Teams).";

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

        bool agegroupChanges = sourceDivision.AgegroupId != targetDivision.AgegroupId;
        var now = DateTime.UtcNow;

        if (request.IsSymmetricalSwap && request.SourceTeamIds.Count != request.TargetTeamIds.Count)
            throw new ArgumentException("Symmetrical swap requires equal numbers of source and target teams.");

        var sourceTeams = await _teamRepo.GetTeamsForPoolTransferAsync(request.SourceTeamIds, jobId, ct);
        if (sourceTeams.Count == 0)
            throw new ArgumentException("No valid source teams found for transfer.");

        var scheduledTeamIds = await _teamRepo.GetScheduledTeamIdsAsync(jobId, ct);
        bool anyScheduled = sourceTeams.Any(t => scheduledTeamIds.Contains(t.TeamId));
        if (anyScheduled && !request.IsSymmetricalSwap)
            throw new InvalidOperationException(
                "One or more source teams have scheduled games. A symmetrical swap is required to maintain schedule integrity.");

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

            if (agegroupChanges && targetAgegroup != null)
            {
                var (newFeeBase, newFeeProcessing) = _teamFeeCalc.CalculateTeamFees(
                    targetAgegroup.RosterFee ?? 0m,
                    targetAgegroup.TeamFee ?? 0m,
                    team.Job.BTeamsFullPaymentRequired ?? false,
                    team.Job.BAddProcessingFees,
                    team.Job.BApplyProcessingFeesToTeamDeposit ?? false,
                    team.Job.ProcessingFeePercent,
                    team.PaidTotal ?? 0m,
                    team.FeeTotal ?? 0m);
                team.FeeBase = newFeeBase;
                team.FeeProcessing = newFeeProcessing;
                team.FeeTotal = newFeeBase + newFeeProcessing;
                team.OwedTotal = (newFeeBase + newFeeProcessing) - (team.PaidTotal ?? 0m);
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

                if (agegroupChanges && sourceAgegroup != null)
                {
                    var (newFeeBase, newFeeProcessing) = _teamFeeCalc.CalculateTeamFees(
                        sourceAgegroup.RosterFee ?? 0m,
                        sourceAgegroup.TeamFee ?? 0m,
                        team.Job.BTeamsFullPaymentRequired ?? false,
                        team.Job.BAddProcessingFees,
                        team.Job.BApplyProcessingFeesToTeamDeposit ?? false,
                        team.Job.ProcessingFeePercent,
                        team.PaidTotal ?? 0m,
                        team.FeeTotal ?? 0m);
                    team.FeeBase = newFeeBase;
                    team.FeeProcessing = newFeeProcessing;
                    team.FeeTotal = newFeeBase + newFeeProcessing;
                    team.OwedTotal = (newFeeBase + newFeeProcessing) - (team.PaidTotal ?? 0m);
                    feesRecalculated++;
                }

                if (team.ClubrepRegistrationid.HasValue)
                    affectedClubRepIds.Add(team.ClubrepRegistrationid.Value);

                teamsMoved++;
            }
        }

        // Persist all team changes in one transaction
        await _teamRepo.SaveChangesAsync(ct);

        // Schedule sync for moved teams
        foreach (var team in sourceTeams.Concat(targetTeams))
        {
            if (!scheduledTeamIds.Contains(team.TeamId)) continue;

            await _scheduleRepo.SynchronizeScheduleNamesForTeamAsync(team.TeamId, jobId, ct);

            var agName = team.AgegroupId == targetDivision.AgegroupId
                ? targetAgegroup?.AgegroupName ?? ""
                : sourceAgegroup?.AgegroupName ?? "";
            var divName = team.DivId == request.TargetDivId
                ? targetDivision.DivName ?? ""
                : sourceDivision.DivName ?? "";

            var updated = await _scheduleRepo.SynchronizeScheduleDivisionForTeamAsync(
                team.TeamId, jobId, team.AgegroupId, agName,
                team.DivId ?? Guid.Empty, divName, ct);
            scheduleRecordsUpdated += updated;
        }

        // Renumber DivRanks: only for non-symmetrical moves (close gaps).
        // Symmetrical swaps preserve ranks intentionally to maintain schedule
        // pairings (T1No/T2No map to DivRank).
        if (!request.IsSymmetricalSwap)
        {
            await _teamRepo.RenumberDivRanksAsync(request.SourceDivId, ct);
            await _teamRepo.RenumberDivRanksAsync(request.TargetDivId, ct);
        }

        // Club rep financial sync
        foreach (var clubRepId in affectedClubRepIds)
        {
            await _registrationRepo.SynchronizeClubRepFinancialsAsync(clubRepId, adminUserId, ct);
        }

        var parts = new List<string>();
        if (teamsMoved > 0) parts.Add($"{teamsMoved} team(s) moved");
        if (feesRecalculated > 0) parts.Add($"{feesRecalculated} fee(s) recalculated");
        if (teamsDeactivated > 0) parts.Add($"{teamsDeactivated} team(s) deactivated");
        if (scheduleRecordsUpdated > 0) parts.Add($"{scheduleRecordsUpdated} schedule record(s) updated");

        return new PoolTransferResultDto
        {
            TeamsMoved = teamsMoved,
            FeesRecalculated = feesRecalculated,
            TeamsDeactivated = teamsDeactivated,
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

        team.Active = active;
        team.Modified = DateTime.UtcNow;
        team.LebUserId = adminUserId;
        await _teamRepo.SaveChangesAsync(ct);

        if (team.ClubrepRegistrationid.HasValue)
            await _registrationRepo.SynchronizeClubRepFinancialsAsync(
                team.ClubrepRegistrationid.Value, adminUserId, ct);
    }

    public async Task UpdateTeamDivRankAsync(
        Guid teamId, Guid jobId, int divRank, string adminUserId, CancellationToken ct = default)
    {
        var team = await _teamRepo.GetTeamFromTeamId(teamId, ct)
            ?? throw new KeyNotFoundException("Team not found.");

        if (team.JobId != jobId)
            throw new ArgumentException("Team does not belong to this job.");
        if (!team.DivId.HasValue)
            throw new InvalidOperationException("Team has no division assignment.");

        int oldRank = team.DivRank;
        if (oldRank == divRank) return;

        // DivRank edit is a positional swap: the team at the target rank
        // gets the editing team's old rank. This keeps ranks contiguous
        // (1..N) and preserves schedule pairings (T1No/T2No).
        var swapTeam = await _teamRepo.GetTeamByDivRankAsync(team.DivId.Value, divRank, ct);
        if (swapTeam != null)
        {
            swapTeam.DivRank = oldRank;
            swapTeam.Modified = DateTime.UtcNow;
            swapTeam.LebUserId = adminUserId;
        }

        team.DivRank = divRank;
        team.Modified = DateTime.UtcNow;
        team.LebUserId = adminUserId;
        await _teamRepo.SaveChangesAsync(ct);
    }

    private static bool IsDroppedTeams(Entities.Agegroups? agegroup)
    {
        if (agegroup?.AgegroupName == null) return false;
        return agegroup.AgegroupName.Contains("DROPPED", StringComparison.OrdinalIgnoreCase);
    }
}
