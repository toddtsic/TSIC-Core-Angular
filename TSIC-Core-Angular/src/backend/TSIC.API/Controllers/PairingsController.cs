using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using TSIC.API.Extensions;
using TSIC.API.Services.Shared.Jobs;
using TSIC.Contracts.Dtos.Scheduling;
using TSIC.Contracts.Services;

namespace TSIC.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "AdminOnly")]
public class PairingsController : ControllerBase
{
    private readonly ILogger<PairingsController> _logger;
    private readonly IPairingsService _pairingsService;
    private readonly IJobLookupService _jobLookupService;

    public PairingsController(
        ILogger<PairingsController> logger,
        IPairingsService pairingsService,
        IJobLookupService jobLookupService)
    {
        _logger = logger;
        _pairingsService = pairingsService;
        _jobLookupService = jobLookupService;
    }

    private async Task<(Guid? jobId, string? userId, ActionResult? error)> ResolveContext()
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null)
            return (null, null, BadRequest(new { message = "Scheduling context required" }));

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
            return (null, null, Unauthorized());

        return (jobId, userId, null);
    }

    /// <summary>GET /api/pairings/agegroups — Division navigator tree.</summary>
    [HttpGet("agegroups")]
    public async Task<ActionResult<List<AgegroupWithDivisionsDto>>> GetAgegroups(CancellationToken ct)
    {
        var (jobId, _, error) = await ResolveContext();
        if (error != null) return error;

        var result = await _pairingsService.GetAgegroupsWithDivisionsAsync(jobId!.Value, ct);
        return Ok(result);
    }

    /// <summary>GET /api/pairings/division/{divId} — All pairings for a division.</summary>
    [HttpGet("division/{divId:guid}")]
    public async Task<ActionResult<DivisionPairingsResponse>> GetDivisionPairings(
        Guid divId, CancellationToken ct)
    {
        var (jobId, _, error) = await ResolveContext();
        if (error != null) return error;

        try
        {
            var result = await _pairingsService.GetDivisionPairingsAsync(jobId!.Value, divId, ct);
            return Ok(result);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    /// <summary>GET /api/pairings/who-plays-who?teamCount=N — N×N matchup matrix.</summary>
    [HttpGet("who-plays-who")]
    public async Task<ActionResult<WhoPlaysWhoResponse>> GetWhoPlaysWho(
        [FromQuery] int teamCount, CancellationToken ct)
    {
        var (jobId, _, error) = await ResolveContext();
        if (error != null) return error;

        var result = await _pairingsService.GetWhoPlaysWhoAsync(jobId!.Value, teamCount, ct);
        return Ok(result);
    }

    /// <summary>POST /api/pairings/add-block — Add round-robin pairings.</summary>
    [HttpPost("add-block")]
    public async Task<ActionResult<List<PairingDto>>> AddBlock(
        [FromBody] AddPairingBlockRequest request, CancellationToken ct)
    {
        var (jobId, userId, error) = await ResolveContext();
        if (error != null) return error;

        var result = await _pairingsService.AddPairingBlockAsync(jobId!.Value, userId!, request, ct);
        return Ok(result);
    }

    /// <summary>POST /api/pairings/add-elimination — Add single-elimination bracket.</summary>
    [HttpPost("add-elimination")]
    public async Task<ActionResult<List<PairingDto>>> AddElimination(
        [FromBody] AddSingleEliminationRequest request, CancellationToken ct)
    {
        var (jobId, userId, error) = await ResolveContext();
        if (error != null) return error;

        var result = await _pairingsService.AddSingleEliminationAsync(jobId!.Value, userId!, request, ct);
        return Ok(result);
    }

    /// <summary>POST /api/pairings/add-single — Add one blank pairing row.</summary>
    [HttpPost("add-single")]
    public async Task<ActionResult<PairingDto>> AddSingle(
        [FromBody] AddSinglePairingRequest request, CancellationToken ct)
    {
        var (jobId, userId, error) = await ResolveContext();
        if (error != null) return error;

        var result = await _pairingsService.AddSinglePairingAsync(jobId!.Value, userId!, request, ct);
        return Ok(result);
    }

    /// <summary>PUT /api/pairings — Inline edit a pairing.</summary>
    [HttpPut]
    public async Task<ActionResult> EditPairing(
        [FromBody] EditPairingRequest request, CancellationToken ct)
    {
        var (_, userId, error) = await ResolveContext();
        if (error != null) return error;

        try
        {
            await _pairingsService.EditPairingAsync(userId!, request, ct);
            return NoContent();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    /// <summary>DELETE /api/pairings/{ai} — Delete a single pairing.</summary>
    [HttpDelete("{ai:int}")]
    public async Task<ActionResult> DeletePairing(int ai, CancellationToken ct)
    {
        var (_, _, error) = await ResolveContext();
        if (error != null) return error;

        try
        {
            await _pairingsService.DeletePairingAsync(ai, ct);
            return NoContent();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    /// <summary>POST /api/pairings/remove-all — Remove all pairings for a team count.</summary>
    [HttpPost("remove-all")]
    public async Task<ActionResult> RemoveAll(
        [FromBody] RemoveAllPairingsRequest request, CancellationToken ct)
    {
        var (jobId, _, error) = await ResolveContext();
        if (error != null) return error;

        await _pairingsService.RemoveAllPairingsAsync(jobId!.Value, request, ct);
        return NoContent();
    }

    // ── Division Teams ──

    /// <summary>GET /api/pairings/division/{divId}/teams — Teams in a division with rank and club name.</summary>
    [HttpGet("division/{divId:guid}/teams")]
    public async Task<ActionResult<List<DivisionTeamDto>>> GetDivisionTeams(
        Guid divId, CancellationToken ct)
    {
        var (jobId, _, error) = await ResolveContext();
        if (error != null) return error;

        var result = await _pairingsService.GetDivisionTeamsAsync(jobId!.Value, divId, ct);
        return Ok(result);
    }

    /// <summary>PUT /api/pairings/division-team — Edit team rank/name (rank changes swap).</summary>
    [HttpPut("division-team")]
    public async Task<ActionResult<List<DivisionTeamDto>>> EditDivisionTeam(
        [FromBody] EditDivisionTeamRequest request, CancellationToken ct)
    {
        var (jobId, userId, error) = await ResolveContext();
        if (error != null) return error;

        try
        {
            var result = await _pairingsService.EditDivisionTeamAsync(
                jobId!.Value, userId!, request, ct);
            return Ok(result);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }
}
