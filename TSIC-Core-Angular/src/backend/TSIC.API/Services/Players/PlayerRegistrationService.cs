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

    private sealed class PreSubmitContext
    {
        public Guid JobId { get; init; }
        public string FamilyUserId { get; init; } = string.Empty;
        public List<TSIC.Domain.Entities.Teams> Teams { get; init; } = new();
        public Dictionary<Guid, int> TeamRosterCounts { get; init; } = new();
        public string RegistrationMode { get; init; } = "PP";
        public string? MetadataJson { get; init; }
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
        ITeamPlacementService placement)
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
        var teamRosterCounts = await _registrations.GetActiveTeamRosterCountsAsync(jobId, teamIds);

        var jobEntity = await _jobs.GetPreSubmitMetadataAsync(jobId);

        var metadataJson = jobEntity?.PlayerProfileMetadataJson;
        var registrationMode = GetRegistrationMode(jobEntity?.CoreRegformPlayer, jobEntity?.JsonOptions);

        var nameToProperty = FormValueMapper.BuildFieldNameToPropertyMap(metadataJson);
        var writableProps = FormValueMapper.BuildWritablePropertyMap();

        var playerIds = request.TeamSelections.Select(ts => ts.PlayerId).Distinct().ToList();
        var existingRegs = await _registrations.GetFamilyRegistrationsForPlayersAsync(jobId, familyUserId, playerIds);

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
            NameToProperty = nameToProperty,
            WritableProps = writableProps,
            ExistingByPlayer = existingByPlayer,
            ExistingByPlayerTeam = existingByPlayerTeam
        };
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

    private void AddResult(List<PreSubmitTeamResultDto> results, string playerId, Guid teamId, bool isFull, string teamName, string message, bool created)
    {
        results.Add(new PreSubmitTeamResultDto
        {
            PlayerId = playerId,
            TeamId = teamId,
            IsFull = isFull,
            TeamName = teamName,
            Message = message,
            RegistrationCreated = created
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
        if (isFull)
        {
            try
            {
                var rosterPlacement = await _placement.ResolveRosterPlacementAsync(
                    ctx.JobId, team.TeamId, ctx.FamilyUserId);
                if (rosterPlacement.IsWaitlisted)
                {
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
        if (regToUpdate != null)
        {
            await UpdateExistingRegistrationAsync(ctx, regToUpdate, team, sel, playerId, teamResults, applyFormValues);
        }
        else
        {
            await CreateNewRegistrationAsync(ctx, playerId, team, sel, teamResults, applyFormValues);
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
            if (isFull)
            {
                try
                {
                    var rosterPlacement = await _placement.ResolveRosterPlacementAsync(
                        ctx.JobId, team.TeamId, ctx.FamilyUserId);
                    if (rosterPlacement.IsWaitlisted)
                    {
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
            if (ctx.ExistingByPlayerTeam.TryGetValue((playerId, effectiveTeamId), out var existing))
            {
                existing.Modified = DateTime.UtcNow;
                var sel = selections.Last(s => s.TeamId == team.TeamId);
                if (applyFormValues)
                {
                    FormValueMapper.ApplyFormValues(existing, sel.FormValues, ctx.NameToProperty, ctx.WritableProps);
                }
                await ApplyInitialFeesAsync(existing, team.JobId, team.AgegroupId, team.TeamId);
                AddResult(teamResults, playerId, team.TeamId, false, team.TeamName ?? string.Empty, "Registration updated.", false);
            }
            else
            {
                var sel = selections.Last(s => s.TeamId == team.TeamId);
                await CreateNewRegistrationAsync(ctx, playerId, team, sel, teamResults, applyFormValues);
            }
        }
    }

    private async Task UpdateExistingRegistrationAsync(PreSubmitContext ctx, Registrations regToUpdate, TSIC.Domain.Entities.Teams team, PreSubmitTeamSelectionDto sel, string playerId, List<PreSubmitTeamResultDto> teamResults, bool applyFormValues = true)
    {
        regToUpdate.Modified = DateTime.UtcNow;
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
            regToUpdate.AssignedTeamId = team.TeamId;
            regToUpdate.Assignment = $"Player: {team.TeamName}";
            if (applyFormValues)
            {
                FormValueMapper.ApplyFormValues(regToUpdate, sel.FormValues, ctx.NameToProperty, ctx.WritableProps);
            }
            await ApplyInitialFeesAsync(regToUpdate, team.JobId, team.AgegroupId, team.TeamId);
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
            await ApplyInitialFeesAsync(regToUpdate, team.JobId, team.AgegroupId, team.TeamId);
            AddResult(teamResults, playerId, team.TeamId, false, team.TeamName ?? string.Empty, "Registration updated (team changed - same cost).", false);
        }
        else
        {
            if (regToUpdate.AssignedTeamId.HasValue)
            {
                var assigned = ctx.Teams.Find(x => x.TeamId == regToUpdate.AssignedTeamId.Value);
                if (assigned != null) regToUpdate.Assignment = $"Player: {assigned.TeamName}";
            }
            await ApplyInitialFeesAsync(regToUpdate, team.JobId, team.AgegroupId, regToUpdate.AssignedTeamId ?? team.TeamId);
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
            await ApplyInitialFeesAsync(regToUpdate, team.JobId, team.AgegroupId, team.TeamId);
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
                Modified = DateTime.UtcNow,
                RegistrationTs = DateTime.UtcNow,
                RoleId = RoleConstants.Player,
                Assignment = $"Player: {team.TeamName}"
            };
            if (applyFormValues)
            {
                FormValueMapper.ApplyFormValues(newReg, sel.FormValues, ctx.NameToProperty, ctx.WritableProps);
            }
            await ApplyInitialFeesAsync(newReg, team.JobId, team.AgegroupId, team.TeamId);
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
        await ApplyInitialFeesAsync(regToUpdate, team.JobId, team.AgegroupId, team.TeamId);
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
            Modified = DateTime.UtcNow,
            RegistrationTs = DateTime.UtcNow,
            RoleId = RoleConstants.Player,
            Assignment = $"Player: {team.TeamName}"
        };
        if (applyFormValues)
        {
            FormValueMapper.ApplyFormValues(reg, sel.FormValues, ctx.NameToProperty, ctx.WritableProps);
        }
        await ApplyInitialFeesAsync(reg, team.JobId, team.AgegroupId, team.TeamId);
        _registrations.Add(reg);
        AddResult(teamResults, playerId, team.TeamId, false, team.TeamName ?? string.Empty, "Registration created, pending payment.", true);
    }

    private async Task ApplyInitialFeesAsync(Registrations reg, Guid jobId, Guid agegroupId, Guid teamId)
    {
        if (reg.PaidTotal > 0m) return;

        // Only set fees if not already set (new registration, not recalc)
        if (reg.FeeBase > 0m) return;

        await _feeService.ApplyNewRegistrationFeesAsync(
            reg, jobId, agegroupId, teamId, new FeeApplicationContext());
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


