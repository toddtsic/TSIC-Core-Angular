using System.Globalization;
using System.Text.Json;
using TSIC.Domain.Constants;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Services;
using TSIC.Domain.Entities;
using TSIC.API.Services.Shared.Utilities;
using TSIC.API.Services.Shared.VerticalInsure;
using TSIC.API.Services.Teams;
using TSIC.Contracts.Repositories;

namespace TSIC.API.Services.Players;

public class PlayerRegistrationService : IPlayerRegistrationService
{
    private readonly ILogger<PlayerRegistrationService> _logger;
    private readonly IFeeResolutionService _feeService;
    private readonly IVerticalInsureService _verticalInsure;
    private readonly ITeamLookupService _teamLookupService;
    private readonly IPlayerFormValidationService _validationService;
    private readonly IRegistrationRepository _registrations;
    private readonly ITeamRepository _teams;
    private readonly IJobRepository _jobs;
    private readonly ITeamPlacementService _placement;
    private readonly IMedFormService _medForms;

    private sealed class PreSubmitContext
    {
        public Guid JobId { get; init; }
        public string FamilyUserId { get; init; } = string.Empty;
        public List<TSIC.Domain.Entities.Teams> Teams { get; init; } = new();
        public Dictionary<Guid, int> TeamRosterCounts { get; init; } = new();
        public string RegistrationMode { get; init; } = "PP";
        public string? MetadataJson { get; init; }
        public bool BPlayersFullPaymentRequired { get; init; }
        public Dictionary<string, string> NameToProperty { get; init; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, System.Reflection.PropertyInfo> WritableProps { get; init; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, List<Registrations>> ExistingByPlayer { get; init; } = new();
        public Dictionary<(string PlayerId, Guid TeamId), Registrations> ExistingByPlayerTeam { get; init; } = new();
    }

    public PlayerRegistrationService(
        ILogger<PlayerRegistrationService> logger,
        IFeeResolutionService feeService,
        IVerticalInsureService verticalInsure,
        ITeamLookupService teamLookupService,
        IPlayerFormValidationService validationService,
        IRegistrationRepository registrations,
        ITeamRepository teams,
        IJobRepository jobs,
        ITeamPlacementService placement,
        IMedFormService medForms)
    {
        _logger = logger;
        _feeService = feeService;
        _verticalInsure = verticalInsure;
        _teamLookupService = teamLookupService;
        _validationService = validationService;
        _registrations = registrations;
        _teams = teams;
        _jobs = jobs;
        _placement = placement;
        _medForms = medForms;
    }

    public async Task<ReserveTeamsResponseDto> ReserveTeamsAsync(Guid jobId, string familyUserId, ReserveTeamsRequestDto request, string callerUserId)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));

        // Build lightweight selections (no form values)
        var preSubmitSelections = request.TeamSelections
            .Select(s => new PreSubmitTeamSelectionDto { PlayerId = s.PlayerId, TeamId = s.TeamId })
            .ToList();

        var fakeRequest = new PreSubmitPlayerRegistrationRequestDto
        {
            JobPath = request.JobPath,
            TeamSelections = preSubmitSelections
        };

        var ctx = await BuildPreSubmitContextAsync(jobId, familyUserId, fakeRequest);

        var selectionsByPlayer = preSubmitSelections
            .GroupBy(s => s.PlayerId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var teamResults = new List<PreSubmitTeamResultDto>();
        foreach (var (playerId, selections) in selectionsByPlayer)
        {
            await ProcessPlayerSelectionsAsync(ctx, playerId, selections, teamResults, applyFormValues: false);
        }

        await _registrations.SaveChangesAsync();

        // Mint-on-fill (same as PreSubmit): a reservation can also bring a team to max.
        await EnsureWaitlistMirrorsForFilledTeamsAsync(ctx, fakeRequest);

        return new ReserveTeamsResponseDto { TeamResults = teamResults };
    }

