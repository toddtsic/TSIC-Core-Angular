using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using TSIC.API.Extensions;
using TSIC.API.Services.Shared.Jobs;
using TSIC.Contracts.Dtos.Scheduling;
using TSIC.Contracts.Services;

namespace TSIC.API.Controllers;

/// <summary>
/// Consumer-facing schedule viewer (009-5).
/// Supports both authenticated admin/coach access and public access mode.
/// Public endpoints check Job.BScheduleAllowPublicAccess before serving data.
/// </summary>
[ApiController]
[Route("api/view-schedule")]
public class ViewScheduleController : ControllerBase
{
    private readonly ILogger<ViewScheduleController> _logger;
    private readonly IViewScheduleService _service;
    private readonly IJobLookupService _jobLookupService;

    public ViewScheduleController(
        ILogger<ViewScheduleController> logger,
        IViewScheduleService service,
        IJobLookupService jobLookupService)
    {
        _logger = logger;
        _service = service;
        _jobLookupService = jobLookupService;
    }

    /// <summary>
    /// Resolve jobId from either:
    /// 1. Authenticated user's regId claim (standard path)
    /// 2. jobPath query parameter (public access path)
    /// Returns (jobId, userId, isAdmin, error).
    /// </summary>
    private async Task<(Guid? jobId, string? userId, bool isAdmin, ActionResult? error)> ResolveContext(
        string? jobPath = null)
    {
        // Try authenticated path first
        var regId = User.GetRegistrationId();
        if (regId.HasValue)
        {
            var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
            if (jobId == null)
                return (null, null, false, BadRequest(new { message = "Schedule context required" }));

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            // Check role using both mapped (ClaimTypes.Role) and unmapped ("role") claim types.
            // .NET 10's JsonWebTokenHandler may not remap "role" → ClaimTypes.Role.
            var roleName = User.FindFirstValue(ClaimTypes.Role)
                ?? User.FindFirstValue("role");
            var isAdmin = roleName is "SuperUser" or "Director" or "SuperDirector" or "Scorer";

            return (jobId, userId, isAdmin, null);
        }

        // Public access path — resolve from jobPath
        if (!string.IsNullOrEmpty(jobPath))
        {
            var jobId = await _jobLookupService.GetJobIdByPathAsync(jobPath);
            if (jobId == null)
                return (null, null, false, NotFound(new { message = "Schedule not found" }));

            return (jobId, null, false, null);
        }

        return (null, null, false, Unauthorized(new { message = "Authentication or jobPath required" }));
    }

    // ══════════════════════════════════════════════════════════════
    // Public-accessible endpoints (when BScheduleAllowPublicAccess)
    // ══════════════════════════════════════════════════════════════

    /// <summary>GET /api/view-schedule/filter-options?jobPath= — CADT tree + game days + fields.</summary>
    [AllowAnonymous]
    [HttpGet("filter-options")]
    public async Task<ActionResult<ScheduleFilterOptionsDto>> GetFilterOptions(
        [FromQuery] string? jobPath, CancellationToken ct)
    {
        var (jobId, _, _, error) = await ResolveContext(jobPath);
        if (error != null) return error;

        var result = await _service.GetFilterOptionsAsync(jobId!.Value, ct);
        return Ok(result);
    }

    /// <summary>GET /api/view-schedule/capabilities?jobPath= — Feature flags for this job/user.</summary>
    [AllowAnonymous]
    [HttpGet("capabilities")]
    public async Task<ActionResult<ScheduleCapabilitiesDto>> GetCapabilities(
        [FromQuery] string? jobPath, CancellationToken ct)
    {
        var (jobId, _, isAdmin, error) = await ResolveContext(jobPath);
        if (error != null) return error;

        var isAuthenticated = User.Identity?.IsAuthenticated == true;
        var result = await _service.GetCapabilitiesAsync(jobId!.Value, isAuthenticated, isAdmin, ct);
        return Ok(result);
    }

    /// <summary>POST /api/view-schedule/games?jobPath= — Filtered game list.</summary>
    [AllowAnonymous]
    [HttpPost("games")]
    public async Task<ActionResult<List<ViewGameDto>>> GetGames(
        [FromBody] ScheduleFilterRequest request, [FromQuery] string? jobPath, CancellationToken ct)
    {
        var (jobId, _, _, error) = await ResolveContext(jobPath);
        if (error != null) return error;

        var result = await _service.GetGamesAsync(jobId!.Value, request, ct);
        return Ok(result);
    }

