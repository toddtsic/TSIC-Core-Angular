using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using TSIC.API.DTOs;
using TSIC.API.Services;
using TSIC.Infrastructure.Data.SqlDbContext;
using TSIC.Domain.Entities;
using TSIC.API.Constants;
using System.Text.Json;
using System.Globalization;

namespace TSIC.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class RegistrationController : ControllerBase
{
    private readonly ILogger<RegistrationController> _logger;
    private readonly IJobLookupService _jobLookupService;
    private readonly SqlDbContext _db;
    private readonly IPaymentService _paymentService;
    private readonly IFeeResolverService _feeResolver;
    private readonly IFeeCalculatorService _feeCalculator;
    private static readonly string IsoDate = "yyyy-MM-dd";

    public RegistrationController(
        ILogger<RegistrationController> logger,
        IJobLookupService jobLookupService,
        SqlDbContext db,
        IPaymentService paymentService,
        IFeeResolverService feeResolver,
        IFeeCalculatorService feeCalculator)
    {
        _logger = logger;
        _jobLookupService = jobLookupService;
        _db = db;
        _paymentService = paymentService;
        _feeResolver = feeResolver;
        _feeCalculator = feeCalculator;
    }

    /// <summary>
    /// Checks team roster capacity and creates pending registrations (BActive=false) for available teams before payment.
    /// Returns per-team results and next tab to show.
    /// </summary>
    [HttpPost("preSubmit")]
    [Authorize]
    [ProducesResponseType(typeof(TSIC.API.Dtos.PreSubmitRegistrationResponseDto), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    public async Task<IActionResult> PreSubmitRegistration([FromBody] TSIC.API.Dtos.PreSubmitRegistrationRequestDto request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.JobPath) || string.IsNullOrWhiteSpace(request.FamilyUserId))
            return BadRequest(new { message = "Invalid preSubmit request" });

        var callerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (callerId == null) return Unauthorized();
        if (!string.Equals(callerId, request.FamilyUserId, StringComparison.OrdinalIgnoreCase))
            return Forbid();

        var jobId = await _jobLookupService.GetJobIdByPathAsync(request.JobPath);
        if (jobId is null)
            return NotFound(new { message = $"Job not found: {request.JobPath}" });

        var teamIds = request.TeamSelections.Select(ts => ts.TeamId).Distinct().ToList();
        var teams = await _db.Teams.Where(t => t.JobId == jobId.Value && teamIds.Contains(t.TeamId)).ToListAsync();
        var teamRosterCounts = await _db.Registrations
            .Where(r => r.JobId == jobId.Value && r.AssignedTeamId.HasValue && teamIds.Contains(r.AssignedTeamId.Value) && r.BActive == true)
            .GroupBy(r => r.AssignedTeamId!.Value)
            .ToDictionaryAsync(g => g.Key, g => g.Count());

        // Load profile metadata to map field names -> Registrations property names (dbColumn)
        var jobEntity = await _db.Jobs
            .AsNoTracking()
            .Where(j => j.JobId == jobId.Value)
            .Select(j => new { j.PlayerProfileMetadataJson, j.JsonOptions })
            .SingleOrDefaultAsync();

        var metadataJson = jobEntity?.PlayerProfileMetadataJson;
        var registrationMode = GetRegistrationMode(jobEntity?.JsonOptions); // "PP" or "CAC"

        var nameToProperty = BuildFieldNameToPropertyMap(metadataJson);
        var writableProps = BuildWritablePropertyMap();

        // Load existing registrations for involved players to allow update-in-place when editing
        var playerIds = request.TeamSelections.Select(ts => ts.PlayerId).Distinct().ToList();
        var existingRegs = await _db.Registrations
            .Where(r => r.JobId == jobId.Value && r.FamilyUserId == request.FamilyUserId && r.UserId != null && playerIds.Contains(r.UserId))
            .OrderByDescending(r => r.Modified)
            .ToListAsync();

        var existingByPlayer = existingRegs.GroupBy(r => r.UserId!).ToDictionary(g => g.Key, g => g.ToList());
        var existingByPlayerTeam = existingRegs
            .Where(r => r.AssignedTeamId.HasValue)
            .GroupBy(r => (r.UserId!, r.AssignedTeamId!.Value))
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.Modified).First());

        // Group requested selections by player to detect single-team edit vs multi-team intent
        var selectionsByPlayer = request.TeamSelections
            .GroupBy(s => s.PlayerId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var teamResults = new List<TSIC.API.Dtos.PreSubmitTeamResultDto>();

        // Local wrapper to use central fee resolver; preloaded list gives a fast path
        async Task<decimal> ResolveTeamBaseFeeAsync(Guid teamId)
        {
            var cached = teams.FirstOrDefault(x => x.TeamId == teamId);
            if (cached != null && (cached.FeeBase.HasValue || cached.PerRegistrantFee.HasValue))
            {
                var v = cached.FeeBase ?? cached.PerRegistrantFee ?? 0m;
                if (v > 0) return v;
            }
            return await _feeResolver.ResolveBaseFeeForTeamAsync(teamId);
        }

        // Ensure initial fee fields are populated for new/unpaid registrations
        async Task ApplyInitialFeesAsync(Registrations reg, Guid teamId, decimal? teamFeeBase, decimal? teamPerRegistrantFee)
        {
            // If any payment already recorded, don't alter core fee fields
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
                // Compute processing + total centrally (discount & donation default 0 at this stage)
                var (processing, total) = _feeCalculator.ComputeTotals(reg.FeeBase, reg.FeeDiscount, reg.FeeDonation,
                    (reg.FeeProcessing > 0m) ? reg.FeeProcessing : null);
                if (reg.FeeProcessing <= 0m) reg.FeeProcessing = processing;
                reg.FeeTotal = total;
                // Owed = total - paid
                reg.OwedTotal = reg.FeeTotal - reg.PaidTotal;
            }
        }

        foreach (var (playerId, selections) in selectionsByPlayer)
        {
            // De-duplicate teamIds per player just in case
            var desiredTeamIds = selections.Select(s => s.TeamId).Distinct().ToList();
            if (desiredTeamIds.Count == 0) continue;

            // Helper to add a result entry
            void AddResult(Guid teamId, bool isFull, string teamName, string message, bool created)
                => teamResults.Add(new TSIC.API.Dtos.PreSubmitTeamResultDto
                {
                    PlayerId = playerId,
                    TeamId = teamId,
                    IsFull = isFull,
                    TeamName = teamName,
                    Message = message,
                    RegistrationCreated = created
                });

            if (desiredTeamIds.Count == 1)
            {
                var teamId = desiredTeamIds[0];
                var team = teams.Find(t => t.TeamId == teamId);
                if (team == null)
                {
                    AddResult(teamId, true, "Unknown", "Team not found.", false);
                    continue;
                }
                var rosterCount = teamRosterCounts.TryGetValue(team.TeamId, out var cnt) ? cnt : 0;
                var isFull = team.MaxCount > 0 && rosterCount >= team.MaxCount;
                if (isFull)
                {
                    AddResult(team.TeamId, true, team.TeamName ?? string.Empty, "Team roster is full.", false);
                    continue;
                }

                // If a registration already exists for this player in this job, update it in-place to the selected team
                Registrations? regToUpdate = null;
                if (existingByPlayer.TryGetValue(playerId, out var list) && list.Count > 0)
                {
                    // Prefer exact team match; else latest registration
                    if (existingByPlayerTeam.TryGetValue((playerId, team.TeamId), out var exact))
                        regToUpdate = exact;
                    else
                        regToUpdate = list.OrderByDescending(r => r.Modified).First();
                }

                if (regToUpdate != null)
                {
                    regToUpdate.Modified = DateTime.UtcNow;
                    var sel = selections[selections.Count - 1]; // use last selection for form values
                    if (string.Equals(registrationMode, "PP", StringComparison.OrdinalIgnoreCase))
                    {
                        // PP: Allow team change ONLY if unpaid OR (paid and destination team has identical base fee)
                        var hasPayment = (regToUpdate.PaidTotal > 0) || (regToUpdate.OwedTotal > 0 && regToUpdate.PaidTotal > 0);
                        if (hasPayment)
                        {
                            var existingBase = regToUpdate.FeeBase;
                            if (existingBase <= 0 && regToUpdate.AssignedTeamId.HasValue)
                            {
                                existingBase = await ResolveTeamBaseFeeAsync(regToUpdate.AssignedTeamId.Value);
                            }
                            var newTeamBase = team.FeeBase ?? team.PerRegistrantFee ?? await ResolveTeamBaseFeeAsync(team.TeamId);
                            var sameBase = existingBase > 0 && newTeamBase > 0 && existingBase == newTeamBase;
                            if (sameBase)
                            {
                                // Paid, but base fee matches new team: allow switch
                                regToUpdate.AssignedTeamId = team.TeamId;
                                regToUpdate.Assignment = $"Player: {team.TeamName}";
                                ApplyFormValues(regToUpdate, sel, nameToProperty, writableProps);
                                await ApplyInitialFeesAsync(regToUpdate, team.TeamId, team.FeeBase, team.PerRegistrantFee);
                                AddResult(team.TeamId, false, team.TeamName ?? string.Empty, "Registration updated (team changed - same cost).", false);
                            }
                            else
                            {
                                // Paid and different (or unknown) base cost: block switch
                                ApplyFormValues(regToUpdate, sel, nameToProperty, writableProps);
                                if (regToUpdate.AssignedTeamId.HasValue)
                                {
                                    var assigned = teams.Find(x => x.TeamId == regToUpdate.AssignedTeamId.Value);
                                    if (assigned != null)
                                        regToUpdate.Assignment = $"Player: {assigned.TeamName}";
                                }
                                // No fee change when already paid; still ensure owed/total consistent if missing
                                await ApplyInitialFeesAsync(regToUpdate, regToUpdate.AssignedTeamId ?? team.TeamId, team.FeeBase, team.PerRegistrantFee);
                                AddResult(regToUpdate.AssignedTeamId ?? team.TeamId, false, team.TeamName ?? string.Empty, sameBase ? "Registration updated (team changed - same cost)." : "Registration updated (team change blocked after payment).", false);
                            }
                        }
                        else
                        {
                            // Unpaid registrant can switch teams freely
                            regToUpdate.AssignedTeamId = team.TeamId;
                            regToUpdate.Assignment = $"Player: {team.TeamName}";
                            ApplyFormValues(regToUpdate, sel, nameToProperty, writableProps);
                            await ApplyInitialFeesAsync(regToUpdate, team.TeamId, team.FeeBase, team.PerRegistrantFee);
                            AddResult(team.TeamId, false, team.TeamName ?? string.Empty, "Registration updated (team changed).", false);
                        }
                    }
                    else
                    {
                        // CAC: multi-team friendly
                        // If exact team match, just update form values.
                        if (regToUpdate.AssignedTeamId == team.TeamId)
                        {
                            ApplyFormValues(regToUpdate, sel, nameToProperty, writableProps);
                            regToUpdate.Assignment = $"Player: {team.TeamName}";
                            await ApplyInitialFeesAsync(regToUpdate, team.TeamId, team.FeeBase, team.PerRegistrantFee);
                            AddResult(team.TeamId, false, team.TeamName ?? string.Empty, "Registration updated.", false);
                        }
                        else
                        {
                            // Not an exact match. If the existing registration is paid, don't retarget it; create a new registration for the selected team.
                            var hasPayment = (regToUpdate.PaidTotal > 0) || (regToUpdate.OwedTotal > 0 && regToUpdate.PaidTotal > 0);
                            if (hasPayment)
                            {
                                var newReg = new Registrations
                                {
                                    RegistrationId = Guid.NewGuid(),
                                    JobId = jobId.Value,
                                    FamilyUserId = request.FamilyUserId,
                                    UserId = playerId,
                                    AssignedTeamId = team.TeamId,
                                    BActive = false,
                                    Modified = DateTime.UtcNow,
                                    RegistrationTs = DateTime.UtcNow,
                                    RoleId = RoleConstants.Player,
                                    Assignment = $"Player: {team.TeamName}"
                                };
                                ApplyFormValues(newReg, sel, nameToProperty, writableProps);
                                await ApplyInitialFeesAsync(newReg, team.TeamId, team.FeeBase, team.PerRegistrantFee);
                                _db.Registrations.Add(newReg);
                                AddResult(team.TeamId, false, team.TeamName ?? string.Empty, "New registration created (existing paid kept).", true);
                            }
                            else
                            {
                                // Unpaid: safe to retarget to new team
                                regToUpdate.AssignedTeamId = team.TeamId;
                                regToUpdate.Assignment = $"Player: {team.TeamName}";
                                ApplyFormValues(regToUpdate, sel, nameToProperty, writableProps);
                                await ApplyInitialFeesAsync(regToUpdate, team.TeamId, team.FeeBase, team.PerRegistrantFee);
                                AddResult(team.TeamId, false, team.TeamName ?? string.Empty, "Registration updated (team changed).", false);
                            }
                        }
                    }
                }
                else
                {
                    // Create new pending registration
                    if (string.Equals(registrationMode, "PP", StringComparison.OrdinalIgnoreCase))
                    {
                        // PP: single registration per player; if none exists, allow create; else would have hit prior branch
                    }
                    var reg = new Registrations
                    {
                        RegistrationId = Guid.NewGuid(),
                        JobId = jobId.Value,
                        FamilyUserId = request.FamilyUserId,
                        UserId = playerId,
                        AssignedTeamId = team.TeamId,
                        BActive = false,
                        Modified = DateTime.UtcNow,
                        RegistrationTs = DateTime.UtcNow,
                        RoleId = RoleConstants.Player,
                        Assignment = $"Player: {team.TeamName}"
                    };
                    var sel = selections[selections.Count - 1];
                    ApplyFormValues(reg, sel, nameToProperty, writableProps);
                    await ApplyInitialFeesAsync(reg, team.TeamId, team.FeeBase, team.PerRegistrantFee);
                    _db.Registrations.Add(reg);
                    AddResult(team.TeamId, false, team.TeamName ?? string.Empty, "Registration created, pending payment.", true);
                }
            }
            else
            {
                // Multiple teams requested for this player (multi-camp). Create or update per team.
                if (string.Equals(registrationMode, "PP", StringComparison.OrdinalIgnoreCase))
                {
                    // PP: Multiple teams not allowed; report and skip
                    foreach (var teamId in desiredTeamIds)
                    {
                        var team = teams.Find(t => t.TeamId == teamId);
                        var name = team?.TeamName ?? string.Empty;
                        AddResult(teamId, false, name, "Multiple teams not allowed for this job.", false);
                    }
                    continue;
                }
                foreach (var teamId in desiredTeamIds)
                {
                    var team = teams.Find(t => t.TeamId == teamId);
                    if (team == null)
                    {
                        AddResult(teamId, true, "Unknown", "Team not found.", false);
                        continue;
                    }
                    var rosterCount = teamRosterCounts.TryGetValue(team.TeamId, out var cnt) ? cnt : 0;
                    var isFull = team.MaxCount > 0 && rosterCount >= team.MaxCount;
                    if (isFull)
                    {
                        AddResult(team.TeamId, true, team.TeamName ?? string.Empty, "Team roster is full.", false);
                        continue;
                    }
                    // Update existing exact match or create new
                    if (existingByPlayerTeam.TryGetValue((playerId, team.TeamId), out var existing))
                    {
                        existing.Modified = DateTime.UtcNow;
                        var sel = selections.Last(s => s.TeamId == team.TeamId);
                        ApplyFormValues(existing, sel, nameToProperty, writableProps);
                        await ApplyInitialFeesAsync(existing, team.TeamId, team.FeeBase, team.PerRegistrantFee);
                        AddResult(team.TeamId, false, team.TeamName ?? string.Empty, "Registration updated.", false);
                    }
                    else
                    {
                        var reg = new Registrations
                        {
                            RegistrationId = Guid.NewGuid(),
                            JobId = jobId.Value,
                            FamilyUserId = request.FamilyUserId,
                            UserId = playerId,
                            AssignedTeamId = team.TeamId,
                            BActive = false,
                            Modified = DateTime.UtcNow,
                            RegistrationTs = DateTime.UtcNow,
                            RoleId = RoleConstants.Player,
                            Assignment = $"Player: {team.TeamName}"
                        };
                        var sel = selections.Last(s => s.TeamId == team.TeamId);
                        ApplyFormValues(reg, sel, nameToProperty, writableProps);
                        await ApplyInitialFeesAsync(reg, team.TeamId, team.FeeBase, team.PerRegistrantFee);
                        _db.Registrations.Add(reg);
                        AddResult(team.TeamId, false, team.TeamName ?? string.Empty, "Registration created, pending payment.", true);
                    }
                }
            }
        }

        // Persist once
        await _db.SaveChangesAsync();
        var response = new TSIC.API.Dtos.PreSubmitRegistrationResponseDto
        {
            TeamResults = teamResults,
            NextTab = teamResults.Exists(r => r.IsFull) ? "Team" : "Payment" // may be downgraded to Forms if validation errors
        };

        // --- Lightweight server-side form validation (metadata-driven) ---
        try
        {
            var validationErrors = ValidatePlayerFormValues(metadataJson, request.TeamSelections);
            if (validationErrors.Count > 0)
            {
                response.ValidationErrors = validationErrors;
                // Force user back to Forms to fix issues unless team fullness already blocks
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
        // Attempt to build insurance snapshot if job offers player RegSaver
        try
        {
            // Load job offer flag & name (avoid earlier jobEntity variable scope issues)
            var jobOffer = await _db.Jobs
                .Where(j => j.JobId == jobId.Value)
                .Select(j => new { j.JobName, j.BOfferPlayerRegsaverInsurance })
                .SingleOrDefaultAsync();
            if (jobOffer != null && (jobOffer.BOfferPlayerRegsaverInsurance ?? false))
            {
                // Build eligible registrations (pending or existing) with fees > 0 and no policy; ensure team not near expiry.
                var nowPlus1Day = DateTime.Now.AddHours(24);
                var regs = await _db.Registrations
                    .AsNoTracking()
                    .Where(r => r.JobId == jobId.Value && r.FamilyUserId == request.FamilyUserId && r.FeeTotal > 0 && r.RegsaverPolicyId == null && r.AssignedTeam != null && r.AssignedTeam.Expireondate > nowPlus1Day)
                    .Select(r => new
                    {
                        r.RegistrationId,
                        r.Assignment,
                        FirstName = r.User != null ? r.User.FirstName : null,
                        LastName = r.User != null ? r.User.LastName : null,
                        PerRegistrantFee = r.AssignedTeam != null ? r.AssignedTeam.PerRegistrantFee : null,
                        TeamFee = (r.AssignedTeam != null && r.AssignedTeam.Agegroup != null) ? r.AssignedTeam.Agegroup.TeamFee : null,
                        r.FeeTotal
                    })
                    .ToListAsync();

                // Family contact info for customer block
                var family = await _db.Families.AsNoTracking()
                    .Where(f => f.FamilyUserId == request.FamilyUserId)
                    .Select(f => new
                    {
                        FirstName = f.MomFirstName,
                        LastName = f.MomLastName,
                        Email = f.MomEmail,
                        Phone = f.MomCellphone,
                        City = f.FamilyUser.City,
                        State = f.FamilyUser.State,
                        Zip = f.FamilyUser.PostalCode
                    })
                    .SingleOrDefaultAsync();

                // Organization contact from first Director registration (legacy parity)
                var director = await _db.Registrations
                    .AsNoTracking()
                    .Where(r => r.JobId == jobId.Value && r.Role != null && r.Role.Name == "Director" && r.BActive == true)
                    .OrderBy(r => r.RegistrationTs)
                    .Select(r => new
                    {
                        Email = r.User != null ? r.User.Email : null,
                        FirstName = r.User != null ? r.User.FirstName : null,
                        LastName = r.User != null ? r.User.LastName : null,
                        Cellphone = r.User != null ? r.User.Cellphone : null,
                        OrgName = r.Job != null ? r.Job.JobName : null,
                        PaymentPlan = r.Job != null && (r.Job.AdnArb == true)
                    })
                    .FirstOrDefaultAsync();

                if (regs.Count == 0)
                {
                    response.Insurance = new TSIC.API.Dtos.PreSubmitInsuranceDto
                    {
                        Available = false,
                        Error = "No eligible registrations for insurance (fee or expiry criteria unmet)."
                    };
                }
                else
                {
                    // Build VerticalInsure-like player object inline (mirrors VerticalInsureController output) but keep it opaque (object).
                    var products = new List<object>();
                    var contextName = (jobOffer.JobName ?? string.Empty).Split(':')[0];
                    foreach (var r in regs)
                    {
                        int insurable;
                        if ((int?)(r.PerRegistrantFee ?? 0) > 0) insurable = (int)((r.PerRegistrantFee ?? 0) * 100);
                        else if ((int?)(r.TeamFee ?? 0) > 0) insurable = (int)((r.TeamFee ?? 0) * 100);
                        else insurable = (int)(r.FeeTotal * 100);
                        var product = new
                        {
                            customer = new
                            {
                                email_address = family?.Email ?? string.Empty,
                                first_name = family?.FirstName ?? string.Empty,
                                last_name = family?.LastName ?? string.Empty,
                                city = family?.City,
                                state = family?.State,
                                postal_code = family?.Zip,
                                phone = family?.Phone
                            },
                            metadata = new
                            {
                                tsic_secondchance = "0",
                                context_name = contextName,
                                context_event = jobOffer.JobName ?? contextName,
                                context_description = r.Assignment,
                                tsic_registrationid = r.RegistrationId
                            },
                            policy_attributes = new
                            {
                                event_start_date = DateOnly.FromDateTime(DateTime.Now).AddDays(1),
                                event_end_date = DateOnly.FromDateTime(DateTime.Now).AddYears(1),
                                insurable_amount = insurable,
                                participant = new { first_name = r.FirstName ?? string.Empty, last_name = r.LastName ?? string.Empty },
                                organization = new
                                {
                                    org_contact_email = director?.Email,
                                    org_contact_first_name = director?.FirstName,
                                    org_contact_last_name = director?.LastName,
                                    org_contact_phone = director?.Cellphone,
                                    org_name = director?.OrgName,
                                    payment_plan = director?.PaymentPlan ?? false
                                }
                            }
                        };
                        products.Add(product);
                    }
                    // Hard-coded client id (same as VerticalInsureController); secret not exposed.
                    var playerObject = new
                    {
                        client_id = "live_VJ8O8O81AZQ8MCSKWM98928597WUHSMS",
                        payments = new { enabled = false, button = false },
                        theme = new { colors = new { primary = "purple" }, font_family = "Fira Sans" },
                        product_config = new { registration_cancellation = products }
                    };
                    response.Insurance = new TSIC.API.Dtos.PreSubmitInsuranceDto
                    {
                        Available = true,
                        PlayerObject = playerObject,
                        ExpiresUtc = DateTime.UtcNow.AddMinutes(10), // short-lived snapshot
                        StateId = $"vi-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N")[..8]}"
                    };
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[PreSubmit] Failed to build insurance snapshot.");
            response.Insurance = new TSIC.API.Dtos.PreSubmitInsuranceDto
            {
                Available = false,
                Error = "Insurance snapshot generation failed"
            };
        }
        return Ok(response);
    }

    // Parse metadata JSON and validate submitted form values per player.
    private static List<TSIC.API.Dtos.PreSubmitValidationErrorDto> ValidatePlayerFormValues(string? metadataJson, List<TSIC.API.Dtos.PreSubmitTeamSelectionDto> selections)
    {
        var errors = new List<TSIC.API.Dtos.PreSubmitValidationErrorDto>();
        if (string.IsNullOrWhiteSpace(metadataJson) || selections.Count == 0) return errors;
        JsonDocument? doc = null;
        try { doc = JsonDocument.Parse(metadataJson); } catch { return errors; }
        if (doc == null) return errors;
        if (!doc.RootElement.TryGetProperty("fields", out var fieldsEl) || fieldsEl.ValueKind != JsonValueKind.Array) return errors;

        // Build simplified schema list we care about
        var schemas = new List<(string Name, bool Required, string Type, string? ConditionField, JsonElement? ConditionValue, string? ConditionOp, HashSet<string> Options)>();
        foreach (var f in fieldsEl.EnumerateArray())
        {
            var name = f.TryGetProperty("name", out var nEl) ? nEl.GetString() : null;
            if (string.IsNullOrWhiteSpace(name)) continue;
            // visibility: skip hidden only; admin-only SHOULD be validated server-side
            if (f.TryGetProperty("visibility", out var visEl) && visEl.ValueKind == JsonValueKind.String)
            {
                var vis = visEl.GetString()?.ToLowerInvariant();
                if (vis == "hidden") continue; // validate admin-only
            }
            // Determine required
            bool required = false;
            if (f.TryGetProperty("required", out var reqEl) && reqEl.ValueKind == JsonValueKind.True) required = true;
            if (!required && f.TryGetProperty("validation", out var valEl) && valEl.ValueKind == JsonValueKind.Object)
            {
                if (valEl.TryGetProperty("required", out var rEl) && rEl.ValueKind == JsonValueKind.True) required = true;
                if (valEl.TryGetProperty("requiredTrue", out var rtEl) && rtEl.ValueKind == JsonValueKind.True) required = true;
            }
            var type = f.TryGetProperty("type", out var tEl) ? (tEl.GetString() ?? "text") : "text";
            // Condition
            string? condField = null; JsonElement? condValue = null; string? condOp = null;
            if (f.TryGetProperty("condition", out var cEl) && cEl.ValueKind == JsonValueKind.Object)
            {
                condField = cEl.TryGetProperty("field", out var cfEl) ? cfEl.GetString() : null;
                if (cEl.TryGetProperty("value", out var cvEl)) condValue = cvEl;
                condOp = cEl.TryGetProperty("operator", out var coEl) ? coEl.GetString() : null;
            }
            // Options
            var options = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (f.TryGetProperty("options", out var optEl) && optEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var o in optEl.EnumerateArray())
                {
                    if (o.ValueKind == JsonValueKind.String) options.Add(o.GetString()!);
                }
            }
            schemas.Add((name!, required, type!.ToLowerInvariant(), condField, condValue, condOp, options));
        }

        // Group form values per player (a player may appear multiple times with same formValues; use last)
        var latestValuesByPlayer = new Dictionary<string, Dictionary<string, JsonElement>>();
        foreach (var sel in selections)
        {
            if (sel.FormValues == null) continue;
            latestValuesByPlayer[sel.PlayerId] = sel.FormValues;
        }

        foreach (var (playerId, formValues) in latestValuesByPlayer)
        {
            foreach (var schema in schemas)
            {
                // Evaluate condition (only equals supported)
                if (schema.ConditionField != null && schema.ConditionValue.HasValue)
                {
                    formValues.TryGetValue(schema.ConditionField, out var otherVal);
                    var condOk = otherVal.ValueKind == schema.ConditionValue.Value.ValueKind && otherVal.ToString() == schema.ConditionValue.Value.ToString();
                    if (!condOk) continue; // not visible -> skip validation
                }
                // Required check
                formValues.TryGetValue(schema.Name, out var valEl);
                bool present = valEl.ValueKind != JsonValueKind.Undefined && valEl.ValueKind != JsonValueKind.Null && valEl.ToString().Trim().Length > 0;
                if (schema.Required && !present)
                {
                    errors.Add(new TSIC.API.Dtos.PreSubmitValidationErrorDto { PlayerId = playerId, Field = schema.Name, Message = "Required" });
                    continue;
                }
                if (!present) continue; // only validate content if provided
                var rawStr = valEl.ToString();
                switch (schema.Type)
                {
                    case "number":
                        if (!double.TryParse(rawStr, out _)) errors.Add(new TSIC.API.Dtos.PreSubmitValidationErrorDto { PlayerId = playerId, Field = schema.Name, Message = "Must be a number" });
                        break;
                    case "date":
                        if (!DateTime.TryParse(rawStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                            errors.Add(new TSIC.API.Dtos.PreSubmitValidationErrorDto { PlayerId = playerId, Field = schema.Name, Message = "Invalid date" });
                        break;
                    case "select":
                        if (schema.Options.Count > 0 && !schema.Options.Contains(rawStr)) errors.Add(new TSIC.API.Dtos.PreSubmitValidationErrorDto { PlayerId = playerId, Field = schema.Name, Message = "Invalid option" });
                        break;
                    case "multiselect":
                        if (valEl.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var item in valEl.EnumerateArray())
                            {
                                var s = item.ToString();
                                if (schema.Options.Count > 0 && !schema.Options.Contains(s))
                                {
                                    errors.Add(new TSIC.API.Dtos.PreSubmitValidationErrorDto { PlayerId = playerId, Field = schema.Name, Message = "Invalid option" });
                                    break;
                                }
                            }
                        }
                        else if (schema.Required)
                        {
                            errors.Add(new TSIC.API.Dtos.PreSubmitValidationErrorDto { PlayerId = playerId, Field = schema.Name, Message = "Required" });
                        }
                        break;
                    case "checkbox":
                        if (schema.Required)
                        {
                            if (!bool.TryParse(rawStr, out var b) || b == false)
                                errors.Add(new TSIC.API.Dtos.PreSubmitValidationErrorDto { PlayerId = playerId, Field = schema.Name, Message = "Required" });
                        }
                        break;
                }
            }
        }
        return errors;
    }

    // Determine registration mode for job: "PP" (single registration per player) vs "CAC" (multi-team allowed).
    private static string GetRegistrationMode(string? jsonOptions)
    {
        if (string.IsNullOrWhiteSpace(jsonOptions)) return "PP";
        try
        {
            using var doc = JsonDocument.Parse(jsonOptions);
            var root = doc.RootElement;
            // Accept several possible keys for flexibility
            string[] keys = new[] { "registrationMode", "profileMode", "regProfileType", "registrationType" };
            foreach (var k in keys)
            {
                if (root.TryGetProperty(k, out var el))
                {
                    var s = el.GetString();
                    if (!string.IsNullOrWhiteSpace(s))
                    {
                        s = s.Trim();
                        if (s.Equals("CAC", StringComparison.OrdinalIgnoreCase)) return "CAC";
                        if (s.Equals("PP", StringComparison.OrdinalIgnoreCase)) return "PP";
                    }
                }
                // Case-insensitive traversal
                foreach (var prop in root.EnumerateObject())
                {
                    if (prop.NameEquals(k))
                    {
                        var sv = prop.Value.GetString();
                        if (!string.IsNullOrWhiteSpace(sv))
                        {
                            sv = sv.Trim();
                            if (sv.Equals("CAC", StringComparison.OrdinalIgnoreCase)) return "CAC";
                            if (sv.Equals("PP", StringComparison.OrdinalIgnoreCase)) return "PP";
                        }
                    }
                }
            }
        }
        catch { }
        return "PP"; // default safe mode
    }

    private static void ApplyFormValues(Registrations reg, TSIC.API.Dtos.PreSubmitTeamSelectionDto sel,
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

    // Build a case-insensitive map of incoming field name -> Registrations property name, using job metadata when available
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
                    if (!string.IsNullOrWhiteSpace(dbCol))
                    {
                        // Prefer mapping name -> dbColumn when provided
                        map[name!] = dbCol!;
                    }
                    else
                    {
                        // Fallback: use name itself
                        map[name!] = name!;
                    }
                }
            }
        }
        catch { /* ignore malformed metadata */ }
        return map;
    }

    // Build writable Registrations property map (name -> PropertyInfo), filtered similarly to read model exclusions
    private static Dictionary<string, System.Reflection.PropertyInfo> BuildWritablePropertyMap()
    {
        var props = typeof(Registrations).GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        var dict = new Dictionary<string, System.Reflection.PropertyInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in props)
        {
            if (!p.CanWrite) continue;
            // Reuse ShouldIncludeProperty to determine safe, non-system fields; also reject AssignedTeamId as it's set explicitly
            if (!ShouldIncludeProperty(p)) continue;
            if (string.Equals(p.Name, nameof(Registrations.AssignedTeamId), StringComparison.OrdinalIgnoreCase)) continue;
            dict[p.Name] = p;
        }
        return dict;
    }

    // Decide which Registrations property an incoming key should write to
    private static string? ResolveTargetPropertyName(string incoming,
        Dictionary<string, string> nameToProperty,
        Dictionary<string, System.Reflection.PropertyInfo> writable)
    {
        // 1) Metadata map (name -> dbColumn/property)
        if (nameToProperty.TryGetValue(incoming, out var target) && writable.ContainsKey(target))
        {
            return target;
        }
        // 2) Direct property match
        if (writable.ContainsKey(incoming)) return incoming;
        return null;
    }

    // Convert JsonElement to the desired target type and return boxed value
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
            if (t == typeof(DateTime) && json.ValueKind == JsonValueKind.String)
            {
                if (DateTime.TryParse(json.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)) { boxed = dt; return true; }
            }
            if (t == typeof(Guid) && json.ValueKind == JsonValueKind.String)
            {
                if (Guid.TryParse(json.GetString(), out var g)) { boxed = g; return true; }
            }
        }
        catch
        {
            return false;
        }
        return false;
    }

    /// <summary>
    /// Returns an existing registration snapshot for a family user in the context of a job.
    /// Shape is compatible with the wizard prefill expectations: teams per player and form values per player.
    /// NOTE: Currently returns an empty payload as a placeholder until data-mapping is implemented.
    /// </summary>
    /// <param name="jobPath">Canonical job path (e.g., stepsgirls-players-2025-2026)</param>
    /// <param name="familyUserId">Family account user id (guid string)</param>
    [HttpGet("existing")]
    [Authorize]
    [ProducesResponseType(typeof(object), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetExistingRegistration([FromQuery] string jobPath, [FromQuery] string familyUserId)
    {
        if (string.IsNullOrWhiteSpace(jobPath) || string.IsNullOrWhiteSpace(familyUserId))
        {
            return BadRequest(new { message = "jobPath and familyUserId are required" });
        }

        // Ensure caller is the same family user (or has elevated roles) - basic check
        var callerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (callerId == null) return Unauthorized();
        if (!string.Equals(callerId, familyUserId, StringComparison.OrdinalIgnoreCase))
        {
            // Future: allow admin/superusers; for now restrict
            return Forbid();
        }

        // Validate job exists via lookup
        var jobId = await _jobLookupService.GetJobIdByPathAsync(jobPath);
        if (jobId is null)
        {
            return NotFound(new { message = $"Job not found: {jobPath}" });
        }

        // Load all registrations for this family+job
        var regsAll = await _db.Registrations
            .Where(r => r.JobId == jobId.Value && r.FamilyUserId == familyUserId && r.UserId != null)
            .OrderByDescending(r => r.Modified)
            .ToListAsync();

        // Group by player id (UserId) for value mapping; latest (first in ordered list) drives form value prefill.
        var latestByPlayer = regsAll
            .GroupBy(r => r.UserId!)
            .ToDictionary(g => g.Key, g => g.First());

        // Build teams map: playerId -> teamId | teamIds[] when multiple registrations (e.g., multiple camps under one job)
        var teams = new Dictionary<string, object>();
        foreach (var grp in regsAll.GroupBy(r => r.UserId!))
        {
            var playerId = grp.Key;
            var teamIds = grp
                .Where(r => r.AssignedTeamId.HasValue && r.AssignedTeamId.Value != Guid.Empty)
                .Select(r => r.AssignedTeamId!.Value.ToString())
                .Distinct()
                .ToList();
            if (teamIds.Count == 1)
            {
                teams[playerId] = teamIds[0];
            }
            else if (teamIds.Count > 1)
            {
                teams[playerId] = teamIds; // array signals multi-team scenario
            }
        }

        // Build values map: playerId -> { fieldName -> value }
        var values = new Dictionary<string, Dictionary<string, object?>>();
        foreach (var (pid, reg) in latestByPlayer)
        {
            var map = BuildValuesMap(reg);
            // Keep property names as-is (e.g., GradYear) to avoid duplicate keys or guesswork
            values[pid] = map;
        }

        return Ok(new { teams, values });
    }

    /// <summary>
    /// Returns a flat list of registrations (one row per registration) for a given family within a job.
    /// Useful for payment/checkout flows where each team/camp is a distinct registration.
    /// </summary>
    [HttpGet("family-registrations")]
    [Authorize]
    [ProducesResponseType(typeof(IEnumerable<FamilyRegistrationItemDto>), 200)]
    public async Task<IActionResult> GetFamilyRegistrations([FromQuery] string jobPath, [FromQuery] string familyUserId)
    {
        var validationResult = ValidateFamilyRequest(jobPath, familyUserId);
        if (validationResult is IActionResult early) return early;
        var jobId = await _jobLookupService.GetJobIdByPathAsync(jobPath);
        if (jobId is null) return NotFound(new { message = $"Job not found: {jobPath}" });

        var regs = await LoadFamilyRegistrations(jobId.Value, familyUserId);
        if (regs.Count == 0) return Ok(Array.Empty<FamilyRegistrationItemDto>());

        var teamMap = await BuildTeamNameMap(jobId.Value, regs);
        var userMap = await BuildUserNameMap(regs);
        var items = regs.Select(r => ProjectFamilyRegistration(r, userMap, teamMap, jobPath)).ToList();
        return Ok(items);
    }

    private IActionResult? ValidateFamilyRequest(string jobPath, string familyUserId)
    {
        if (string.IsNullOrWhiteSpace(jobPath) || string.IsNullOrWhiteSpace(familyUserId))
            return BadRequest(new { message = "jobPath and familyUserId are required" });
        var callerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (callerId == null) return Unauthorized();
        if (!string.Equals(callerId, familyUserId, StringComparison.OrdinalIgnoreCase)) return Forbid();
        return null;
    }

    private async Task<List<Registrations>> LoadFamilyRegistrations(Guid jobId, string familyUserId) =>
        await _db.Registrations
            .Where(r => r.JobId == jobId && r.FamilyUserId == familyUserId && r.UserId != null)
            .OrderByDescending(r => r.Modified)
            .ToListAsync();

    private async Task<Dictionary<Guid, string>> BuildTeamNameMap(Guid jobId, List<Registrations> regs)
    {
        var teamIds = regs.Where(r => r.AssignedTeamId.HasValue).Select(r => r.AssignedTeamId!.Value).Distinct().ToList();
        if (teamIds.Count == 0) return new();
        return await _db.Teams
            .Where(t => t.JobId == jobId && teamIds.Contains(t.TeamId))
            .ToDictionaryAsync(t => t.TeamId, t => t.TeamName ?? string.Empty);
    }

    private async Task<Dictionary<string, (string? First, string? Last)>> BuildUserNameMap(List<Registrations> regs)
    {
        var playerIds = regs.Select(r => r.UserId!).Distinct().ToList();
        var data = await _db.AspNetUsers
            .Where(u => playerIds.Contains(u.Id))
            .Select(u => new { u.Id, u.FirstName, u.LastName })
            .ToListAsync();
        return data.ToDictionary(x => x.Id, x => (x.FirstName, x.LastName));
    }

    private static FamilyRegistrationItemDto ProjectFamilyRegistration(
        Registrations r,
        Dictionary<string, (string? First, string? Last)> userMap,
        Dictionary<Guid, string> teamMap,
        string jobPath) => new()
        {
            RegistrationId = r.RegistrationId,
            PlayerId = r.UserId!,
            PlayerFirstName = userMap.TryGetValue(r.UserId!, out var u) ? u.First : null,
            PlayerLastName = userMap.TryGetValue(r.UserId!, out var u2) ? u2.Last : null,
            JobId = r.JobId,
            JobPath = jobPath,
            AssignedTeamId = r.AssignedTeamId,
            AssignedTeamName = r.AssignedTeamId.HasValue && teamMap.TryGetValue(r.AssignedTeamId.Value, out var tn) ? tn : null,
            Modified = r.Modified,
            GradYear = r.GradYear,
            SportAssnId = r.SportAssnId,
            FeeBase = r.FeeBase,
            FeeDiscount = r.FeeDiscount,
            FeeDiscountMp = r.FeeDiscountMp,
            FeeDonation = r.FeeDonation,
            FeeLatefee = r.FeeLatefee,
            FeeProcessing = r.FeeProcessing,
            FeeTotal = r.FeeTotal,
            OwedTotal = r.OwedTotal,
            PaidTotal = r.PaidTotal
        };

    private static Dictionary<string, object?> BuildValuesMap(Registrations reg)
    {
        var map = new Dictionary<string, object?>();
        var props = typeof(Registrations).GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        foreach (var p in props)
        {
            if (!ShouldIncludeProperty(p)) continue;
            var val = p.GetValue(reg);
            if (val is null) continue;
            map[p.Name] = NormalizeSimpleValue(val);
        }
        return map;
    }

    private static bool ShouldIncludeProperty(System.Reflection.PropertyInfo p)
    {
        if (!p.CanRead || p.GetIndexParameters().Length > 0) return false;
        var name = p.Name;
        var type = p.PropertyType;

        // Exclude collections/enumerables (navigation props) except string
        if (typeof(System.Collections.IEnumerable).IsAssignableFrom(type) && type != typeof(string))
        {
            return false;
        }

        // Include only simple scalar types
        static bool IsSimple(Type t)
        {
            var u = Nullable.GetUnderlyingType(t) ?? t;
            return u.IsPrimitive
                || u.IsEnum
                || u == typeof(string)
                || u == typeof(decimal)
                || u == typeof(DateTime)
                || u == typeof(Guid);
        }
        if (!IsSimple(type)) return false;
        // Exclude known system/sensitive fields
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
        // Exclude any property ending with Id/ID except SportAssnId
        if (!string.Equals(name, nameof(Registrations.SportAssnId), StringComparison.Ordinal) &&
            (name.EndsWith("Id", StringComparison.Ordinal) || name.EndsWith("ID", StringComparison.Ordinal)))
        {
            return false;
        }
        // Exclude boolean waiver/upload flags so Waivers step can manage them
        if (name.StartsWith("BWaiverSigned", StringComparison.Ordinal) || name.StartsWith("BUploaded", StringComparison.Ordinal))
        {
            return false;
        }
        // Exclude obvious ADN/RegSaver/Payment fields by prefix
        if (name.StartsWith("Adn", StringComparison.Ordinal) || name.StartsWith("Regsaver", StringComparison.Ordinal))
        {
            return false;
        }
        return true;
    }

    private static object NormalizeSimpleValue(object val)
    {
        // Normalize dates and Guids to strings for consistency
        return val switch
        {
            DateTime dt => dt.ToString(IsoDate),
            Guid g => g.ToString(),
            _ => val
        };
    }
    [AllowAnonymous]
    [HttpPost("check-status")]
    public async Task<ActionResult<IEnumerable<RegistrationStatusResponse>>> CheckJobRegistrationStatus([FromBody] RegistrationStatusRequest request)
    {
        _logger.LogInformation("Checking registration status for job: {JobPath}, types: {Types}",
            request.JobPath, string.Join(", ", request.RegistrationTypes));

        // Lookup job by path
        var jobId = await _jobLookupService.GetJobIdByPathAsync(request.JobPath);

        if (jobId == null)
        {
            return NotFound(new { message = $"Job not found: {request.JobPath}" });
        }

        var responses = new List<RegistrationStatusResponse>();

        foreach (var regType in request.RegistrationTypes)
        {
            RegistrationStatusResponse response;

            if (regType.Equals("Player", StringComparison.OrdinalIgnoreCase))
            {
                var isActive = await _jobLookupService.IsPlayerRegistrationActiveAsync(jobId.Value);

                response = new RegistrationStatusResponse
                {
                    RegistrationType = regType,
                    IsAvailable = isActive,
                    Message = isActive ? null : "Player registration is not available at this time.",
                    RegistrationUrl = isActive ? $"/{request.JobPath}/register/player" : null
                };
            }
            else
            {
                // NOTE: Phase 1 follow-up: add support for Team, ClubRep, Volunteer, Recruiter registration types
                response = new RegistrationStatusResponse
                {
                    RegistrationType = regType,
                    IsAvailable = false,
                    Message = $"{regType} registration is not available at this time.",
                    RegistrationUrl = null
                };
            }

            responses.Add(response);
        }

        return Ok(responses);
    }

    [HttpPost("submit-payment")]
    [Authorize]
    [ProducesResponseType(typeof(PaymentResponseDto), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    public async Task<IActionResult> SubmitPayment([FromBody] PaymentRequestDto request)
    {
        if (request == null || request.CreditCard == null)
        {
            return BadRequest(new { message = "Invalid payment request" });
        }

        var callerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (callerId == null) return Unauthorized();

        // Ensure caller is the family user
        if (!string.Equals(callerId, request.FamilyUserId.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            return Forbid();
        }

        var result = await _paymentService.ProcessPaymentAsync(request, callerId);
        if (result.Success)
        {
            return Ok(result);
        }
        else
        {
            return BadRequest(new { message = result.Message });
        }
    }
}
