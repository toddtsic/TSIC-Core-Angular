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

    // ── Roster ──

    [HttpGet("roster")]
    [ProducesResponseType(typeof(TeamRosterDetailDto), 200)]
    public async Task<IActionResult> GetRoster(Guid teamId, CancellationToken ct)
    {
        var roster = await _teamService.GetRosterAsync(teamId, ct);
        return Ok(roster);
    }

    // ── Links ──

    [HttpGet("links")]
    [ProducesResponseType(typeof(List<TeamLinkDto>), 200)]
    public async Task<IActionResult> GetLinks(Guid teamId, CancellationToken ct)
    {
        var links = await _teamService.GetLinksAsync(teamId, ct);
        return Ok(links);
    }

    [HttpPost("links")]
    [ProducesResponseType(typeof(TeamLinkDto), 201)]
    [ProducesResponseType(400)]
    public async Task<IActionResult> AddLink(
        Guid teamId, [FromBody] AddTeamLinkRequest request, CancellationToken ct)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
        var link = await _teamService.AddLinkAsync(teamId, userId, request, ct);
        return CreatedAtAction(nameof(GetLinks), new { teamId }, link);
    }

    [HttpDelete("links/{docId:guid}")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> DeleteLink(Guid teamId, Guid docId, CancellationToken ct)
    {
        var deleted = await _teamService.DeleteLinkAsync(docId, ct);
        return deleted ? Ok() : NotFound();
    }

    // ── Pushes ──

    [HttpGet("pushes")]
    [ProducesResponseType(typeof(List<TeamPushDto>), 200)]
    public async Task<IActionResult> GetPushes(Guid teamId, CancellationToken ct)
    {
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
