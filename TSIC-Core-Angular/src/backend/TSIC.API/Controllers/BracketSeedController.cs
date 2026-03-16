using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TSIC.API.Extensions;
using TSIC.API.Services.Shared.Jobs;
using TSIC.Contracts.Dtos.Scheduling;
using TSIC.Contracts.Services;

namespace TSIC.API.Controllers;

/// <summary>
/// Bracket Seeds — assign division-ranked teams to bracket/playoff game slots.
/// </summary>
[ApiController]
[Route("api/bracket-seeds")]
[Authorize]
public class BracketSeedController : ControllerBase
{
    private readonly IBracketSeedService _service;
    private readonly IJobLookupService _jobLookupService;

    public BracketSeedController(
        IBracketSeedService service,
        IJobLookupService jobLookupService)
    {
        _service = service;
        _jobLookupService = jobLookupService;
    }

    /// <summary>GET /api/bracket-seeds — All bracket games with seed data.</summary>
    [HttpGet]
    public async Task<ActionResult<List<BracketSeedGameDto>>> GetBracketGames(
        CancellationToken ct)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null)
            return BadRequest(new { message = "Job context required" });

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        var result = await _service.GetBracketGamesAsync(jobId.Value, userId, ct);
        return Ok(result);
    }

    /// <summary>PUT /api/bracket-seeds — Update seed assignment for a bracket game.</summary>
    [HttpPut]
    public async Task<ActionResult<BracketSeedGameDto>> UpdateSeed(
        [FromBody] UpdateBracketSeedRequest request,
        CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        var result = await _service.UpdateSeedAsync(request, userId, ct);
        return Ok(result);
    }

    /// <summary>GET /api/bracket-seeds/divisions/{gid} — Available divisions for seed dropdown.</summary>
    [HttpGet("divisions/{gid:int}")]
    public async Task<ActionResult<List<BracketSeedDivisionOptionDto>>> GetDivisionsForGame(
        int gid, CancellationToken ct)
    {
        var result = await _service.GetDivisionsForGameAsync(gid, ct);
        return Ok(result);
    }
}
