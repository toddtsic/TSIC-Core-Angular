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

    /// <summary>
    /// Second gate, after DenyIfCrossJob: how far inside the job the caller reaches.
    /// Director and Superuser reach every team in it. Player and Staff are confined to the
    /// team on their OWN registration -- the reach Todd specified on 2026-08-25 when he
    /// opened authoring past the admin roles.
    ///
    /// addAllTeams is REJECTED for the confined roles, never silently downgraded to their own
    /// team: a staffer who believes the whole club has their link, when one team does, is a
    /// worse outcome than a 403 they can see.
    /// </summary>
    private async Task<ActionResult?> DenyIfOutsideReach(
        Guid teamId, bool addAllTeams, CancellationToken ct)
    {
        if (HasJobWideReach()) return null;

        if (addAllTeams)
            return StatusCode(StatusCodes.Status403Forbidden, new ProblemDetails
            {
                Status = StatusCodes.Status403Forbidden,
                Type = "AllTeamsNotPermitted",
                Title = "All-Teams Not Permitted",
                Detail = "Only a director can address every team in the event. Send this to your own team instead."
            });

        var ownTeamId = await User.GetTeamIdFromRegistrationAsync(_jobLookupService, ct);

        // Fail closed: unresolvable registration, inactive registration, or unrostered caller.
        if (ownTeamId == null || ownTeamId.Value != teamId)
            return StatusCode(StatusCodes.Status403Forbidden, new ProblemDetails
            {
                Status = StatusCodes.Status403Forbidden,
                Type = "TeamReachDenied",
                Title = "Team Access Denied",
                Detail = "You can only post to your own team."
            });

        return null;
    }

    /// <summary>Director and Superuser reach the whole job; everyone else one team.</summary>
    private bool HasJobWideReach() =>
        User.IsInRole(RoleConstants.Names.SuperuserName)
        || User.IsInRole(RoleConstants.Names.DirectorName);

    /// <summary>
    /// http/https only. Now that a player can file a link, the stored value is untrusted
    /// author input that renders as a tappable target in the app and in the admin list --
    /// javascript:, data: and file: must never reach either. Scheme is checked here rather
    /// than trusting the client to repair bare hosts.
    /// </summary>
    private static bool IsSafeLinkUrl(string? docUrl) =>
        !string.IsNullOrWhiteSpace(docUrl)
        && Uri.TryCreate(docUrl, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

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

    /// <summary>
    /// Files a link on a team. Player, Staff, Director, Superuser (Todd 2026-08-25) -- the
    /// class-level [Authorize] alone admitted every role in the job, and let any of them post
    /// a job-wide link. Reach is the separate DenyIfOutsideReach check below.
    /// </summary>
    [HttpPost("links")]
    [Authorize(Policy = "CanAuthorTeamContent")]
    [ProducesResponseType(typeof(TeamLinkDto), 201)]
    [ProducesResponseType(400)]
    [ProducesResponseType(403)]
    public async Task<IActionResult> AddLink(
        Guid teamId, [FromBody] AddTeamLinkRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Label))
            return BadRequest(new { message = "Label is required." });
        if (!IsSafeLinkUrl(request.DocUrl))
            return BadRequest(new { message = "URL must be a valid http or https address." });

        if (await DenyIfCrossJob(teamId, ct) is { } d) return d;
        if (await DenyIfOutsideReach(teamId, request.AddAllTeams, ct) is { } r) return r;

        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
        var link = await _teamService.AddLinkAsync(teamId, userId, request, ct);
        return CreatedAtAction(nameof(GetLinks), new { teamId }, link);
    }

    /// <summary>
    /// Removes a link. Same role floor as AddLink. The confined roles pass
    /// allowJobLevel: false -- without it, a player on one team could delete the director's
    /// job-wide link, which the routed teamId does not catch because a job-level TeamDocs row
    /// has TeamId null and matches on JobId alone.
    /// </summary>
    [HttpDelete("links/{docId:guid}")]
    [Authorize(Policy = "CanAuthorTeamContent")]
    [ProducesResponseType(200)]
    [ProducesResponseType(403)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> DeleteLink(Guid teamId, Guid docId, CancellationToken ct)
    {
        if (await DenyIfCrossJob(teamId, ct) is { } d) return d;
        if (await DenyIfOutsideReach(teamId, addAllTeams: false, ct) is { } r) return r;

        var deleted = await _teamService.DeleteLinkAsync(docId, teamId, HasJobWideReach(), ct);
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
    /// Sends an alert to a team. Player, Staff, Director, Superuser (Todd 2026-08-25),
    /// widening the previous Director+Superuser rule for THIS route only -- the website's
    /// push screen (PushNotificationController) still holds CanSendPushNotifications.
    ///
    /// A push cannot be recalled, so the reach check is what keeps this safe: Player and
    /// Staff can only reach the devices subscribed to their own team, and addAllTeams is a
    /// 403 for them, not a downgrade.
    /// </summary>
    [HttpPost("pushes")]
    [Authorize(Policy = "CanAuthorTeamContent")]
    [ProducesResponseType(typeof(TeamPushDto), 201)]
    [ProducesResponseType(400)]
    [ProducesResponseType(403)]
    public async Task<IActionResult> SendPush(
        Guid teamId, [FromBody] SendTeamPushRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.PushText))
            return BadRequest(new { message = "Alert text is required." });

        // Redundant with the service-level check below, deliberately. The controller guard
        // keeps this action uniform with the other five; the service keeps the invariant at
        // the write chokepoint for any future caller that bypasses this controller.
        if (await DenyIfCrossJob(teamId, ct) is { } d) return d;
        if (await DenyIfOutsideReach(teamId, request.AddAllTeams, ct) is { } r) return r;

        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
        var callerJobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        var isSuperuser = User.IsInRole(RoleConstants.Names.SuperuserName);
        var jobWide = HasJobWideReach();
        var callerTeamId = jobWide
            ? null
            : await User.GetTeamIdFromRegistrationAsync(_jobLookupService, ct);

        var push = await _teamService.SendPushAsync(
            teamId, userId, callerJobId, isSuperuser, jobWide, callerTeamId, request, ct);

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
