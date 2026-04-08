using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TSIC.API.Extensions;
using TSIC.API.Services.Shared.Jobs;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Repositories;

namespace TSIC.API.Controllers;

/// <summary>
/// Anonymous-accessible roster view for public tournament rosters.
/// Gated behind Job.BScheduleAllowPublicAccess.
/// </summary>
[ApiController]
[Route("api/public-rosters")]
[AllowAnonymous]
public class PublicRosterController : ControllerBase
{
    private readonly ITeamRepository _teamRepository;
    private readonly IJobRepository _jobRepository;
    private readonly IJobLookupService _jobLookupService;

    public PublicRosterController(
        ITeamRepository teamRepository,
        IJobRepository jobRepository,
        IJobLookupService jobLookupService)
    {
        _teamRepository = teamRepository;
        _jobRepository = jobRepository;
        _jobLookupService = jobLookupService;
    }

    /// <summary>
    /// Resolve jobId from either:
    /// 1. Authenticated user's regId claim (standard path)
    /// 2. jobPath query parameter (public access path)
    /// </summary>
    private async Task<(Guid? jobId, ActionResult? error)> ResolveContext(string? jobPath = null)
    {
        // Try authenticated path first
        var regId = User.GetRegistrationId();
        if (regId.HasValue)
        {
            var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
            if (jobId == null)
                return (null, BadRequest(new { message = "Roster context required" }));
            return (jobId, null);
        }

        // Public access path — resolve from jobPath
        if (!string.IsNullOrEmpty(jobPath))
        {
            var jobId = await _jobLookupService.GetJobIdByPathAsync(jobPath);
            if (jobId == null)
                return (null, NotFound(new { message = "Event not found" }));
            return (jobId, null);
        }

        return (null, Unauthorized(new { message = "Authentication or jobPath required" }));
    }

    /// <summary>GET /api/public-rosters/tree?jobPath= — CADT tree of clubs/teams with player counts.</summary>
    [HttpGet("tree")]
    [ProducesResponseType(typeof(PublicRosterTreeDto), 200)]
    public async Task<IActionResult> GetTree([FromQuery] string? jobPath, CancellationToken ct)
    {
        var (jobId, error) = await ResolveContext(jobPath);
        if (error != null) return error;

        var isPublic = await _jobRepository.IsPublicAccessEnabledAsync(jobId!.Value, ct);
        if (!isPublic)
            return StatusCode(403, new { message = "Public rosters are not available for this event." });

        var tree = await _teamRepository.GetPublicRosterTreeAsync(jobId.Value, ct);
        return Ok(new PublicRosterTreeDto { Clubs = tree });
    }

    /// <summary>GET /api/public-rosters/team/{teamId}?jobPath= — Public roster for a specific team.</summary>
    [HttpGet("team/{teamId:guid}")]
    [ProducesResponseType(typeof(List<PublicRosterPlayerDto>), 200)]
    public async Task<IActionResult> GetTeamRoster(Guid teamId, [FromQuery] string? jobPath, CancellationToken ct)
    {
        var (jobId, error) = await ResolveContext(jobPath);
        if (error != null) return error;

        var isPublic = await _jobRepository.IsPublicAccessEnabledAsync(jobId!.Value, ct);
        if (!isPublic)
            return StatusCode(403, new { message = "Public rosters are not available for this event." });

        // Verify team belongs to this job
        var belongsToJob = await _teamRepository.BelongsToJobAsync(teamId, jobId.Value, ct);
        if (!belongsToJob)
            return NotFound(new { message = "Team not found." });

        var roster = await _teamRepository.GetPublicTeamRosterAsync(jobId.Value, teamId, ct);
        return Ok(roster);
    }
}
