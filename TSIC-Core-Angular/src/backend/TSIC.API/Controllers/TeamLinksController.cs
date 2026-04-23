using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TSIC.API.Extensions;
using TSIC.API.Services.Shared.Jobs;
using TSIC.Contracts.Dtos.TeamLink;
using TSIC.Contracts.Services;

namespace TSIC.API.Controllers;

/// <summary>
/// Admin endpoints for managing team links — labeled URLs surfaced to players
/// in the TSIC-Teams mobile app. Replaces legacy MobileTeamLinks/JobTeamLinks.
/// </summary>
[ApiController]
[Route("api/team-links")]
[Authorize(Policy = "AdminOnly")]
public class TeamLinksController : ControllerBase
{
    private readonly ITeamLinkService _service;
    private readonly IJobLookupService _jobLookupService;

    public TeamLinksController(
        ITeamLinkService service,
        IJobLookupService jobLookupService)
    {
        _service = service;
        _jobLookupService = jobLookupService;
    }

    [HttpGet]
    [ProducesResponseType(typeof(List<AdminTeamLinkDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<List<AdminTeamLinkDto>>> GetTeamLinks(CancellationToken ct)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null)
            return BadRequest(new { message = "Registration context required." });

        var links = await _service.GetForJobAsync(jobId.Value, ct);
        return Ok(links);
    }

    [HttpGet("available-teams")]
    [ProducesResponseType(typeof(List<TeamLinkTeamOptionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<List<TeamLinkTeamOptionDto>>> GetAvailableTeams(CancellationToken ct)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null)
            return BadRequest(new { message = "Registration context required." });

        var teams = await _service.GetAvailableTeamsAsync(jobId.Value, ct);
        return Ok(teams);
    }

    [HttpPost]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> AddTeamLink(
        [FromBody] CreateTeamLinkRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Label))
            return BadRequest(new { message = "Label is required." });
        if (string.IsNullOrWhiteSpace(request.DocUrl))
            return BadRequest(new { message = "URL is required." });

        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null)
            return BadRequest(new { message = "Registration context required." });

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
            return Unauthorized();

        await _service.AddAsync(jobId.Value, userId, request, ct);
        return NoContent();
    }

    [HttpPut("{docId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateTeamLink(
        Guid docId,
        [FromBody] UpdateTeamLinkRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Label))
            return BadRequest(new { message = "Label is required." });
        if (string.IsNullOrWhiteSpace(request.DocUrl))
            return BadRequest(new { message = "URL is required." });

        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null)
            return BadRequest(new { message = "Registration context required." });

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
            return Unauthorized();

        await _service.UpdateAsync(jobId.Value, userId, docId, request, ct);
        return NoContent();
    }

    [HttpDelete("{docId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> DeleteTeamLink(Guid docId, CancellationToken ct)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null)
            return BadRequest(new { message = "Registration context required." });

        await _service.DeleteAsync(jobId.Value, docId, ct);
        return NoContent();
    }
}
