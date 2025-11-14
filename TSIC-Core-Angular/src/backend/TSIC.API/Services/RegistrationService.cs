using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using System.Globalization;
using System.Text.Json;
using TSIC.API.Constants;
using TSIC.API.Dtos;
using TSIC.Infrastructure.Data.SqlDbContext;
using TSIC.Domain.Entities;

namespace TSIC.API.Services;

public class RegistrationService : IRegistrationService
{
    private readonly ILogger<RegistrationService> _logger;
    private readonly SqlDbContext _db;
    private readonly IFeeResolverService _feeResolver;
    private readonly IFeeCalculatorService _feeCalculator;
    private readonly IVerticalInsureService _verticalInsure;

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

    public RegistrationService(
        ILogger<RegistrationService> logger,
        SqlDbContext db,
        IFeeResolverService feeResolver,
        IFeeCalculatorService feeCalculator,
        IVerticalInsureService verticalInsure)
    {
        _logger = logger;
        _db = db;
        _feeResolver = feeResolver;
        _feeCalculator = feeCalculator;
        _verticalInsure = verticalInsure;
    }

    public async Task<PreSubmitRegistrationResponseDto> PreSubmitAsync(Guid jobId, string familyUserId, PreSubmitRegistrationRequestDto request, string callerUserId)
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

        await _db.SaveChangesAsync();
        var response = new PreSubmitRegistrationResponseDto
        {
            TeamResults = teamResults,
            NextTab = teamResults.Exists(r => r.IsFull) ? "Team" : "Payment"
        };

