using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TSIC.API.Extensions;
using TSIC.API.Services.Shared.Jobs;
using TSIC.Contracts.Dtos.ClubRoster;
using TSIC.Contracts.Services;

namespace TSIC.API.Controllers;

[ApiController]
[Route("api/club-rosters")]
[Authorize]
public class ClubRosterController : ControllerBase
{
    private readonly IClubRosterService _clubRosterService;
    private readonly IJobLookupService _jobLookupService;

    public ClubRosterController(
        IClubRosterService clubRosterService,
        IJobLookupService jobLookupService)
    {
        _clubRosterService = clubRosterService;
        _jobLookupService = jobLookupService;
    }

    [HttpGet("teams")]
    public async Task<ActionResult<List<ClubRosterTeamDto>>> GetTeams(CancellationToken ct)
    {
        var (regId, jobId) = await ResolveContextAsync();
        if (regId == null || jobId == null)
            return BadRequest(new { message = "Registration context required." });

        var teams = await _clubRosterService.GetTeamsAsync(regId.Value, jobId.Value, ct);
        return Ok(teams);
    }

    [HttpGet("teams/{teamId:guid}/roster")]
    public async Task<ActionResult<List<ClubRosterPlayerDto>>> GetRoster(Guid teamId, CancellationToken ct)
    {
        var (regId, jobId) = await ResolveContextAsync();
        if (regId == null || jobId == null)
            return BadRequest(new { message = "Registration context required." });

        try
        {
            var roster = await _clubRosterService.GetRosterAsync(teamId, regId.Value, jobId.Value, ct);
            return Ok(roster);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    [HttpPut("move-players")]
    public async Task<ActionResult<ClubRosterMutationResultDto>> MovePlayers(
        [FromBody] MovePlayersRequest request, CancellationToken ct)
    {
        var (regId, jobId) = await ResolveContextAsync();
        if (regId == null || jobId == null)
            return BadRequest(new { message = "Registration context required." });

        try
        {
            var result = await _clubRosterService.MovePlayersAsync(request, regId.Value, jobId.Value, ct);
            return Ok(result);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    [HttpPost("delete-players")]
    public async Task<ActionResult<ClubRosterMutationResultDto>> DeletePlayers(
        [FromBody] DeletePlayersRequest request, CancellationToken ct)
    {
        var (regId, jobId) = await ResolveContextAsync();
        if (regId == null || jobId == null)
            return BadRequest(new { message = "Registration context required." });

        try
        {
            var result = await _clubRosterService.DeletePlayersAsync(request, regId.Value, jobId.Value, ct);
            return Ok(result);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    [HttpPatch("uniform-number")]
    public async Task<ActionResult> UpdateUniformNumber(
        [FromBody] UpdateUniformNumberRequest request, CancellationToken ct)
    {
        var (regId, jobId) = await ResolveContextAsync();
        if (regId == null || jobId == null)
            return BadRequest(new { message = "Registration context required." });

        try
        {
            await _clubRosterService.UpdateUniformNumberAsync(request, regId.Value, jobId.Value, ct);
            return NoContent();
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    private async Task<(Guid? regId, Guid? jobId)> ResolveContextAsync()
    {
        var regId = User.GetRegistrationId();
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        return (regId, jobId);
    }
}
