using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TSIC.API.Extensions;
using TSIC.API.Services.Shared.Jobs;
using TSIC.Contracts.Dtos.CampGroups;
using TSIC.Contracts.Dtos.CheckIn;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;

namespace TSIC.API.Controllers;

/// <summary>
/// Live check-in (staff station) — replaces the legacy static check-in report family.
/// Two modes by job type: team check-in (Tournament / League) and player check-in
/// (Camp / Tryouts). Records arrival, surfaces balance + med-form status; payment and
/// doc viewing reuse the existing payment flow and med-form endpoints.
/// AdminOnly; every action derives job scope from the caller's regId claim.
/// </summary>
[ApiController]
[Route("api/checkin")]
[Authorize(Policy = "AdminOnly")]
public class CheckinController : ControllerBase
{
    private readonly ICheckinService _service;
    private readonly ITeamRepository _teamRepo;
    private readonly IJobLookupService _jobLookupService;

    public CheckinController(
        ICheckinService service,
        ITeamRepository teamRepo,
        IJobLookupService jobLookupService)
    {
        _service = service;
        _teamRepo = teamRepo;
        _jobLookupService = jobLookupService;
    }

    /// <summary>
    /// GET /api/checkin/teams — active teams in the current job with player counts
    /// (team picker for player-mode check-in).
    /// </summary>
    [HttpGet("teams")]
    public async Task<ActionResult<List<TeamRosterCountDto>>> GetTeams(CancellationToken ct)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId is null)
            return BadRequest(new { message = "Registration context required." });

        return Ok(await _service.GetTeamsAsync(jobId.Value, ct));
    }

    /// <summary>
    /// GET /api/checkin/team-roster — team check-in roster for the current job
    /// (Tournament / League): every active team with clubrep balance and check-in state.
    /// </summary>
    [HttpGet("team-roster")]
    public async Task<ActionResult<List<TeamCheckinRowDto>>> GetTeamRoster(CancellationToken ct)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId is null)
            return BadRequest(new { message = "Registration context required." });

        return Ok(await _service.GetTeamRosterAsync(jobId.Value, ct));
    }

    /// <summary>
    /// GET /api/checkin/teams/{teamId}/players — player check-in roster for a team
    /// (Camp / Tryouts): active players with balance, med-form flag, and check-in state.
    /// </summary>
    [HttpGet("teams/{teamId:guid}/players")]
    public async Task<ActionResult<List<PlayerCheckinRowDto>>> GetPlayers(Guid teamId, CancellationToken ct)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId is null)
            return BadRequest(new { message = "Registration context required." });

        var belongs = await _teamRepo.BelongsToJobAsync(teamId, jobId.Value, ct);
        if (!belongs)
            return NotFound(new { message = "Team not found in current job." });

        return Ok(await _service.GetPlayerRosterAsync(teamId, ct));
    }

    /// <summary>
    /// POST /api/checkin/players/{regId} — check a player in (idempotent). Records the
    /// caller's regId as CheckedInByRegId. Returns the resulting check-in state.
    /// </summary>
    [HttpPost("players/{regId:guid}")]
    public async Task<ActionResult<CheckinStateDto>> CheckInPlayer(Guid regId, CancellationToken ct)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId is null)
            return BadRequest(new { message = "Registration context required." });

        var byRegId = User.GetRegistrationId();
        if (byRegId is null)
            return BadRequest(new { message = "Registration context required." });

        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        var state = await _service.CheckInPlayerAsync(jobId.Value, regId, byRegId.Value, userId, ct);
        if (state is null)
            return NotFound(new { message = "Registration not found in current job." });

        return Ok(state);
    }

    /// <summary>
    /// DELETE /api/checkin/players/{regId} — undo a player check-in.
    /// </summary>
    [HttpDelete("players/{regId:guid}")]
    public async Task<IActionResult> UndoPlayer(Guid regId, CancellationToken ct)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId is null)
            return BadRequest(new { message = "Registration context required." });

        var undone = await _service.UndoPlayerCheckInAsync(jobId.Value, regId, ct);
        if (!undone)
            return NotFound(new { message = "Registration not checked in, or not in current job." });

        return NoContent();
    }

    /// <summary>
    /// POST /api/checkin/teams/{teamId} — check a team in (idempotent). Records the
    /// caller's regId as CheckedInByRegId. Returns the resulting check-in state.
    /// </summary>
    [HttpPost("teams/{teamId:guid}")]
    public async Task<ActionResult<CheckinStateDto>> CheckInTeam(Guid teamId, CancellationToken ct)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId is null)
            return BadRequest(new { message = "Registration context required." });

        var byRegId = User.GetRegistrationId();
        if (byRegId is null)
            return BadRequest(new { message = "Registration context required." });

        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        var state = await _service.CheckInTeamAsync(jobId.Value, teamId, byRegId.Value, userId, ct);
        if (state is null)
            return NotFound(new { message = "Team not found in current job." });

        return Ok(state);
    }

    /// <summary>
    /// DELETE /api/checkin/teams/{teamId} — undo a team check-in.
    /// </summary>
    [HttpDelete("teams/{teamId:guid}")]
    public async Task<IActionResult> UndoTeam(Guid teamId, CancellationToken ct)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId is null)
            return BadRequest(new { message = "Registration context required." });

        var undone = await _service.UndoTeamCheckInAsync(jobId.Value, teamId, ct);
        if (!undone)
            return NotFound(new { message = "Team not checked in, or not in current job." });

        return NoContent();
    }
}
