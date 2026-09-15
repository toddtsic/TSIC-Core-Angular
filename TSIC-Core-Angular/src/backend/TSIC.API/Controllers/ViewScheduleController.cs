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
/// Every schedule-data endpoint is gated by <see cref="IViewScheduleService.CanViewScheduleAsync"/>:
/// anyone the rule excludes gets the generic "Schedule not available" 404 (<see cref="ScheduleNotAvailable"/>).
/// </summary>
[ApiController]
[Route("api/view-schedule")]
public class ViewScheduleController : ControllerBase
{
    private readonly IViewScheduleService _service;
    private readonly IJobLookupService _jobLookupService;

    public ViewScheduleController(
        IViewScheduleService service,
        IJobLookupService jobLookupService)
    {
        _service = service;
        _jobLookupService = jobLookupService;
    }

    /// <summary>
    /// Resolve jobId from either:
    /// 1. jobPath query parameter — the event the page is showing, whether or not the caller is logged in
    /// 2. Authenticated user's regId claim — admin surfaces that pass no jobPath
    /// Returns (jobId, userId, isAdmin, error). isAdmin holds only when the caller's login is for THIS job.
    /// </summary>
    private async Task<(Guid? jobId, string? userId, bool isAdmin, ActionResult? error)> ResolveContext(
        string? jobPath = null)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var callerJobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);

        // jobPath wins when given. Preferring the login's job served the logged-in event's schedule
        // on another event's page, and its admin rights with it.
        if (!string.IsNullOrEmpty(jobPath))
        {
            var jobId = await _jobLookupService.GetJobIdByPathAsync(jobPath);
            if (jobId == null)
                return (null, null, false, ScheduleNotAvailable());

            return (jobId, userId, callerJobId == jobId && IsAdminRole(), null);
        }

        if (User.GetRegistrationId().HasValue)
        {
            if (callerJobId == null)
                return (null, null, false, BadRequest(new { message = "Schedule context required" }));

            return (callerJobId, userId, IsAdminRole(), null);
        }

        return (null, null, false, Unauthorized(new { message = "Authentication or jobPath required" }));
    }

    private bool IsAdminRole() => CallerRole() is "Superuser" or "Director" or "SuperDirector" or "Scorer";

    /// <summary>The caller's role name; null when anonymous.</summary>
    private string? CallerRole() =>
        // Check role using both mapped (ClaimTypes.Role) and unmapped ("role") claim types.
        // .NET 10's JsonWebTokenHandler may not remap "role" → ClaimTypes.Role.
        User.FindFirstValue(ClaimTypes.Role) ?? User.FindFirstValue("role");

    /// <summary>Whether the caller may see <paramref name="jobId"/>'s schedule.</summary>
    private Task<bool> CallerCanViewAsync(Guid jobId, CancellationToken ct) =>
        HttpContext.CanViewScheduleAsync(jobId, _jobLookupService, _service, ct);

    /// <summary>
    /// THE refusal for schedule data. Unreleased, not permitted, unknown event, unknown game or team: all
    /// get this identical 404, so a caller cannot tell a hidden schedule from one that doesn't exist.
    /// </summary>
    internal static NotFoundObjectResult ScheduleNotAvailable() => new(new ProblemDetails
    {
        Status = StatusCodes.Status404NotFound,
        Detail = "Schedule not available"
    });

    /// <summary><see cref="ScheduleNotAvailable"/> unless the caller may see <paramref name="jobId"/>'s schedule; null = allowed.</summary>
    private async Task<ActionResult?> UnavailableUnlessCanViewAsync(Guid jobId, CancellationToken ct) =>
        await CallerCanViewAsync(jobId, ct) ? null : ScheduleNotAvailable();

    // ══════════════════════════════════════════════════════════════
    // Public-accessible endpoints (gated by CanViewScheduleAsync)
    // ══════════════════════════════════════════════════════════════

    /// <summary>GET /api/view-schedule/filter-options?jobPath= — CADT tree + game days + fields.</summary>
    [AllowAnonymous]
    [HttpGet("filter-options")]
    public async Task<ActionResult<ScheduleFilterOptionsDto>> GetFilterOptions(
        [FromQuery] string? jobPath, CancellationToken ct)
    {
        var (jobId, _, _, error) = await ResolveContext(jobPath);
        if (error != null) return error;
        var unavailable = await UnavailableUnlessCanViewAsync(jobId!.Value, ct);
        if (unavailable != null) return unavailable;

        var result = await _service.GetFilterOptionsAsync(jobId.Value, ct);
        return Ok(result);
    }

    /// <summary>
    /// GET /api/view-schedule/capabilities?jobPath= — Feature flags for this job/user.
    /// Deliberately ungated: its CanView flag is how the page learns the schedule is unreleased.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("capabilities")]
    public async Task<ActionResult<ScheduleCapabilitiesDto>> GetCapabilities(
        [FromQuery] string? jobPath, CancellationToken ct)
    {
        var (jobId, _, isAdmin, error) = await ResolveContext(jobPath);
        if (error != null) return error;

        var isAuthenticated = User.Identity?.IsAuthenticated == true;
        var canView = await CallerCanViewAsync(jobId!.Value, ct);
        var result = await _service.GetCapabilitiesAsync(jobId.Value, isAuthenticated, isAdmin, canView, ct);
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
        var unavailable = await UnavailableUnlessCanViewAsync(jobId!.Value, ct);
        if (unavailable != null) return unavailable;

        // Server-side paging is opt-in via request.Skip/Take. Take omitted ⇒ full unpaginated
        // body (identical to before). X-Total-Count = total matches before paging; the client
        // uses it for a "showing N of M" affordance and end-of-list detection. Exposed via CORS
        // (Program.cs WithExposedHeaders) so the browser can read it cross-origin.
        var (games, total) = await _service.GetGamesPagedAsync(jobId.Value, request, ct);
        Response.Headers["X-Total-Count"] = total.ToString();
        return Ok(games);
    }

    /// <summary>POST /api/view-schedule/standings?jobPath= — Pool play standings by division.</summary>
    [AllowAnonymous]
    [HttpPost("standings")]
    public async Task<ActionResult<StandingsByDivisionResponse>> GetStandings(
        [FromBody] ScheduleFilterRequest request, [FromQuery] string? jobPath, CancellationToken ct)
    {
        var (jobId, _, _, error) = await ResolveContext(jobPath);
        if (error != null) return error;
        var unavailable = await UnavailableUnlessCanViewAsync(jobId!.Value, ct);
        if (unavailable != null) return unavailable;

        // Paged over DIVISIONS (the standings unit). X-Total-Count = total division count for the
        // filter. Take omitted ⇒ full response, identical to before.
        var (result, total) = await _service.GetStandingsPagedAsync(jobId.Value, request, ct);
        Response.Headers["X-Total-Count"] = total.ToString();
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
        var unavailable = await UnavailableUnlessCanViewAsync(jobId!.Value, ct);
        if (unavailable != null) return unavailable;

        var result = await _service.GetTeamRecordsAsync(jobId.Value, request, ct);
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
        var unavailable = await UnavailableUnlessCanViewAsync(jobId!.Value, ct);
        if (unavailable != null) return unavailable;

        var result = await _service.GetBracketsAsync(jobId.Value, request, ct);
        return Ok(result);
    }

    /// <summary>GET /api/view-schedule/team-results/{teamId}?jobPath= — Team game history drill-down.</summary>
    [AllowAnonymous]
    [HttpGet("team-results/{teamId:guid}")]
    public async Task<ActionResult<TeamResultsResponse>> GetTeamResults(
        Guid teamId, [FromQuery] string? jobPath, CancellationToken ct)
    {
        var (_, _, _, error) = await ResolveContext(jobPath);
        if (error != null) return error;

        // Gate on the job that OWNS the team, not the jobPath — the teamId can name any job's team.
        var teamJobId = await _jobLookupService.GetJobIdByTeamAsync(teamId, ct);
        if (teamJobId == null) return ScheduleNotAvailable();
        var unavailable = await UnavailableUnlessCanViewAsync(teamJobId.Value, ct);
        if (unavailable != null) return unavailable;

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

    // ── Mobile deep-link lookups ──
    // The Events app navigates from a game row or team where it holds only a
    // gid/teamId and cannot compose a division filter (ViewGameDto carries no divId).
    // The owning job is resolved server-side from the row itself, and the gate runs
    // against that job — gids are sequential, so they must never bypass it.

    /// <summary>GET /api/view-schedule/brackets/by-game/{gid} — Brackets for the game's division.</summary>
    [AllowAnonymous]
    [HttpGet("brackets/by-game/{gid:int}")]
    public async Task<ActionResult<List<DivisionBracketResponse>>> GetBracketsByGame(int gid, CancellationToken ct)
    {
        var gameJobId = await _service.GetGameJobIdAsync(gid, ct);
        if (gameJobId == null) return ScheduleNotAvailable();
        var unavailable = await UnavailableUnlessCanViewAsync(gameJobId.Value, ct);
        if (unavailable != null) return unavailable;

        var result = await _service.GetBracketsByGameAsync(gid, ct);
        if (result == null) return NotFound();
        return Ok(result);
    }

    /// <summary>GET /api/view-schedule/brackets/by-team/{teamId} — Brackets for the team's division.</summary>
    [AllowAnonymous]
    [HttpGet("brackets/by-team/{teamId:guid}")]
    public async Task<ActionResult<List<DivisionBracketResponse>>> GetBracketsByTeam(Guid teamId, CancellationToken ct)
    {
        var teamJobId = await _jobLookupService.GetJobIdByTeamAsync(teamId, ct);
        if (teamJobId == null) return ScheduleNotAvailable();
        var unavailable = await UnavailableUnlessCanViewAsync(teamJobId.Value, ct);
        if (unavailable != null) return unavailable;

        var result = await _service.GetBracketsByTeamAsync(teamId, ct);
        if (result == null) return NotFound();
        return Ok(result);
    }

    /// <summary>GET /api/view-schedule/standings/by-game/{gid} — Pool standings for the game's division.</summary>
    [AllowAnonymous]
    [HttpGet("standings/by-game/{gid:int}")]
    public async Task<ActionResult<StandingsByDivisionResponse>> GetStandingsByGame(int gid, CancellationToken ct)
    {
        var gameJobId = await _service.GetGameJobIdAsync(gid, ct);
        if (gameJobId == null) return ScheduleNotAvailable();
        var unavailable = await UnavailableUnlessCanViewAsync(gameJobId.Value, ct);
        if (unavailable != null) return unavailable;

        var result = await _service.GetStandingsByGameAsync(gid, ct);
        if (result == null) return NotFound();
        return Ok(result);
    }

    /// <summary>GET /api/view-schedule/standings/by-team/{teamId} — Pool standings for the team's division.</summary>
    [AllowAnonymous]
    [HttpGet("standings/by-team/{teamId:guid}")]
    public async Task<ActionResult<StandingsByDivisionResponse>> GetStandingsByTeam(Guid teamId, CancellationToken ct)
    {
        var teamJobId = await _jobLookupService.GetJobIdByTeamAsync(teamId, ct);
        if (teamJobId == null) return ScheduleNotAvailable();
        var unavailable = await UnavailableUnlessCanViewAsync(teamJobId.Value, ct);
        if (unavailable != null) return unavailable;

        var result = await _service.GetStandingsByTeamAsync(teamId, ct);
        if (result == null) return NotFound();
        return Ok(result);
    }

    // ══════════════════════════════════════════════════════════════
    // Admin/Scorer-only endpoints
    // ══════════════════════════════════════════════════════════════

    /// <summary>POST /api/view-schedule/quick-score — Inline score edit. Admin roles + Scorer.</summary>
    [Authorize(Policy = "CanScore")]
    [HttpPost("quick-score")]
    public async Task<ActionResult> QuickEditScore(
        [FromBody] EditScoreRequest request, CancellationToken ct)
    {
        var (jobId, userId, _, error) = await ResolveContext();
        if (error != null) return error;

        try
        {
            await _service.QuickEditScoreAsync(jobId!.Value, userId!, request, ct);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { Message = ex.Message });
        }
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

        try
        {
            await _service.EditGameAsync(jobId!.Value, userId!, request, ct);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { Message = ex.Message });
        }
        return Ok();
    }

    /// <summary>
    /// POST /api/view-schedule/rebuild-records — one-time deploy-time backfill of the stored pool
    /// record columns for this job's teams. Idempotent; safe to re-run. Returns the count written.
    /// </summary>
    [Authorize(Policy = "AdminOnly")]
    [HttpPost("rebuild-records")]
    public async Task<ActionResult> RebuildRecords(CancellationToken ct)
    {
        var (jobId, _, _, error) = await ResolveContext();
        if (error != null) return error;

        var written = await _service.RebuildTeamRecordsAsync(jobId!.Value, ct);
        return Ok(new { TeamsUpdated = written });
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
        var unavailable = await UnavailableUnlessCanViewAsync(jobId!.Value, ct);
        if (unavailable != null) return unavailable;

        var result = await _service.GetContactsAsync(jobId.Value, request, ct);
        return Ok(result);
    }
}