        await ValidateAndAdjustNextTabAsync(ctx.MetadataJson, request.TeamSelections, response);
        // Delegate insurance offer construction to VerticalInsure service.
        response.Insurance = await _verticalInsure.BuildOfferAsync(ctx.JobId, ctx.FamilyUserId);
        return response;
    }

    private async Task<PreSubmitContext> BuildPreSubmitContextAsync(Guid jobId, string familyUserId, PreSubmitRegistrationRequestDto request)
    {
        var teamIds = request.TeamSelections.Select(ts => ts.TeamId).Distinct().ToList();
        var teams = await _db.Teams.Where(t => t.JobId == jobId && teamIds.Contains(t.TeamId)).ToListAsync();
        var teamRosterCounts = await _db.Registrations
            .Where(r => r.JobId == jobId && r.AssignedTeamId.HasValue && teamIds.Contains(r.AssignedTeamId.Value) && r.BActive == true)
            .GroupBy(r => r.AssignedTeamId!.Value)
            .ToDictionaryAsync(g => g.Key, g => g.Count());

        var jobEntity = await _db.Jobs
            .AsNoTracking()
            .Where(j => j.JobId == jobId)
            .Select(j => new { j.PlayerProfileMetadataJson, j.JsonOptions, j.CoreRegformPlayer })
            .SingleOrDefaultAsync();

        var metadataJson = jobEntity?.PlayerProfileMetadataJson;
        var registrationMode = GetRegistrationMode(jobEntity?.CoreRegformPlayer, jobEntity?.JsonOptions);

        var nameToProperty = BuildFieldNameToPropertyMap(metadataJson);
        var writableProps = BuildWritablePropertyMap();

        var playerIds = request.TeamSelections.Select(ts => ts.PlayerId).Distinct().ToList();
        var existingRegs = await _db.Registrations
            .Where(r => r.JobId == jobId && r.FamilyUserId == familyUserId && r.UserId != null && playerIds.Contains(r.UserId))
            .OrderByDescending(r => r.Modified)
            .ToListAsync();

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

    private async Task ProcessPlayerSelectionsAsync(PreSubmitContext ctx, string playerId, List<PreSubmitTeamSelectionDto> selections, List<PreSubmitTeamResultDto> teamResults)
    {
        var desiredTeamIds = selections.Select(s => s.TeamId).Distinct().ToList();
        if (desiredTeamIds.Count == 0) return;

        if (desiredTeamIds.Count == 1)
        {
            await ProcessSingleTeamSelectionAsync(ctx, playerId, selections, teamResults);
        }
        else
        {
            await ProcessMultiTeamSelectionsAsync(ctx, playerId, selections, teamResults);
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

    private async Task ProcessSingleTeamSelectionAsync(PreSubmitContext ctx, string playerId, List<PreSubmitTeamSelectionDto> selections, List<PreSubmitTeamResultDto> teamResults)
    {
        var teamId = selections.Select(s => s.TeamId).First();
        var team = ctx.Teams.Find(t => t.TeamId == teamId);
        if (team == null)
        {
            AddResult(teamResults, playerId, teamId, true, "Unknown", "Team not found.", false);
            return;
        }
        var rosterCount = ctx.TeamRosterCounts.TryGetValue(team.TeamId, out var cnt) ? cnt : 0;
        var isFull = team.MaxCount > 0 && rosterCount >= team.MaxCount;
        if (isFull)
        {
            AddResult(teamResults, playerId, team.TeamId, true, team.TeamName ?? string.Empty, "Team roster is full.", false);
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
        if (regToUpdate != null)
        {
            await UpdateExistingRegistrationAsync(ctx, regToUpdate, team, sel, playerId, teamResults);
        }
        else
        {
            await CreateNewRegistrationAsync(ctx, playerId, team, sel, teamResults);
        }
    }

    private async Task ProcessMultiTeamSelectionsAsync(PreSubmitContext ctx, string playerId, List<PreSubmitTeamSelectionDto> selections, List<PreSubmitTeamResultDto> teamResults)
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
            var rosterCount = ctx.TeamRosterCounts.TryGetValue(team.TeamId, out var cnt) ? cnt : 0;
            var isFull = team.MaxCount > 0 && rosterCount >= team.MaxCount;
            if (isFull)
            {
                AddResult(teamResults, playerId, team.TeamId, true, team.TeamName ?? string.Empty, "Team roster is full.", false);
                continue;
            }
            if (ctx.ExistingByPlayerTeam.TryGetValue((playerId, team.TeamId), out var existing))
            {
                existing.Modified = DateTime.UtcNow;
                var sel = selections.Last(s => s.TeamId == team.TeamId);
                ApplyFormValues(existing, sel, ctx.NameToProperty, ctx.WritableProps);
                await ApplyInitialFeesAsync(existing, team.TeamId, team.FeeBase, team.PerRegistrantFee);
                AddResult(teamResults, playerId, team.TeamId, false, team.TeamName ?? string.Empty, "Registration updated.", false);
            }
            else
            {
                var sel = selections.Last(s => s.TeamId == team.TeamId);
                await CreateNewRegistrationAsync(ctx, playerId, team, sel, teamResults);
            }
        }
    }

    private async Task UpdateExistingRegistrationAsync(PreSubmitContext ctx, Registrations regToUpdate, TSIC.Domain.Entities.Teams team, PreSubmitTeamSelectionDto sel, string playerId, List<PreSubmitTeamResultDto> teamResults)
    {
        regToUpdate.Modified = DateTime.UtcNow;
        if (string.Equals(ctx.RegistrationMode, "PP", StringComparison.OrdinalIgnoreCase))
        {
            await UpdateExistingPPModeAsync(ctx, regToUpdate, team, sel, playerId, teamResults);
            return;
        }
        await UpdateExistingCACModeAsync(ctx, regToUpdate, team, sel, playerId, teamResults);
    }

    private async Task UpdateExistingPPModeAsync(PreSubmitContext ctx, Registrations regToUpdate, TSIC.Domain.Entities.Teams team, PreSubmitTeamSelectionDto sel, string playerId, List<PreSubmitTeamResultDto> teamResults)
    {
        var hasPayment = (regToUpdate.PaidTotal > 0) || (regToUpdate.OwedTotal > 0 && regToUpdate.PaidTotal > 0);
        if (!hasPayment)
        {
            regToUpdate.AssignedTeamId = team.TeamId;
            regToUpdate.Assignment = $"Player: {team.TeamName}";
            ApplyFormValues(regToUpdate, sel, ctx.NameToProperty, ctx.WritableProps);
            await ApplyInitialFeesAsync(regToUpdate, team.TeamId, team.FeeBase, team.PerRegistrantFee);
            AddResult(teamResults, playerId, team.TeamId, false, team.TeamName ?? string.Empty, "Registration updated (team changed).", false);
            return;
        }

        var existingBase = regToUpdate.FeeBase;
        if (existingBase <= 0 && regToUpdate.AssignedTeamId.HasValue)
        {
            existingBase = await ResolveTeamBaseFeeAsync(team.TeamId);
        }
        var newTeamBase = team.FeeBase ?? team.PerRegistrantFee ?? await ResolveTeamBaseFeeAsync(team.TeamId);
        var sameBase = existingBase > 0 && newTeamBase > 0 && existingBase == newTeamBase;
        ApplyFormValues(regToUpdate, sel, ctx.NameToProperty, ctx.WritableProps);
        if (sameBase)
        {
            regToUpdate.AssignedTeamId = team.TeamId;
            regToUpdate.Assignment = $"Player: {team.TeamName}";
            await ApplyInitialFeesAsync(regToUpdate, team.TeamId, team.FeeBase, team.PerRegistrantFee);
            AddResult(teamResults, playerId, team.TeamId, false, team.TeamName ?? string.Empty, "Registration updated (team changed - same cost).", false);
        }
        else
        {
            if (regToUpdate.AssignedTeamId.HasValue)
            {
                var assigned = ctx.Teams.Find(x => x.TeamId == regToUpdate.AssignedTeamId.Value);
                if (assigned != null) regToUpdate.Assignment = $"Player: {assigned.TeamName}";
            }
            await ApplyInitialFeesAsync(regToUpdate, regToUpdate.AssignedTeamId ?? team.TeamId, team.FeeBase, team.PerRegistrantFee);
            AddResult(teamResults, playerId, regToUpdate.AssignedTeamId ?? team.TeamId, false, team.TeamName ?? string.Empty, "Registration updated (team change blocked after payment).", false);
        }
    }

    private async Task UpdateExistingCACModeAsync(PreSubmitContext ctx, Registrations regToUpdate, TSIC.Domain.Entities.Teams team, PreSubmitTeamSelectionDto sel, string playerId, List<PreSubmitTeamResultDto> teamResults)
    {
        if (regToUpdate.AssignedTeamId == team.TeamId)
        {
            ApplyFormValues(regToUpdate, sel, ctx.NameToProperty, ctx.WritableProps);
            regToUpdate.Assignment = $"Player: {team.TeamName}";
            await ApplyInitialFeesAsync(regToUpdate, team.TeamId, team.FeeBase, team.PerRegistrantFee);
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
            ApplyFormValues(newReg, sel, ctx.NameToProperty, ctx.WritableProps);
            await ApplyInitialFeesAsync(newReg, team.TeamId, team.FeeBase, team.PerRegistrantFee);
            _db.Registrations.Add(newReg);
            AddResult(teamResults, playerId, team.TeamId, false, team.TeamName ?? string.Empty, "New registration created (existing paid kept).", true);
            return;
        }

        regToUpdate.AssignedTeamId = team.TeamId;
        regToUpdate.Assignment = $"Player: {team.TeamName}";
        ApplyFormValues(regToUpdate, sel, ctx.NameToProperty, ctx.WritableProps);
        await ApplyInitialFeesAsync(regToUpdate, team.TeamId, team.FeeBase, team.PerRegistrantFee);
        AddResult(teamResults, playerId, team.TeamId, false, team.TeamName ?? string.Empty, "Registration updated (team changed).", false);
    }

    private async Task CreateNewRegistrationAsync(PreSubmitContext ctx, string playerId, TSIC.Domain.Entities.Teams team, PreSubmitTeamSelectionDto sel, List<PreSubmitTeamResultDto> teamResults)
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
        ApplyFormValues(reg, sel, ctx.NameToProperty, ctx.WritableProps);
        await ApplyInitialFeesAsync(reg, team.TeamId, team.FeeBase, team.PerRegistrantFee);
        _db.Registrations.Add(reg);
        AddResult(teamResults, playerId, team.TeamId, false, team.TeamName ?? string.Empty, "Registration created, pending payment.", true);
    }

    private async Task<decimal> ResolveTeamBaseFeeAsync(Guid teamId)
    {
        var cached = await _db.Teams.Where(x => x.TeamId == teamId).Select(x => new { x.FeeBase, x.PerRegistrantFee }).FirstOrDefaultAsync();
        if (cached != null)
        {
            var v = cached.FeeBase ?? cached.PerRegistrantFee ?? 0m;
            if (v > 0) return v;
        }
        return await _feeResolver.ResolveBaseFeeForTeamAsync(teamId);
    }

    private async Task ApplyInitialFeesAsync(Registrations reg, Guid teamId, decimal? teamFeeBase, decimal? teamPerRegistrantFee)
    {
        var paid = reg.PaidTotal;
        if (paid > 0m) return;

        var baseFee = teamFeeBase ?? teamPerRegistrantFee ?? 0m;
        if (baseFee <= 0m)
        {
            baseFee = await ResolveTeamBaseFeeAsync(teamId);
        }
        if (baseFee > 0m)
        {
            if (reg.FeeBase <= 0m) reg.FeeBase = baseFee;
            var (processing, total) = _feeCalculator.ComputeTotals(reg.FeeBase, reg.FeeDiscount, reg.FeeDonation,
                (reg.FeeProcessing > 0m) ? reg.FeeProcessing : null);
            if (reg.FeeProcessing <= 0m) reg.FeeProcessing = processing;
            reg.FeeTotal = total;
            reg.OwedTotal = reg.FeeTotal - reg.PaidTotal;
        }
    }

    private async Task ValidateAndAdjustNextTabAsync(string? metadataJson, List<PreSubmitTeamSelectionDto> selections, PreSubmitRegistrationResponseDto response)
    {
        try
        {
            var validationErrors = ValidatePlayerFormValues(metadataJson, selections);
            if (validationErrors.Count > 0)
            {
                response.ValidationErrors = validationErrors;
                if (!response.HasFullTeams)
                {
                    response.NextTab = "Forms";
                }
            }
        }
        catch (Exception vex)
        {
            _logger.LogWarning(vex, "[PreSubmit] Server-side metadata validation failed (non-fatal). Skipping.");
        }
    }

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
        if (!string.IsNullOrWhiteSpace(coreRegformPlayer) && coreRegformPlayer != "0" && coreRegformPlayer != "1")
        {
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
        }

        // 2) Fallback to JsonOptions keys if provided
        if (!string.IsNullOrWhiteSpace(jsonOptions))
        {
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
                // Ignore malformed jsonOptions; default to PP below
            }
        }

        // 3) Default to PP to maintain backward compatibility
        return "PP";
    }

    private static Dictionary<string, string> BuildFieldNameToPropertyMap(string? metadataJson)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(metadataJson)) return map;
        try
        {
            using var doc = JsonDocument.Parse(metadataJson);
            if (doc.RootElement.TryGetProperty("fields", out var fieldsEl) && fieldsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var f in fieldsEl.EnumerateArray())
                {
                    var name = f.TryGetProperty("name", out var nEl) ? nEl.GetString() : null;
                    var dbCol = f.TryGetProperty("dbColumn", out var dEl) ? dEl.GetString() : null;
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    // Exclude fields marked hidden or adminOnly via visibility
                    if (f.TryGetProperty("visibility", out var visEl) && visEl.ValueKind == JsonValueKind.String)
                    {
                        var vis = visEl.GetString();
                        if (string.Equals(vis, "hidden", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(vis, "adminOnly", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }
                    }
                    // Do not include admin-only fields in the writable map
                    if (TryGetPropertyCI(f, "adminOnly", out var adminEl))
                    {
                        var adminFlag = adminEl.ValueKind == JsonValueKind.True ||
                                         (adminEl.ValueKind == JsonValueKind.String && bool.TryParse(adminEl.GetString(), out var b) && b);
                        if (adminFlag) continue;
                    }
                    map[name!] = !string.IsNullOrWhiteSpace(dbCol) ? dbCol! : name!;
                }
            }
        }
        catch (Exception)
        {
            // Ignore malformed metadata and return empty map
        }
        return map;
    }

    // Helper: case-insensitive property lookup on JsonElement objects
    private static bool TryGetPropertyCI(JsonElement obj, string name, out JsonElement value)
    {
        value = default;
        if (obj.ValueKind != JsonValueKind.Object) return false;
        foreach (var p in obj.EnumerateObject())
        {
            if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = p.Value;
                return true;
            }
        }
        return false;
    }

    private static Dictionary<string, System.Reflection.PropertyInfo> BuildWritablePropertyMap()
    {
        var props = typeof(Registrations).GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        var dict = new Dictionary<string, System.Reflection.PropertyInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in props)
        {
            if (!p.CanWrite) continue;
            if (!ShouldIncludeProperty(p)) continue;
            if (string.Equals(p.Name, nameof(Registrations.AssignedTeamId), StringComparison.OrdinalIgnoreCase)) continue;
            dict[p.Name] = p;
        }
        return dict;
    }

    private static bool ShouldIncludeProperty(System.Reflection.PropertyInfo p)
    {
        if (!p.CanRead || p.GetIndexParameters().Length > 0) return false;
        var name = p.Name;
        var type = p.PropertyType;

        if (typeof(System.Collections.IEnumerable).IsAssignableFrom(type) && type != typeof(string))
        {
            return false;
        }

        static bool IsSimple(Type t)
        {
            var u = Nullable.GetUnderlyingType(t) ?? t;
            return u.IsPrimitive || u.IsEnum || u == typeof(string) || u == typeof(decimal) || u == typeof(DateTime) || u == typeof(Guid);
        }
        if (!IsSimple(type)) return false;
        if (name is nameof(Registrations.RegistrationAi)
            or nameof(Registrations.RegistrationId)
            or nameof(Registrations.RegistrationTs)
            or nameof(Registrations.RoleId)
            or nameof(Registrations.UserId)
            or nameof(Registrations.FamilyUserId)
            or nameof(Registrations.BActive)
            or nameof(Registrations.BConfirmationSent)
            or nameof(Registrations.JobId)
            or nameof(Registrations.LebUserId)
            or nameof(Registrations.Modified)
            or nameof(Registrations.RegistrationFormName)
            or nameof(Registrations.PaymentMethodChosen)
            or nameof(Registrations.FeeProcessing)
            or nameof(Registrations.FeeBase)
            or nameof(Registrations.FeeDiscount)
            or nameof(Registrations.FeeDiscountMp)
            or nameof(Registrations.FeeDonation)
            or nameof(Registrations.FeeLatefee)
            or nameof(Registrations.FeeTotal)
            or nameof(Registrations.OwedTotal)
            or nameof(Registrations.PaidTotal)
            or nameof(Registrations.CustomerId)
            or nameof(Registrations.DiscountCodeId)
            or nameof(Registrations.AssignedTeamId)
            or nameof(Registrations.AssignedAgegroupId)
            or nameof(Registrations.AssignedCustomerId)
            or nameof(Registrations.AssignedDivId)
            or nameof(Registrations.AssignedLeagueId)
            or nameof(Registrations.RegformId)
            or nameof(Registrations.AccountingApplyToSummaries))
        {
            return false;
        }
        if (!string.Equals(name, nameof(Registrations.SportAssnId), StringComparison.Ordinal) &&
            (name.EndsWith("Id", StringComparison.Ordinal) || name.EndsWith("ID", StringComparison.Ordinal)))
        {
            return false;
        }
        // Allow waiver acceptance booleans to be written (client injects them). Still exclude uploaded flags.
        if (name.StartsWith("BUploaded", StringComparison.Ordinal))
        {
            return false;
        }
        if (name.StartsWith("Adn", StringComparison.Ordinal) || name.StartsWith("Regsaver", StringComparison.Ordinal))
        {
            return false;
        }
        return true;
    }

    private static void ApplyFormValues(Registrations reg, PreSubmitTeamSelectionDto sel,
        Dictionary<string, string> nameToProperty,
        Dictionary<string, System.Reflection.PropertyInfo> writableProps)
    {
        if (sel.FormValues == null || sel.FormValues.Count == 0) return;
        foreach (var kvp in sel.FormValues)
        {
            var incomingName = kvp.Key;
            var jsonVal = kvp.Value;
            var targetName = ResolveTargetPropertyName(incomingName, nameToProperty, writableProps);
            if (targetName == null) continue;
            if (!writableProps.TryGetValue(targetName, out var prop)) continue;
            if (TryConvertAndAssign(jsonVal, prop.PropertyType, out var converted))
            {
                prop.SetValue(reg, converted);
            }
        }
    }

    private static string? ResolveTargetPropertyName(string incoming,
        Dictionary<string, string> nameToProperty,
        Dictionary<string, System.Reflection.PropertyInfo> writable)
    {
        if (nameToProperty.TryGetValue(incoming, out var target) && writable.ContainsKey(target))
        {
            return target;
        }
        if (writable.ContainsKey(incoming)) return incoming;
        return null;
    }

    private static bool TryConvertAndAssign(JsonElement json, Type targetType, out object? boxed)
    {
        boxed = null;
        var t = Nullable.GetUnderlyingType(targetType) ?? targetType;
        try
        {
            if (t == typeof(string))
            {
                boxed = json.ValueKind == JsonValueKind.Null ? null : json.ToString();
                return true;
            }
            if (t == typeof(int))
            {
                if (json.ValueKind == JsonValueKind.String && int.TryParse(json.GetString(), out var iv)) { boxed = iv; return true; }
                if (json.TryGetInt32(out var i)) { boxed = i; return true; }
            }
            if (t == typeof(long))
            {
                if (json.ValueKind == JsonValueKind.String && long.TryParse(json.GetString(), out var lv)) { boxed = lv; return true; }
                if (json.TryGetInt64(out var l)) { boxed = l; return true; }
            }
            if (t == typeof(decimal))
            {
                if (json.ValueKind == JsonValueKind.String && decimal.TryParse(json.GetString(), out var dv)) { boxed = dv; return true; }
                if (json.TryGetDecimal(out var d)) { boxed = d; return true; }
            }
            if (t == typeof(double))
            {
                if (json.ValueKind == JsonValueKind.String && double.TryParse(json.GetString(), out var xv)) { boxed = xv; return true; }
                if (json.TryGetDouble(out var x)) { boxed = x; return true; }
            }
            if (t == typeof(bool))
            {
                if (json.ValueKind == JsonValueKind.String && bool.TryParse(json.GetString(), out var bv)) { boxed = bv; return true; }
                if (json.ValueKind == JsonValueKind.Number) { boxed = json.GetInt32() != 0; return true; }
                if (json.ValueKind == JsonValueKind.True || json.ValueKind == JsonValueKind.False) { boxed = json.GetBoolean(); return true; }
            }
            if (t == typeof(DateTime) && json.ValueKind == JsonValueKind.String && DateTime.TryParse(json.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            {
                boxed = dt; return true;
            }
            if (t == typeof(Guid) && json.ValueKind == JsonValueKind.String && Guid.TryParse(json.GetString(), out var g))
            {
                boxed = g; return true;
            }
        }
        catch { return false; }
        return false;
    }

    private static List<PreSubmitValidationErrorDto> ValidatePlayerFormValues(string? metadataJson, List<PreSubmitTeamSelectionDto> selections)
    {
        var errors = new List<PreSubmitValidationErrorDto>();
        if (string.IsNullOrWhiteSpace(metadataJson) || selections.Count == 0) return errors;
        JsonDocument doc;
        try { doc = JsonDocument.Parse(metadataJson); } catch { return errors; }
        if (!doc.RootElement.TryGetProperty("fields", out var fieldsEl) || fieldsEl.ValueKind != JsonValueKind.Array) return errors;

        var schemas = BuildSchemas(fieldsEl);
        // Merge all FormValues per player across selections (case-insensitive keys, last write wins)
        var mergedValuesByPlayer = selections
            .Where(s => s.FormValues != null)
            .GroupBy(s => s.PlayerId)
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    var dict = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
                    foreach (var sel in g)
                    {
                        foreach (var kv in sel.FormValues!)
                        {
                            dict[kv.Key] = kv.Value; // last write wins
                        }
                    }
                    return dict;
                }
            );

        foreach (var kv in mergedValuesByPlayer)
        {
            var playerId = kv.Key;
            var formValues = kv.Value;
            ValidateSchemasForPlayer(schemas, playerId, formValues, errors);
        }
        return errors;
    }

    private static List<(string Name, bool Required, string Type, string? ConditionField, JsonElement? ConditionValue, string? ConditionOp, HashSet<string> Options)> BuildSchemas(JsonElement fieldsEl)
    {
        var list = new List<(string, bool, string, string?, JsonElement?, string?, HashSet<string>)>();
        foreach (var f in fieldsEl.EnumerateArray())
        {
            var name = f.TryGetProperty("name", out var nEl) ? nEl.GetString() : null;
            if (string.IsNullOrWhiteSpace(name)) continue;
            if (f.TryGetProperty("visibility", out var visEl) && visEl.ValueKind == JsonValueKind.String &&
                (string.Equals(visEl.GetString(), "hidden", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(visEl.GetString(), "adminOnly", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }
            // Skip admin-only fields so they are not required/validated for player form submission.
            if (TryGetPropertyCI(f, "adminOnly", out var adminEl))
            {
                var adminFlag = adminEl.ValueKind == JsonValueKind.True ||
                                 (adminEl.ValueKind == JsonValueKind.String && bool.TryParse(adminEl.GetString(), out var b) && b);
                if (adminFlag) continue;
            }
            var required = f.TryGetProperty("required", out var reqEl) && reqEl.ValueKind == JsonValueKind.True;
            if (!required && f.TryGetProperty("validation", out var valEl) && valEl.ValueKind == JsonValueKind.Object)
            {
                if (valEl.TryGetProperty("required", out var rEl) && rEl.ValueKind == JsonValueKind.True) required = true;
                if (valEl.TryGetProperty("requiredTrue", out var rtEl) && rtEl.ValueKind == JsonValueKind.True) required = true;
            }
            var type = f.TryGetProperty("type", out var tEl) ? (tEl.GetString() ?? "text") : "text";
            string? condField = null; JsonElement? condValue = null; string? condOp = null;
            if (f.TryGetProperty("condition", out var cEl) && cEl.ValueKind == JsonValueKind.Object)
            {
                condField = cEl.TryGetProperty("field", out var cfEl) ? cfEl.GetString() : null;
                if (cEl.TryGetProperty("value", out var cvEl)) condValue = cvEl;
                condOp = cEl.TryGetProperty("operator", out var coEl) ? coEl.GetString() : null;
            }
            var options = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (f.TryGetProperty("options", out var optEl) && optEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var o in optEl.EnumerateArray())
                {
                    if (o.ValueKind == JsonValueKind.String) options.Add(o.GetString()!);
                }
            }
            list.Add((name!, required, type!.ToLowerInvariant(), condField, condValue, condOp, options));
        }
        return list;
    }

    private static void ValidateSchemasForPlayer(
        List<(string Name, bool Required, string Type, string? ConditionField, JsonElement? ConditionValue, string? ConditionOp, HashSet<string> Options)> schemas,
        string playerId,
        Dictionary<string, JsonElement> formValues,
        List<PreSubmitValidationErrorDto> errors)
    {
        // Make field key lookup case-insensitive so client differences in casing (e.g., bWaiverSigned1 vs BWaiverSigned1) don't cause false "Required" errors.
        var ciFormValues = new Dictionary<string, JsonElement>(formValues, StringComparer.OrdinalIgnoreCase);
        foreach (var schema in schemas)
        {
            if (schema.ConditionField != null && schema.ConditionValue.HasValue)
            {
                ciFormValues.TryGetValue(schema.ConditionField, out var otherVal);
                var condOk = otherVal.ValueKind == schema.ConditionValue.Value.ValueKind && otherVal.ToString() == schema.ConditionValue.Value.ToString();
                if (!condOk) continue;
            }
            ciFormValues.TryGetValue(schema.Name, out var valEl);
            var present = valEl.ValueKind != JsonValueKind.Undefined && valEl.ValueKind != JsonValueKind.Null && valEl.ToString().Trim().Length > 0;
            var rawStr = valEl.ToString();
            switch (schema.Type)
            {
                case "number":
                    if (schema.Required && !present)
                    {
                        errors.Add(new PreSubmitValidationErrorDto { PlayerId = playerId, Field = schema.Name, Message = "Required" });
                        break;
                    }
                    if (!present) break;
                    if (!double.TryParse(rawStr, out _)) errors.Add(new PreSubmitValidationErrorDto { PlayerId = playerId, Field = schema.Name, Message = "Must be a number" });
                    break;
                case "date":
                    if (schema.Required && !present)
                    {
                        errors.Add(new PreSubmitValidationErrorDto { PlayerId = playerId, Field = schema.Name, Message = "Required" });
                        break;
                    }
                    if (!present) break;
                    if (!DateTime.TryParse(rawStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                        errors.Add(new PreSubmitValidationErrorDto { PlayerId = playerId, Field = schema.Name, Message = "Invalid date" });
                    break;
                case "select":
                    if (schema.Required && !present)
                    {
                        errors.Add(new PreSubmitValidationErrorDto { PlayerId = playerId, Field = schema.Name, Message = "Required" });
                        break;
                    }
                    if (!present) break;
                    if (schema.Options.Count > 0 && !schema.Options.Contains(rawStr)) errors.Add(new PreSubmitValidationErrorDto { PlayerId = playerId, Field = schema.Name, Message = "Invalid option" });
                    break;
                case "multiselect":
                    if (!present)
                    {
                        if (schema.Required) errors.Add(new PreSubmitValidationErrorDto { PlayerId = playerId, Field = schema.Name, Message = "Required" });
                        break;
                    }
                    if (valEl.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in valEl.EnumerateArray())
                        {
                            var s = item.ToString();
                            if (schema.Options.Count > 0 && !schema.Options.Contains(s))
                            {
                                errors.Add(new PreSubmitValidationErrorDto { PlayerId = playerId, Field = schema.Name, Message = "Invalid option" });
                                break;
                            }
                        }
                    }
                    break;
                case "checkbox":
                    // For required checkboxes (e.g., waiver accepts): only evaluate if present; if missing, skip.
                    if (!present) break;
                    // Accept multiple representations of truthy values: true, "true", 1, "1", "yes", "on", "checked".
                    bool accepted;
                    if (valEl.ValueKind == JsonValueKind.True || valEl.ValueKind == JsonValueKind.False)
                    {
                        accepted = valEl.GetBoolean();
                    }
                    else if (valEl.ValueKind == JsonValueKind.Number)
                    {
                        try { accepted = valEl.GetInt32() != 0; } catch { accepted = false; }
                    }
                    else
                    {
                        var s = rawStr.Trim();
                        accepted = string.Equals(s, "true", StringComparison.OrdinalIgnoreCase) ||
                                   string.Equals(s, "1", StringComparison.OrdinalIgnoreCase) ||
                                   string.Equals(s, "yes", StringComparison.OrdinalIgnoreCase) ||
                                   string.Equals(s, "y", StringComparison.OrdinalIgnoreCase) ||
                                   string.Equals(s, "on", StringComparison.OrdinalIgnoreCase) ||
                                   string.Equals(s, "checked", StringComparison.OrdinalIgnoreCase);
                    }
                    if (schema.Required && !accepted)
                    {
                        errors.Add(new PreSubmitValidationErrorDto { PlayerId = playerId, Field = schema.Name, Message = "Required" });
                    }
                    // legacy bool parse check intentionally not used to avoid double-adding errors
                    break;
                default:
                    if (schema.Required && !present)
                    {
                        errors.Add(new PreSubmitValidationErrorDto { PlayerId = playerId, Field = schema.Name, Message = "Required" });
                    }
                    break;
            }
        }
    }
}