    public async Task<PreSubmitPlayerRegistrationResponseDto> PreSubmitAsync(Guid jobId, string familyUserId, PreSubmitPlayerRegistrationRequestDto request, string callerUserId)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));

        var ctx = await BuildPreSubmitContextAsync(jobId, familyUserId, request);

        var selectionsByPlayer = request.TeamSelections
            .GroupBy(s => s.PlayerId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var teamResults = new List<PreSubmitTeamResultDto>();
        foreach (var (playerId, selections) in selectionsByPlayer)
        {
            await ProcessPlayerSelectionsAsync(ctx, playerId, selections, teamResults);
        }

        // Server-side metadata validation BEFORE saving. If it fails, do not persist any changes.
        try
        {
            var validationErrors = _validationService.ValidatePlayerFormValues(ctx.MetadataJson, request.TeamSelections);
            if (validationErrors.Count > 0)
            {
                // Build insurance offer even on validation errors (non-persistent) for a consistent response shape
                var insurance = await _verticalInsure.BuildOfferAsync(ctx.JobId, ctx.FamilyUserId);
                return new PreSubmitPlayerRegistrationResponseDto
                {
                    TeamResults = teamResults,
                    NextTab = teamResults.Exists(r => r.IsFull) ? "Forms" : "Forms",
                    ValidationErrors = validationErrors,
                    Insurance = insurance
                };
            }
        }
        catch (Exception vex)
        {
            // If validation throws, treat as non-fatal and proceed without blocking save (maintain prior behavior)
            _logger.LogWarning(vex, "[PreSubmit] Validation threw unexpectedly; proceeding.");
        }

        await _registrations.SaveChangesAsync();

        // Mint-on-fill: now that the rows are committed, proactively create the WAITLIST
        // mirror for any real team this submission brought to its roster max, so the picker
        // can offer the twin immediately rather than lazily on the next overflow.
        await EnsureWaitlistMirrorsForFilledTeamsAsync(ctx, request);

        // Delegate insurance offer construction to VerticalInsure service.
        var finalInsurance = await _verticalInsure.BuildOfferAsync(ctx.JobId, ctx.FamilyUserId);
        return new PreSubmitPlayerRegistrationResponseDto
        {
            TeamResults = teamResults,
            NextTab = teamResults.Exists(r => r.IsFull) ? "Team" : "Payment",
            ValidationErrors = null,
            Insurance = finalInsurance
        };
    }

    private async Task<PreSubmitContext> BuildPreSubmitContextAsync(Guid jobId, string familyUserId, PreSubmitPlayerRegistrationRequestDto request)
    {
        var teamIds = request.TeamSelections.Select(ts => ts.TeamId).Distinct().ToList();
        var teams = await _teams.GetTeamsForJobAsync(jobId, teamIds);
        // Capacity seed = active + inactive (pending) players, matching the picker's
        // GetRosterCountsByTeamAsync so the isFull gate agrees with the picker's rosterFull
        // and with the overflow recount (GetAssignedPlayerCountAsync). Pending must count
        // (Todd: "max must include pending").
        var teamRosterCounts = await _registrations.GetRosterCountsByTeamAsync(teamIds);

        var jobEntity = await _jobs.GetPreSubmitMetadataAsync(jobId);

        var metadataJson = jobEntity?.PlayerProfileMetadataJson;
        var registrationMode = GetRegistrationMode(jobEntity?.CoreRegformPlayer, jobEntity?.JsonOptions);

        var nameToProperty = FormValueMapper.BuildFieldNameToPropertyMap(metadataJson);
        var writableProps = FormValueMapper.BuildWritablePropertyMap();

        var playerIds = request.TeamSelections.Select(ts => ts.PlayerId).Distinct().ToList();
        var existingRegs = await _registrations.GetFamilyRegistrationsForPlayersTrackedAsync(jobId, familyUserId, playerIds);

        var existingByPlayer = existingRegs.GroupBy(r => r.UserId!).ToDictionary(g => g.Key, g => g.ToList());
        var existingByPlayerTeam = existingRegs
            .Where(r => r.AssignedTeamId.HasValue)
            .GroupBy(r => (r.UserId!, r.AssignedTeamId!.Value))
            .ToDictionary(g => (g.Key.Item1, g.Key.Value), g => g.OrderByDescending(x => x.Modified).First());

        return new PreSubmitContext
        {
            JobId = jobId,
            FamilyUserId = familyUserId,
            Teams = teams,
            TeamRosterCounts = teamRosterCounts,
            RegistrationMode = registrationMode,
            MetadataJson = metadataJson,
            BPlayersFullPaymentRequired = jobEntity?.BPlayersFullPaymentRequired ?? false,
            NameToProperty = nameToProperty,
            WritableProps = writableProps,
            ExistingByPlayer = existingByPlayer,
            ExistingByPlayerTeam = existingByPlayerTeam
        };
    }

    /// <summary>
    /// After a submission commits, ensure the WAITLIST mirror exists for every real team the
    /// submission brought to (or past) its roster max. Proactive mint-on-fill so the picker
    /// can surface the twin the instant a team fills, instead of waiting for the next
    /// registrant to overflow. Idempotent and gated on the job's waitlist flag
    /// (EnsureWaitlistMirrorAsync no-ops for non-waitlist jobs). The >= MaxCount guard skips
    /// unlimited teams (MaxCount &lt;= 0) and the mirror teams themselves (MaxCount=100000,
    /// never reached).
    /// </summary>
    private async Task EnsureWaitlistMirrorsForFilledTeamsAsync(
        PreSubmitContext ctx, PreSubmitPlayerRegistrationRequestDto request)
    {
        foreach (var teamId in request.TeamSelections.Select(s => s.TeamId).Distinct())
        {
            var team = ctx.Teams.Find(t => t.TeamId == teamId);
            if (team == null || team.MaxCount <= 0)
                continue;

            // Fresh committed count (post-save): active + inactive (pending) players.
            var committed = await _teams.GetAssignedPlayerCountAsync(team.TeamId);
            if (committed >= team.MaxCount)
                await _placement.EnsureWaitlistMirrorAsync(ctx.JobId, team.TeamId, ctx.FamilyUserId);
        }
    }

    private async Task ProcessPlayerSelectionsAsync(PreSubmitContext ctx, string playerId, List<PreSubmitTeamSelectionDto> selections, List<PreSubmitTeamResultDto> teamResults, bool applyFormValues = true)
    {
        var desiredTeamIds = selections.Select(s => s.TeamId).Distinct().ToList();
        if (desiredTeamIds.Count == 0) return;

        if (desiredTeamIds.Count == 1)
        {
            await ProcessSingleTeamSelectionAsync(ctx, playerId, selections, teamResults, applyFormValues);
        }
        else
        {
            await ProcessMultiTeamSelectionsAsync(ctx, playerId, selections, teamResults, applyFormValues);
        }
    }

    private void AddResult(List<PreSubmitTeamResultDto> results, string playerId, Guid teamId, bool isFull, string teamName, string message, bool created, bool isWaitlisted = false, string? waitlistTeamName = null)
    {
        results.Add(new PreSubmitTeamResultDto
        {
            PlayerId = playerId,
            TeamId = teamId,
            IsFull = isFull,
            TeamName = teamName,
            Message = message,
            RegistrationCreated = created,
            IsWaitlisted = isWaitlisted,
            WaitlistTeamName = waitlistTeamName
        });
    }

    private async Task ProcessSingleTeamSelectionAsync(PreSubmitContext ctx, string playerId, List<PreSubmitTeamSelectionDto> selections, List<PreSubmitTeamResultDto> teamResults, bool applyFormValues = true)
    {
        var teamId = selections.Select(s => s.TeamId).First();
        var team = ctx.Teams.Find(t => t.TeamId == teamId);
        if (team == null)
        {
            AddResult(teamResults, playerId, teamId, true, "Unknown", "Team not found.", false);
            return;
        }
        // Check roster capacity — may redirect to waitlist team mirror
        var rosterCount = ctx.TeamRosterCounts.TryGetValue(team.TeamId, out var cnt) ? cnt : 0;
        var isFull = team.MaxCount > 0 && rosterCount >= team.MaxCount;
        var isWaitlisted = false;
        string? waitlistTeamName = null;
        if (isFull)
        {
            try
            {
                var rosterPlacement = await _placement.ResolveRosterPlacementAsync(
                    ctx.JobId, team.TeamId, ctx.FamilyUserId);
                if (rosterPlacement.IsWaitlisted)
                {
                    isWaitlisted = true;
                    waitlistTeamName = rosterPlacement.WaitlistTeamName;
                    // Swap to the waitlist team — continue registration on that team
                    team = ctx.Teams.Find(t => t.TeamId == rosterPlacement.TeamId)
                        ?? await _teams.GetTeamFromTeamId(rosterPlacement.TeamId)
                        ?? team;
                    teamId = rosterPlacement.TeamId;
                }
            }
            catch (InvalidOperationException)
            {
                AddResult(teamResults, playerId, team.TeamId, true, team.TeamName ?? string.Empty, "Team roster is full.", false);
                return;
            }
        }

        // Fail loud, never fabricate: a team with no fee configured at any cascade level
        // must not silently register at $0. Block just this line; other teams still proceed.
        var feeCheck = await _feeService.ResolveFeeAsync(team.JobId, RoleConstants.Player, team.AgegroupId, team.TeamId);
        if (feeCheck is not { FeeConfigured: true })
        {
            AddResult(teamResults, playerId, team.TeamId, false, team.TeamName ?? string.Empty,
                "Fee not set for this event — contact the director.", false);
            return;
        }

        Registrations? regToUpdate = null;
        if (ctx.ExistingByPlayer.TryGetValue(playerId, out var list) && list.Count > 0)
        {
            if (ctx.ExistingByPlayerTeam.TryGetValue((playerId, team.TeamId), out var exact))
            {
                regToUpdate = exact;
            }
            else
            {
                regToUpdate = list.OrderByDescending(r => r.Modified).First();
            }
        }

        var sel = selections[^1];
        var countBefore = teamResults.Count;
        if (regToUpdate != null)
        {
            await UpdateExistingRegistrationAsync(ctx, regToUpdate, team, sel, playerId, teamResults, applyFormValues);
        }
        else
        {
            await CreateNewRegistrationAsync(ctx, playerId, team, sel, teamResults, applyFormValues);
            // Increment the in-memory snapshot so siblings later in the same family
            // submission see the spot consumed (DB count is BActive=true only and won't
            // reflect a brand-new BActive=false row, but capacity must include it).
            ctx.TeamRosterCounts[team.TeamId] = (ctx.TeamRosterCounts.TryGetValue(team.TeamId, out var prev) ? prev : 0) + 1;
        }

        // Stamp waitlist state on any results added by the downstream methods
        if (isWaitlisted)
        {
            for (var i = countBefore; i < teamResults.Count; i++)
            {
                teamResults[i] = teamResults[i] with { IsWaitlisted = true, WaitlistTeamName = waitlistTeamName };
            }
        }
    }

    private async Task ProcessMultiTeamSelectionsAsync(PreSubmitContext ctx, string playerId, List<PreSubmitTeamSelectionDto> selections, List<PreSubmitTeamResultDto> teamResults, bool applyFormValues = true)
    {
        if (string.Equals(ctx.RegistrationMode, "PP", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var tId in selections.Select(s => s.TeamId).Distinct())
            {
                var t = ctx.Teams.Find(tt => tt.TeamId == tId);
                AddResult(teamResults, playerId, tId, false, t?.TeamName ?? string.Empty, "Multiple teams not allowed for this job.", false);
            }
            return;
        }

        foreach (var tId in selections.Select(s => s.TeamId).Distinct())
        {
            var team = ctx.Teams.Find(t => t.TeamId == tId);
            if (team == null)
            {
                AddResult(teamResults, playerId, tId, true, "Unknown", "Team not found.", false);
                continue;
            }
            // Check roster capacity — may redirect to waitlist team mirror
            var rosterCount = ctx.TeamRosterCounts.TryGetValue(team.TeamId, out var cnt) ? cnt : 0;
            var isFull = team.MaxCount > 0 && rosterCount >= team.MaxCount;
            var effectiveTeamId = team.TeamId;
            var isWaitlisted = false;
            string? waitlistTeamName = null;
            if (isFull)
            {
                try
                {
                    var rosterPlacement = await _placement.ResolveRosterPlacementAsync(
                        ctx.JobId, team.TeamId, ctx.FamilyUserId);
                    if (rosterPlacement.IsWaitlisted)
                    {
                        isWaitlisted = true;
                        waitlistTeamName = rosterPlacement.WaitlistTeamName;
                        team = ctx.Teams.Find(t => t.TeamId == rosterPlacement.TeamId)
                            ?? await _teams.GetTeamFromTeamId(rosterPlacement.TeamId)
                            ?? team;
                        effectiveTeamId = rosterPlacement.TeamId;
                    }
                }
                catch (InvalidOperationException)
                {
                    AddResult(teamResults, playerId, team.TeamId, true, team.TeamName ?? string.Empty, "Team roster is full.", false);
                    continue;
                }
            }

            // Fail loud, never fabricate: skip teams with no configured fee (see ProcessSingleTeamSelectionAsync).
            var feeCheck = await _feeService.ResolveFeeAsync(team.JobId, RoleConstants.Player, team.AgegroupId, team.TeamId);
            if (feeCheck is not { FeeConfigured: true })
            {
                AddResult(teamResults, playerId, team.TeamId, false, team.TeamName ?? string.Empty,
                    "Fee not set for this event — contact the director.", false);
                continue;
            }

            var countBefore = teamResults.Count;
            if (ctx.ExistingByPlayerTeam.TryGetValue((playerId, effectiveTeamId), out var existing))
            {
                existing.Modified = DateTime.Now;
                var sel = selections.Last(s => s.TeamId == team.TeamId);
                if (applyFormValues)
                {
                    FormValueMapper.ApplyFormValues(existing, sel.FormValues, ctx.NameToProperty, ctx.WritableProps);
                }
                await ApplyInitialFeesAsync(existing, team.JobId, team.AgegroupId, team.TeamId, ctx.BPlayersFullPaymentRequired);
                AddResult(teamResults, playerId, team.TeamId, false, team.TeamName ?? string.Empty, "Registration updated.", false);
            }
            else
            {
                var sel = selections.Last(s => s.TeamId == team.TeamId);
                await CreateNewRegistrationAsync(ctx, playerId, team, sel, teamResults, applyFormValues);
                // See ProcessSingleTeamSelectionAsync — bump the snapshot so siblings
                // later in the same family submission see the spot consumed.
                ctx.TeamRosterCounts[team.TeamId] = (ctx.TeamRosterCounts.TryGetValue(team.TeamId, out var prev) ? prev : 0) + 1;
            }

            // Stamp waitlist state on any results added above
            if (isWaitlisted)
            {
                for (var i = countBefore; i < teamResults.Count; i++)
                {
                    teamResults[i] = teamResults[i] with { IsWaitlisted = true, WaitlistTeamName = waitlistTeamName };
                }
            }
        }
    }

    private async Task UpdateExistingRegistrationAsync(PreSubmitContext ctx, Registrations regToUpdate, TSIC.Domain.Entities.Teams team, PreSubmitTeamSelectionDto sel, string playerId, List<PreSubmitTeamResultDto> teamResults, bool applyFormValues = true)
    {
        regToUpdate.Modified = DateTime.Now;
        if (string.Equals(ctx.RegistrationMode, "PP", StringComparison.OrdinalIgnoreCase))
        {
            await UpdateExistingPPModeAsync(ctx, regToUpdate, team, sel, playerId, teamResults, applyFormValues);
            return;
        }
        await UpdateExistingCACModeAsync(ctx, regToUpdate, team, sel, playerId, teamResults, applyFormValues);
    }

    private async Task UpdateExistingPPModeAsync(PreSubmitContext ctx, Registrations regToUpdate, TSIC.Domain.Entities.Teams team, PreSubmitTeamSelectionDto sel, string playerId, List<PreSubmitTeamResultDto> teamResults, bool applyFormValues = true)
    {
        var hasPayment = (regToUpdate.PaidTotal > 0) || (regToUpdate.OwedTotal > 0 && regToUpdate.PaidTotal > 0);
        if (!hasPayment)
        {
            var teamChanged = regToUpdate.AssignedTeamId != team.TeamId;
            regToUpdate.AssignedTeamId = team.TeamId;
            regToUpdate.Assignment = $"Player: {team.TeamName}";
            if (applyFormValues)
            {
                FormValueMapper.ApplyFormValues(regToUpdate, sel.FormValues, ctx.NameToProperty, ctx.WritableProps);
            }
            if (teamChanged)
            {
                // Team changed before any payment (e.g. parent went back from Payment, re-picked a
                // different team). The previously-stamped fee belongs to the OLD team, and
                // ApplyInitialFeesAsync no-ops once FeeBase>0 — which would leave the old team's
                // pricing on the moved registration and surface the wrong team's numbers on the
                // payment tab. Re-stamp from scratch for the new team: base + re-evaluated
                // discount/late-fee modifiers + processing + totals. The team's fee was already
                // confirmed configured upstream in ProcessSingleTeamSelectionAsync.
                await _feeService.ApplyNewRegistrationFeesAsync(
                    regToUpdate, team.JobId, team.AgegroupId, team.TeamId,
                    new FeeApplicationContext { IsFullPaymentRequired = ctx.BPlayersFullPaymentRequired });
            }
            else
            {
                await ApplyInitialFeesAsync(regToUpdate, team.JobId, team.AgegroupId, team.TeamId, ctx.BPlayersFullPaymentRequired);
            }
            ActivateIfFree(regToUpdate, applyFormValues);
            AddResult(teamResults, playerId, team.TeamId, false, team.TeamName ?? string.Empty, "Registration updated (team changed).", false);
            return;
        }

        var existingBase = regToUpdate.FeeBase;
        if (existingBase <= 0 && regToUpdate.AssignedTeamId.HasValue)
        {
            var resolvedExisting = await _feeService.ResolveFeeAsync(
                team.JobId, RoleConstants.Player, team.AgegroupId, team.TeamId);
            existingBase = resolvedExisting?.EffectiveBalanceDue ?? 0m;
        }
        var resolvedNew = await _feeService.ResolveFeeAsync(
            team.JobId, RoleConstants.Player, team.AgegroupId, team.TeamId);
        var newTeamBase = resolvedNew?.EffectiveBalanceDue ?? 0m;
        var sameBase = existingBase > 0 && newTeamBase > 0 && existingBase == newTeamBase;
        if (applyFormValues)
        {
            FormValueMapper.ApplyFormValues(regToUpdate, sel.FormValues, ctx.NameToProperty, ctx.WritableProps);
        }
        if (sameBase)
        {
            regToUpdate.AssignedTeamId = team.TeamId;
            regToUpdate.Assignment = $"Player: {team.TeamName}";
            await ApplyInitialFeesAsync(regToUpdate, team.JobId, team.AgegroupId, team.TeamId, ctx.BPlayersFullPaymentRequired);
            AddResult(teamResults, playerId, team.TeamId, false, team.TeamName ?? string.Empty, "Registration updated (team changed - same cost).", false);
        }
        else
        {
            if (regToUpdate.AssignedTeamId.HasValue)
            {
                var assigned = ctx.Teams.Find(x => x.TeamId == regToUpdate.AssignedTeamId.Value);
                if (assigned != null) regToUpdate.Assignment = $"Player: {assigned.TeamName}";
            }
            await ApplyInitialFeesAsync(regToUpdate, team.JobId, team.AgegroupId, regToUpdate.AssignedTeamId ?? team.TeamId, ctx.BPlayersFullPaymentRequired);
            AddResult(teamResults, playerId, regToUpdate.AssignedTeamId ?? team.TeamId, false, team.TeamName ?? string.Empty, "Registration updated (team change blocked after payment).", false);
        }
    }

    private async Task UpdateExistingCACModeAsync(PreSubmitContext ctx, Registrations regToUpdate, TSIC.Domain.Entities.Teams team, PreSubmitTeamSelectionDto sel, string playerId, List<PreSubmitTeamResultDto> teamResults, bool applyFormValues = true)
    {
        if (regToUpdate.AssignedTeamId == team.TeamId)
        {
            if (applyFormValues)
            {
                FormValueMapper.ApplyFormValues(regToUpdate, sel.FormValues, ctx.NameToProperty, ctx.WritableProps);
            }
            regToUpdate.Assignment = $"Player: {team.TeamName}";
            await ApplyInitialFeesAsync(regToUpdate, team.JobId, team.AgegroupId, team.TeamId, ctx.BPlayersFullPaymentRequired);
            ActivateIfFree(regToUpdate, applyFormValues);
            AddResult(teamResults, playerId, team.TeamId, false, team.TeamName ?? string.Empty, "Registration updated.", false);
            return;
        }

        var hasPayment = (regToUpdate.PaidTotal > 0) || (regToUpdate.OwedTotal > 0 && regToUpdate.PaidTotal > 0);
        if (hasPayment)
        {
            var newReg = new Registrations
            {
                RegistrationId = Guid.NewGuid(),
                JobId = ctx.JobId,
                FamilyUserId = ctx.FamilyUserId,
                UserId = playerId,
                AssignedTeamId = team.TeamId,
                BActive = false,
                Modified = DateTime.Now,
                RegistrationTs = DateTime.Now,
                RoleId = RoleConstants.Player,
                Assignment = $"Player: {team.TeamName}"
            };
            if (applyFormValues)
            {
                FormValueMapper.ApplyFormValues(newReg, sel.FormValues, ctx.NameToProperty, ctx.WritableProps);
            }
            newReg.BUploadedMedForm = _medForms.Exists(playerId);
            await ApplyInitialFeesAsync(newReg, team.JobId, team.AgegroupId, team.TeamId, ctx.BPlayersFullPaymentRequired);
            ActivateIfFree(newReg, applyFormValues);
            _registrations.Add(newReg);
            AddResult(teamResults, playerId, team.TeamId, false, team.TeamName ?? string.Empty, "New registration created (existing paid kept).", true);
            return;
        }

        regToUpdate.AssignedTeamId = team.TeamId;
        regToUpdate.Assignment = $"Player: {team.TeamName}";
        if (applyFormValues)
        {
            FormValueMapper.ApplyFormValues(regToUpdate, sel.FormValues, ctx.NameToProperty, ctx.WritableProps);
        }
        await ApplyInitialFeesAsync(regToUpdate, team.JobId, team.AgegroupId, team.TeamId, ctx.BPlayersFullPaymentRequired);
        AddResult(teamResults, playerId, team.TeamId, false, team.TeamName ?? string.Empty, "Registration updated (team changed).", false);
    }

    private async Task CreateNewRegistrationAsync(PreSubmitContext ctx, string playerId, TSIC.Domain.Entities.Teams team, PreSubmitTeamSelectionDto sel, List<PreSubmitTeamResultDto> teamResults, bool applyFormValues = true)
    {
        var reg = new Registrations
        {
            RegistrationId = Guid.NewGuid(),
            JobId = ctx.JobId,
            FamilyUserId = ctx.FamilyUserId,
            UserId = playerId,
            AssignedTeamId = team.TeamId,
            BActive = false,
            Modified = DateTime.Now,
            RegistrationTs = DateTime.Now,
            RoleId = RoleConstants.Player,
            Assignment = $"Player: {team.TeamName}"
        };
        if (applyFormValues)
        {
            FormValueMapper.ApplyFormValues(reg, sel.FormValues, ctx.NameToProperty, ctx.WritableProps);
        }
        reg.BUploadedMedForm = _medForms.Exists(playerId);
        await ApplyInitialFeesAsync(reg, team.JobId, team.AgegroupId, team.TeamId, ctx.BPlayersFullPaymentRequired);
        ActivateIfFree(reg, applyFormValues);
        _registrations.Add(reg);
        AddResult(teamResults, playerId, team.TeamId, false, team.TeamName ?? string.Empty, "Registration created.", true);
    }

    private async Task ApplyInitialFeesAsync(Registrations reg, Guid jobId, Guid agegroupId, Guid teamId, bool isFullPaymentRequired)
    {
        if (reg.PaidTotal > 0m) return;

        // Only set fees if not already set (new registration, not recalc)
        if (reg.FeeBase > 0m) return;

        await _feeService.ApplyNewRegistrationFeesAsync(
            reg, jobId, agegroupId, teamId,
            new FeeApplicationContext { IsFullPaymentRequired = isFullPaymentRequired });
    }

    /// <summary>
    /// Activate a free registration at final submit. A configured $0-fee event leaves nothing owed,
    /// and there is no payment to ride — so unless we flip BActive here it would stay inactive
    /// forever (off rosters, missing from the confirmation), exactly the symptom a 100% discount
    /// code hit. Mirrors legacy (PlayerBaseController: "free events start life active, otherwise
    /// start inactive and convert upon payment").
    ///
    /// Gated on <paramref name="isFinalSubmit"/> (PreSubmit's applyFormValues=true): the reserve
    /// step (applyFormValues=false) holds a roster spot BEFORE forms/waivers are complete, so a
    /// free reg must stay inactive there — identical to how a paid reg only activates at the
    /// post-forms payment step. Paid events (OwedTotal &gt; 0) are untouched and still activate via
    /// ProcessPaymentAsync's charge path.
    /// </summary>
    private static void ActivateIfFree(Registrations reg, bool isFinalSubmit)
    {
        if (isFinalSubmit && reg.OwedTotal <= 0m)
        {
            reg.BActive = true;
        }
    }

    public async Task<int> RecalculatePlayerFeesAsync(
        Guid jobId, string userId, Guid? agegroupId = null, Guid? teamId = null,
        CancellationToken ct = default)
    {
        var jobPaymentInfo = await _jobs.GetJobPaymentInfoAsync(jobId, ct)
            ?? throw new KeyNotFoundException($"Job {jobId} not found.");
        // The job-level flag is now only the BASELINE/fallback. The effective phase is
        // resolved per registration below from JobFees (team → agegroup → league); a
        // per-scope override wins over this job value. Legacy job-wide flips still work:
        // with no per-scope override set, every row resolves null → this baseline.
        var jobFullPaymentRequired = jobPaymentInfo.BPlayersFullPaymentRequired;

        var registrations = await _registrations.GetActivePlayerRegistrationsByJobAsync(jobId, ct);

        // Agegroup is resolved THROUGH the team (player → AssignedTeamId → Teams.AgegroupId).
        // Registrations.AssignedAgegroupId is obsolete and no longer read. Build a
        // team → agegroup map once, used for both the optional scope narrowing below and
        // the per-registration fee resolve in the loop.
        var teamAgegroup = (await _teams.GetTeamsWithDetailsForJobAsync(jobId, ct))
            .ToDictionary(t => t.TeamId, t => t.AgegroupId);
        Guid? AgegroupOf(Registrations r) =>
            r.AssignedTeamId.HasValue && teamAgegroup.TryGetValue(r.AssignedTeamId.Value, out var ag)
                ? ag : (Guid?)null;

        // Optional scope narrowing — the per-scope phase toggle and the LADT
        // "Push Fees to Players" button reprice a single agegroup/team. Filtered
        // in-memory off the job-wide fetch (admin action, not a hot path).
        if (agegroupId.HasValue)
            registrations = registrations.Where(r => AgegroupOf(r) == agegroupId.Value).ToList();
        if (teamId.HasValue)
            registrations = registrations.Where(r => r.AssignedTeamId == teamId.Value).ToList();

        if (registrations.Count == 0)
        {
            _logger.LogInformation(
                "No active player registrations to recalculate for job {JobId} (job-baseline phase: {Phase})",
                jobId, jobFullPaymentRequired ? "full-payment" : "deposit");
            return 0;
        }

        var updated = 0;
        foreach (var reg in registrations)
        {
            if (!reg.AssignedTeamId.HasValue) continue;
            var regAgegroupId = AgegroupOf(reg);
            if (regAgegroupId is null) continue;

            // Skip players already paid-in-full. Re-stamping FeeBase to deposit-only on
            // an unflip (true→false) would produce OwedTotal < 0 (bogus credit) for
            // voluntary-PIF registrations and any balance-due-phase registrant whose
            // payment cleared before the director changed their mind. PIF intent is
            // preserved by leaving these rows alone.
            var resolved = await _feeService.ResolveFeeAsync(
                jobId, RoleConstants.Player, regAgegroupId.Value, reg.AssignedTeamId.Value, ct);
            var fullAmount = (resolved?.EffectiveDeposit ?? 0m) + (resolved?.EffectiveBalanceDue ?? 0m);
            if (fullAmount > 0m && reg.PaidTotal >= fullAmount)
            {
                _logger.LogInformation(
                    "Skipping player registration {RegistrationId}: PaidTotal {Paid} >= full {Full} (PIF or balance-due paid).",
                    reg.RegistrationId, reg.PaidTotal, fullAmount);
                continue;
            }

            var oldFeeBase = reg.FeeBase;
            var oldFeeProcessing = reg.FeeProcessing;

            // Effective phase = per-scope override (team → agegroup → league) ?? job baseline.
            var effectiveFullPayment = resolved?.BFullPaymentRequired ?? jobFullPaymentRequired;

            await _feeService.ApplySwapFeesAsync(
                reg, jobId, regAgegroupId.Value, reg.AssignedTeamId.Value,
                new FeeApplicationContext { IsFullPaymentRequired = effectiveFullPayment }, ct);

            if (reg.FeeBase != oldFeeBase || reg.FeeProcessing != oldFeeProcessing)
            {
                reg.LebUserId = userId;
                reg.Modified = DateTime.Now;
                updated++;

                _logger.LogInformation(
                    "Player registration {RegistrationId}: FeeBase {OldFeeBase} -> {NewFeeBase}, FeeProcessing {OldFeeProcessing} -> {NewFeeProcessing}",
                    reg.RegistrationId, oldFeeBase, reg.FeeBase, oldFeeProcessing, reg.FeeProcessing);
            }
        }

        if (updated > 0)
        {
            await _registrations.SaveChangesAsync(ct);
            _logger.LogInformation(
                "Recalculated {Count} player registration(s) for job {JobId} (job-baseline phase: {Phase})",
                updated, jobId, jobFullPaymentRequired ? "full-payment" : "deposit");
        }
        else
        {
            _logger.LogInformation("No player registrations required fee updates for job {JobId}", jobId);
        }

        return updated;
    }

    /// <summary>
    /// The "blast area" for a player fee/phase change — counts active player registrations in
    /// scope WITHOUT writing. Reuses the same job-wide fetch and agegroup/team narrowing as
    /// <see cref="RecalculatePlayerFeesAsync"/> so the count can't drift from what a reprice
    /// would touch. (Paid-in-full rows are NOT excluded here: they are in the blast area; the
    /// reprice protects them, so the post-save "updated N" may be smaller than this count.)
    /// </summary>
    public async Task<int> CountActivePlayersInScopeAsync(
        Guid jobId, IReadOnlyCollection<Guid>? agegroupIds, Guid? teamId, CancellationToken ct = default)
    {
        var registrations = await _registrations.GetActivePlayerRegistrationsByJobAsync(jobId, ct);

        if (teamId.HasValue)
            return registrations.Count(r => r.AssignedTeamId == teamId.Value);

        if (agegroupIds is { Count: > 0 })
        {
            var set = agegroupIds as ISet<Guid> ?? agegroupIds.ToHashSet();
            // Agegroup is resolved through the team (Registrations.AssignedAgegroupId is obsolete).
            var teamAgegroup = (await _teams.GetTeamsWithDetailsForJobAsync(jobId, ct))
                .ToDictionary(t => t.TeamId, t => t.AgegroupId);
            return registrations.Count(r =>
                r.AssignedTeamId.HasValue
                && teamAgegroup.TryGetValue(r.AssignedTeamId.Value, out var ag)
                && set.Contains(ag));
        }

        return registrations.Count;
    }

    public async Task<SubmitByCheckResponseDto> SubmitByCheckAsync(
        Guid jobId,
        string familyUserId,
        SubmitByCheckRequestDto request,
        string callerUserId,
        CancellationToken ct = default)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));
        if (request.RegistrationIds.Count == 0)
        {
            return new SubmitByCheckResponseDto
            {
                Success = false,
                Message = "No registrations supplied.",
                UpdatedRegistrationIds = new List<Guid>(),
                Rejections = new List<SubmitByCheckRejectionDto>()
            };
        }

        var rows = await _registrations.GetByIdsAsync(request.RegistrationIds, ct);
        var updated = new List<Guid>();
        var rejections = new List<SubmitByCheckRejectionDto>();
        var changedCount = 0;
        const int CheckMethodCode = 3;

        foreach (var reg in rows)
        {
            // Family ownership + job scope: never act on rows outside the caller's family / job.
            if (reg.FamilyUserId != familyUserId || reg.JobId != jobId)
            {
                rejections.Add(new SubmitByCheckRejectionDto
                {
                    RegistrationId = reg.RegistrationId,
                    Reason = "Registration is not owned by the caller for this job."
                });
                continue;
            }

            // Lock to check path: reject rows already committed to a different payment method.
            if (reg.PaymentMethodChosen.HasValue && reg.PaymentMethodChosen.Value != CheckMethodCode)
            {
                rejections.Add(new SubmitByCheckRejectionDto
                {
                    RegistrationId = reg.RegistrationId,
                    Reason = $"Registration already committed to payment method {reg.PaymentMethodChosen.Value}."
                });
                continue;
            }

            // Idempotent: row already in target state (Check + Active) is a no-op success.
            if (reg.PaymentMethodChosen == CheckMethodCode && reg.BActive == true)
            {
                updated.Add(reg.RegistrationId);
                continue;
            }

            reg.PaymentMethodChosen = CheckMethodCode;
            reg.BActive = true;
            reg.Modified = DateTime.Now;
            reg.LebUserId = callerUserId;
            updated.Add(reg.RegistrationId);
            changedCount++;
        }

        // Detect rows missing entirely from the DB (caller passed an unknown id).
        var foundIds = rows.Select(r => r.RegistrationId).ToHashSet();
        foreach (var id in request.RegistrationIds.Where(id => !foundIds.Contains(id)))
        {
            rejections.Add(new SubmitByCheckRejectionDto
            {
                RegistrationId = id,
                Reason = "Registration not found."
            });
        }

        if (changedCount > 0)
        {
            await _registrations.SaveChangesAsync(ct);
            _logger.LogInformation(
                "SubmitByCheck stamped {Count} registration(s) Active for family {FamilyUserId} on job {JobId}.",
                changedCount, familyUserId, jobId);
        }

        return new SubmitByCheckResponseDto
        {
            Success = rejections.Count == 0,
            Message = rejections.Count == 0
                ? $"Stamped {updated.Count} registration(s) as Pay-by-Check pending."
                : $"Stamped {updated.Count}; {rejections.Count} rejected.",
            UpdatedRegistrationIds = updated,
            Rejections = rejections
        };
    }

    // Removed unused ValidateAndAdjustNextTabAsync method (was retained for backward compatibility).

    // Removed VerticalInsure-specific snapshot logic; now handled by IVerticalInsureService.

    // Removed VerticalInsure-specific eligibility queries (moved to VerticalInsureService).

    // Removed VerticalInsure-specific contact helpers (moved to VerticalInsureService).

    // Removed director contact query (moved to VerticalInsureService).

    // Removed product construction (moved to VerticalInsureService).

    // Removed player object construction (handled by VerticalInsureService).

    // Removed insurable amount computation (handled by VerticalInsureService).

    // --- Helpers copied from controller for encapsulation ---
    private static string GetRegistrationMode(string? coreRegformPlayer, string? jsonOptions)
    {
        // 1) Prefer explicit CoreRegformPlayer if present (e.g., "CAC09|..." or "PP10|...")
        var modeFromCore = ExtractModeFromCoreProfile(coreRegformPlayer);
        if (modeFromCore != null)
            return modeFromCore;

        // 2) Fallback to JsonOptions keys if provided
        var modeFromOptions = ExtractModeFromJsonOptions(jsonOptions);
        if (modeFromOptions != null)
            return modeFromOptions;

        // 3) Default to PP to maintain backward compatibility
        return "PP";
    }

    private static string? ExtractModeFromCoreProfile(string? coreRegformPlayer)
    {
        if (string.IsNullOrWhiteSpace(coreRegformPlayer) || coreRegformPlayer == "0" || coreRegformPlayer == "1")
            return null;

        var firstPart = coreRegformPlayer!.Split('|')[0].Trim();
        if (firstPart.StartsWith("CAC", StringComparison.OrdinalIgnoreCase) ||
            firstPart.Equals("CAC", StringComparison.OrdinalIgnoreCase))
        {
            return "CAC";
        }
        if (firstPart.StartsWith("PP", StringComparison.OrdinalIgnoreCase) ||
            firstPart.Equals("PP", StringComparison.OrdinalIgnoreCase))
        {
            return "PP";
        }
        return null;
    }

    private static string? ExtractModeFromJsonOptions(string? jsonOptions)
    {
        if (string.IsNullOrWhiteSpace(jsonOptions))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(jsonOptions);
            var root = doc.RootElement;
            var keys = new[] { "registrationMode", "profileMode", "regProfileType", "registrationType" };
            foreach (var k in keys)
            {
                if (!root.TryGetProperty(k, out var el)) continue;
                var s = el.GetString();
                if (string.IsNullOrWhiteSpace(s)) continue;
                s = s.Trim();
                if (s.Equals("CAC", StringComparison.OrdinalIgnoreCase)) return "CAC";
                if (s.Equals("PP", StringComparison.OrdinalIgnoreCase)) return "PP";
            }
        }
        catch (Exception)
        {
            // Ignore malformed jsonOptions
        }
        return null;
    }

}


