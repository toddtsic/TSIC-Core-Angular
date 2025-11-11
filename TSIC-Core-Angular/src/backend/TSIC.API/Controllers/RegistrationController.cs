using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using TSIC.API.DTOs;
using TSIC.API.Services;
using TSIC.Infrastructure.Data.SqlDbContext;
using TSIC.Domain.Entities;
using TSIC.API.Constants;

namespace TSIC.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class RegistrationController : ControllerBase
{
    private readonly ILogger<RegistrationController> _logger;
    private readonly IJobLookupService _jobLookupService;
    private readonly SqlDbContext _db;
    private readonly IPaymentService _paymentService;
    private static readonly string IsoDate = "yyyy-MM-dd";

    public RegistrationController(
        ILogger<RegistrationController> logger,
        IJobLookupService jobLookupService,
        SqlDbContext db,
        IPaymentService paymentService)
    {
        _logger = logger;
        _jobLookupService = jobLookupService;
        _db = db;
        _paymentService = paymentService;
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

        var teamResults = new List<TSIC.API.Dtos.PreSubmitTeamResultDto>();
        foreach (var sel in request.TeamSelections)
        {
            var team = teams.Find(t => t.TeamId == sel.TeamId);
            if (team == null)
            {
                teamResults.Add(new TSIC.API.Dtos.PreSubmitTeamResultDto
                {
                    PlayerId = sel.PlayerId,
                    TeamId = sel.TeamId,
                    IsFull = true,
                    TeamName = "Unknown",
                    Message = "Team not found.",
                    RegistrationCreated = false
                });
                continue;
            }
            var rosterCount = teamRosterCounts.TryGetValue(team.TeamId, out var cnt) ? cnt : 0;
            var isFull = team.MaxCount > 0 && rosterCount >= team.MaxCount;
            if (isFull)
            {
                teamResults.Add(new TSIC.API.Dtos.PreSubmitTeamResultDto
                {
                    PlayerId = sel.PlayerId,
                    TeamId = team.TeamId,
                    IsFull = true,
                    TeamName = team.TeamName ?? "",
                    Message = "Team roster is full.",
                    RegistrationCreated = false
                });
            }
            else
            {
                // Create pending registration (BActive=false)
                var reg = new Registrations
                {
                    RegistrationId = Guid.NewGuid(),
                    JobId = jobId.Value,
                    FamilyUserId = request.FamilyUserId,
                    UserId = sel.PlayerId,
                    AssignedTeamId = team.TeamId,
                    BActive = false,
                    Modified = DateTime.UtcNow,
                    RegistrationTs = DateTime.UtcNow,
                    RoleId = RoleConstants.Player
                };
                _db.Registrations.Add(reg);
                await _db.SaveChangesAsync();
                teamResults.Add(new TSIC.API.Dtos.PreSubmitTeamResultDto
                {
                    PlayerId = sel.PlayerId,
                    TeamId = team.TeamId,
                    IsFull = false,
                    TeamName = team.TeamName ?? "",
                    Message = "Registration created, pending payment.",
                    RegistrationCreated = true
                });
            }
        }
        var response = new TSIC.API.Dtos.PreSubmitRegistrationResponseDto
        {
            TeamResults = teamResults,
            NextTab = teamResults.Exists(r => r.IsFull) ? "Team" : "Forms"
        };
        return Ok(response);
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
