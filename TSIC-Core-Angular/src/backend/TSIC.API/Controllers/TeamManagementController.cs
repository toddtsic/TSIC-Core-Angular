using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TSIC.API.Extensions;
using TSIC.API.Services.Shared.Jobs;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Services;
using TSIC.Domain.Constants;

namespace TSIC.API.Controllers;

/// <summary>
/// Team management endpoints for roster, links, and push notifications.
/// Used by TSIC-Teams mobile app and available to Angular admin frontend.
/// </summary>
[ApiController]
[Authorize]
[Route("api/teams/{teamId:guid}")]
public class TeamManagementController : ControllerBase
{
    private readonly ITeamManagementService _teamService;
    private readonly IJobLookupService _jobLookupService;

    public TeamManagementController(
        ITeamManagementService teamService,
        IJobLookupService jobLookupService)
    {
        _teamService = teamService;
        _jobLookupService = jobLookupService;
    }

    /// <summary>
    /// Rejects a teamId belonging to another job. Explicit and per-action by design --
    /// nothing in the API scopes by job ambiently (JobPathMatchRequirement is wired into
    /// no policy and abstains on routes without a jobPath segment), so every action that
    /// takes teamId off the route must say so itself. Superuser exempt, matching
    /// JobConfigController and JobVisibilityController.
    /// </summary>
    private async Task<ActionResult?> DenyIfCrossJob(Guid teamId, CancellationToken ct)
    {
        if (User.IsInRole(RoleConstants.Names.SuperuserName)) return null;

        var callerJobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        var teamJobId = await _jobLookupService.GetJobIdByTeamAsync(teamId, ct);

        // Fail closed: unresolvable caller job (phase-1 token, no regId) or unknown team.
        if (callerJobId == null || teamJobId == null || callerJobId.Value != teamJobId.Value)
            return StatusCode(StatusCodes.Status403Forbidden, new ProblemDetails
            {
                Status = StatusCodes.Status403Forbidden,
                Type = "TeamJobMismatch",
                Title = "Team Access Denied",
                Detail = "This team belongs to a different event than the one you are logged into."
            });

        return null;
    }

    // ── Roster ──

    [HttpGet("roster")]
    [ProducesResponseType(typeof(TeamRosterDetailDto), 200)]
    public async Task<IActionResult> GetRoster(Guid teamId, CancellationToken ct)
    {
        if (await DenyIfCrossJob(teamId, ct) is { } d) return d;

        var roster = await _teamService.GetRosterAsync(teamId, ct);
        return Ok(roster);
    }

    // ── Links ──

    [HttpGet("links")]
    [ProducesResponseType(typeof(List<TeamLinkDto>), 200)]
    public async Task<IActionResult> GetLinks(Guid teamId, CancellationToken ct)
    {
        if (await DenyIfCrossJob(teamId, ct) is { } d) return d;

        var links = await _teamService.GetLinksAsync(teamId, ct);
        return Ok(links);
    }

    [HttpPost("links")]
    [ProducesResponseType(typeof(TeamLinkDto), 201)]
    [ProducesResponseType(400)]
    public async Task<IActionResult> AddLink(
        Guid teamId, [FromBody] AddTeamLinkRequest request, CancellationToken ct)
    {
        if (await DenyIfCrossJob(teamId, ct) is { } d) return d;

        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
        var link = await _teamService.AddLinkAsync(teamId, userId, request, ct);
        return CreatedAtAction(nameof(GetLinks), new { teamId }, link);
    }

    [HttpDelete("links/{docId:guid}")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> DeleteLink(Guid teamId, Guid docId, CancellationToken ct)
    {
        if (await DenyIfCrossJob(teamId, ct) is { } d) return d;

        var deleted = await _teamService.DeleteLinkAsync(docId, teamId, ct);
        return deleted ? Ok() : NotFound();
    }

    // ── Pushes ──

    [HttpGet("pushes")]
    [ProducesResponseType(typeof(List<TeamPushDto>), 200)]
    public async Task<IActionResult> GetPushes(Guid teamId, CancellationToken ct)
    {
        if (await DenyIfCrossJob(teamId, ct) is { } d) return d;

        var pushes = await _teamService.GetPushesAsync(teamId, ct);
        return Ok(pushes);
    }

    /// <summary>
    /// Sends a push to a team. Director + Superuser only -- same policy as the website's
    /// push screen. Applied to this action alone: the class-level [Authorize] has to stay
    /// open for the roster and link reads the mobile app makes as staff and coach.
    /// </summary>
    [HttpPost("pushes")]
    [Authorize(Policy = "CanSendPushNotifications")]
    [ProducesResponseType(typeof(TeamPushDto), 201)]
    [ProducesResponseType(400)]
    [ProducesResponseType(403)]
    public async Task<IActionResult> SendPush(
        Guid teamId, [FromBody] SendTeamPushRequest request, CancellationToken ct)
    {
        // Redundant with the service-level check below, deliberately. The controller guard
        // keeps this action uniform with the other five; the service keeps the invariant at
        // the write chokepoint for any future caller that bypasses this controller.
        if (await DenyIfCrossJob(teamId, ct) is { } d) return d;

        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
        var callerJobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        var isSuperuser = User.IsInRole(RoleConstants.Names.SuperuserName);

        var push = await _teamService.SendPushAsync(
            teamId, userId, callerJobId, isSuperuser, request, ct);

        if (push == null)
            return StatusCode(403, new ProblemDetails
            {
                Status = 403,
                Type = "TeamJobMismatch",
                Title = "Team Access Denied",
                Detail = "This team belongs to a different event than the one you are logged into."
            });

        return CreatedAtAction(nameof(GetPushes), new { teamId }, push);
    }
}
