using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using System.Globalization;
using System.Text.Json;
using TSIC.Domain.Constants;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Services;
using TSIC.Application.Services.Players;
using TSIC.Infrastructure.Data.SqlDbContext;
using TSIC.Domain.Entities;
using TSIC.API.Services.External;
using TSIC.API.Services.Teams;

namespace TSIC.API.Services.Players;

public class PlayerRegistrationService : IPlayerRegistrationService
{
    private readonly ILogger<PlayerRegistrationService> _logger;
    private readonly SqlDbContext _db;
    private readonly IPlayerBaseTeamFeeResolverService _feeResolver;
    private readonly IPlayerFeeCalculator _feeCalculator;
    private readonly IVerticalInsureService _verticalInsure;
    private readonly ITeamLookupService _teamLookupService;
    private readonly IPlayerFormValidationService _validationService;

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
        SqlDbContext db,
        IPlayerBaseTeamFeeResolverService feeResolver,
        IPlayerFeeCalculator feeCalculator,
        IVerticalInsureService verticalInsure,
        ITeamLookupService teamLookupService,
        IPlayerFormValidationService validationService)
    {
        _logger = logger;
        _db = db;
        _feeResolver = feeResolver;
        _feeCalculator = feeCalculator;
        _verticalInsure = verticalInsure;
        _teamLookupService = teamLookupService;
        _validationService = validationService;
    }

    public async Task<PreSubmitPlayerRegistrationResponseDto> PreSubmitAsync(Guid jobId, string familyUserId, PreSubmitPlayerRegistrationRequestDto request, string callerUserId)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));

        var ctx = await BuildPreSubmitContextAsync(jobId, familyUserId, request);

        // Build prospective changes first inside a transaction; if validation fails, roll back so nothing is saved.
        using var tx = await _db.Database.BeginTransactionAsync();

        var selectionsByPlayer = request.TeamSelections
            .GroupBy(s => s.PlayerId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var teamResults = new List<PreSubmitTeamResultDto>();
        foreach (var (playerId, selections) in selectionsByPlayer)
        {
            await ProcessPlayerSelectionsAsync(ctx, playerId, selections, teamResults);
        }

        // Server-side metadata validation BEFORE saving. If it fails, do not persist any changes.
        var response = new PreSubmitPlayerRegistrationResponseDto
        {
            TeamResults = teamResults,
            NextTab = teamResults.Exists(r => r.IsFull) ? "Team" : "Payment"
        };

        try
        {
            var validationErrors = _validationService.ValidatePlayerFormValues(ctx.MetadataJson, request.TeamSelections);
            if (validationErrors.Count > 0)
            {
                response.ValidationErrors = validationErrors;
                if (!response.HasFullTeams)
                {
                    response.NextTab = "Forms";
                }
                await tx.RollbackAsync(); // ensure nothing persists when validation fails
                // Build insurance offer even on validation errors (non-persistent) for a consistent response shape
                response.Insurance = await _verticalInsure.BuildOfferAsync(ctx.JobId, ctx.FamilyUserId);
                return response;
            }
        }
        catch (Exception vex)
        {
            // If validation throws, treat as non-fatal and proceed without blocking save (maintain prior behavior)
            _logger.LogWarning(vex, "[PreSubmit] Validation threw unexpectedly; proceeding.");
        }

        await _db.SaveChangesAsync();
        await tx.CommitAsync();
        // Delegate insurance offer construction to VerticalInsure service.
        response.Insurance = await _verticalInsure.BuildOfferAsync(ctx.JobId, ctx.FamilyUserId);
        return response;
    }

    private async Task<PreSubmitContext> BuildPreSubmitContextAsync(Guid jobId, string familyUserId, PreSubmitPlayerRegistrationRequestDto request)
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
        // Prefer centralized TeamLookupService resolver for consistency with team listings.
        var (fee, _) = await _teamLookupService.ResolvePerRegistrantAsync(teamId);
        if (fee > 0m) return fee;

        // Fallback to legacy resolver if centralized logic yields zero, for backward compatibility.
        var cached = await _db.Teams.Where(x => x.TeamId == teamId)
            .Select(x => new { x.FeeBase, x.PerRegistrantFee })
            .FirstOrDefaultAsync();
        if (cached != null)
        {
            var v = cached.FeeBase ?? cached.PerRegistrantFee ?? 0m;
            if (v > 0m) return v;
        }
        return await _feeResolver.ResolveBaseFeeForTeamAsync(teamId);
    }

    private async Task ApplyInitialFeesAsync(Registrations reg, Guid teamId, decimal? teamFeeBase, decimal? teamPerRegistrantFee)
    {
        var paid = reg.PaidTotal;
        if (paid > 0m) return;

        // Centralized fee resolution: prefer provided team values, else resolve via TeamLookupService
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
                    if (TryExtractFieldMapping(f, out var name, out var dbCol))
                    {
                        map[name!] = dbCol!;
                    }
                }
            }
        }
        catch (Exception)
        {
            // Ignore malformed metadata and return empty map
        }
        return map;
    }

    private static bool TryExtractFieldMapping(JsonElement f, out string? name, out string? dbCol)
    {
        name = f.TryGetProperty("name", out var nEl) ? nEl.GetString() : null;
        dbCol = f.TryGetProperty("dbColumn", out var dEl) ? dEl.GetString() : null;

        if (string.IsNullOrWhiteSpace(name))
            return false;

        // Exclude fields marked hidden or adminOnly via visibility
        if (IsFieldExcludedByVisibility(f))
            return false;

        // Do not include admin-only fields in the writable map
        if (IsAdminOnlyField(f))
            return false;

        dbCol = !string.IsNullOrWhiteSpace(dbCol) ? dbCol : name;
        return true;
    }

    private static bool IsFieldExcludedByVisibility(JsonElement f)
    {
        if (f.TryGetProperty("visibility", out var visEl) && visEl.ValueKind == JsonValueKind.String)
        {
            var vis = visEl.GetString();
            if (string.Equals(vis, "hidden", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(vis, "adminOnly", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private static bool IsAdminOnlyField(JsonElement f)
    {
        if (TryGetPropertyCI(f, "adminOnly", out var adminEl))
        {
            var adminFlag = adminEl.ValueKind == JsonValueKind.True ||
                             (adminEl.ValueKind == JsonValueKind.String && bool.TryParse(adminEl.GetString(), out var b) && b);
            return adminFlag;
        }
        return false;
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
                return TryConvertToString(json, out boxed);
            if (t == typeof(int))
                return TryConvertToInt(json, out boxed);
            if (t == typeof(long))
                return TryConvertToLong(json, out boxed);
            if (t == typeof(decimal))
                return TryConvertToDecimal(json, out boxed);
            if (t == typeof(double))
                return TryConvertToDouble(json, out boxed);
            if (t == typeof(bool))
                return TryConvertToBool(json, out boxed);
            if (t == typeof(DateTime))
                return TryConvertToDateTime(json, out boxed);
            if (t == typeof(Guid))
                return TryConvertToGuid(json, out boxed);
        }
        catch { return false; }
        return false;
    }

    private static bool TryConvertToString(JsonElement json, out object? boxed)
    {
        boxed = json.ValueKind == JsonValueKind.Null ? null : json.ToString();
        return true;
    }

    private static bool TryConvertToInt(JsonElement json, out object? boxed)
    {
        boxed = null;
        if (json.ValueKind == JsonValueKind.String && int.TryParse(json.GetString(), out var iv)) { boxed = iv; return true; }
        if (json.TryGetInt32(out var i)) { boxed = i; return true; }
        return false;
    }

    private static bool TryConvertToLong(JsonElement json, out object? boxed)
    {
        boxed = null;
        if (json.ValueKind == JsonValueKind.String && long.TryParse(json.GetString(), out var lv)) { boxed = lv; return true; }
        if (json.TryGetInt64(out var l)) { boxed = l; return true; }
        return false;
    }

    private static bool TryConvertToDecimal(JsonElement json, out object? boxed)
    {
        boxed = null;
        if (json.ValueKind == JsonValueKind.String && decimal.TryParse(json.GetString(), out var dv)) { boxed = dv; return true; }
        if (json.TryGetDecimal(out var d)) { boxed = d; return true; }
        return false;
    }

    private static bool TryConvertToDouble(JsonElement json, out object? boxed)
    {
        boxed = null;
        if (json.ValueKind == JsonValueKind.String && double.TryParse(json.GetString(), out var xv)) { boxed = xv; return true; }
        if (json.TryGetDouble(out var x)) { boxed = x; return true; }
        return false;
    }

    private static bool TryConvertToBool(JsonElement json, out object? boxed)
    {
        boxed = null;
        if (json.ValueKind == JsonValueKind.String && bool.TryParse(json.GetString(), out var bv)) { boxed = bv; return true; }
        if (json.ValueKind == JsonValueKind.Number) { boxed = json.GetInt32() != 0; return true; }
        if (json.ValueKind == JsonValueKind.True || json.ValueKind == JsonValueKind.False) { boxed = json.GetBoolean(); return true; }
        return false;
    }

    private static bool TryConvertToDateTime(JsonElement json, out object? boxed)
    {
        boxed = null;
        if (json.ValueKind == JsonValueKind.String && DateTime.TryParse(json.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
        {
            boxed = dt;
            return true;
        }
        return false;
    }

    private static bool TryConvertToGuid(JsonElement json, out object? boxed)
    {
        boxed = null;
        if (json.ValueKind == JsonValueKind.String && Guid.TryParse(json.GetString(), out var g))
        {
            boxed = g;
            return true;
        }
        return false;
    }
}