    /// <summary>POST /api/view-schedule/standings?jobPath= — Pool play standings by division.</summary>
    [AllowAnonymous]
    [HttpPost("standings")]
    public async Task<ActionResult<StandingsByDivisionResponse>> GetStandings(
        [FromBody] ScheduleFilterRequest request, [FromQuery] string? jobPath, CancellationToken ct)
    {
        var (jobId, _, _, error) = await ResolveContext(jobPath);
        if (error != null) return error;

        var result = await _service.GetStandingsAsync(jobId!.Value, request, ct);
        return Ok(result);
    }

    /// <summary>POST /api/view-schedule/team-records?jobPath= — Full season records.</summary>
    [AllowAnonymous]
    [HttpPost("team-records")]
    public async Task<ActionResult<StandingsByDivisionResponse>> GetTeamRecords(
        [FromBody] ScheduleFilterRequest request, [FromQuery] string? jobPath, CancellationToken ct)
    {
        var (jobId, _, _, error) = await ResolveContext(jobPath);
        if (error != null) return error;

        var result = await _service.GetTeamRecordsAsync(jobId!.Value, request, ct);
        return Ok(result);
    }

    /// <summary>POST /api/view-schedule/brackets?jobPath= — Bracket matches grouped by division.</summary>
    [AllowAnonymous]
    [HttpPost("brackets")]
    public async Task<ActionResult<List<DivisionBracketResponse>>> GetBrackets(
        [FromBody] ScheduleFilterRequest request, [FromQuery] string? jobPath, CancellationToken ct)
    {
        var (jobId, _, _, error) = await ResolveContext(jobPath);
        if (error != null) return error;

        var result = await _service.GetBracketsAsync(jobId!.Value, request, ct);
        return Ok(result);
    }

    /// <summary>GET /api/view-schedule/team-results/{teamId}?jobPath= — Team game history drill-down.</summary>
    [AllowAnonymous]
    [HttpGet("team-results/{teamId:guid}")]
    public async Task<ActionResult<TeamResultsResponse>> GetTeamResults(
        Guid teamId, [FromQuery] string? jobPath, CancellationToken ct)
    {
        // Validate context (needed for public access check)
        var (_, _, _, error) = await ResolveContext(jobPath);
        if (error != null) return error;

        var result = await _service.GetTeamResultsAsync(teamId, ct);
        return Ok(result);
    }

    /// <summary>GET /api/view-schedule/field-info/{fieldId} — Field directions/details.</summary>
    [AllowAnonymous]
    [HttpGet("field-info/{fieldId:guid}")]
    public async Task<ActionResult<FieldDisplayDto>> GetFieldInfo(Guid fieldId, CancellationToken ct)
    {
        var result = await _service.GetFieldInfoAsync(fieldId, ct);
        if (result == null) return NotFound();
        return Ok(result);
    }

    // ══════════════════════════════════════════════════════════════
    // Admin/Scorer-only endpoints
    // ══════════════════════════════════════════════════════════════

    /// <summary>POST /api/view-schedule/quick-score — Inline score edit.</summary>
    [Authorize(Policy = "AdminOnly")]
    [HttpPost("quick-score")]
    public async Task<ActionResult> QuickEditScore(
        [FromBody] EditScoreRequest request, CancellationToken ct)
    {
        var (jobId, userId, _, error) = await ResolveContext();
        if (error != null) return error;

        await _service.QuickEditScoreAsync(jobId!.Value, userId!, request, ct);
        return Ok();
    }

    /// <summary>POST /api/view-schedule/edit-game — Full game edit (teams, scores, annotations).</summary>
    [Authorize(Policy = "AdminOnly")]
    [HttpPost("edit-game")]
    public async Task<ActionResult> EditGame(
        [FromBody] EditGameRequest request, CancellationToken ct)
    {
        var (jobId, userId, _, error) = await ResolveContext();
        if (error != null) return error;

        await _service.EditGameAsync(jobId!.Value, userId!, request, ct);
        return Ok();
    }

    // ══════════════════════════════════════════════════════════════
    // Authenticated-only endpoints
    // ══════════════════════════════════════════════════════════════

    /// <summary>POST /api/view-schedule/contacts — Staff contacts (respects BHideContacts).</summary>
    [Authorize]
    [HttpPost("contacts")]
    public async Task<ActionResult<List<ContactDto>>> GetContacts(
        [FromBody] ScheduleFilterRequest request, CancellationToken ct)
    {
        var (jobId, _, _, error) = await ResolveContext();
        if (error != null) return error;

        var result = await _service.GetContactsAsync(jobId!.Value, request, ct);
        return Ok(result);
    }
}
