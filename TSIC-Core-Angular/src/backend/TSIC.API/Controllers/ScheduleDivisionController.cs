using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using TSIC.API.Extensions;
using TSIC.API.Services.Shared.Jobs;
using TSIC.Contracts.Dtos.Scheduling;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;

namespace TSIC.API.Controllers;

[ApiController]
[Route("api/schedule-division")]
[Authorize(Policy = "AdminOnly")]
public class ScheduleDivisionController : ControllerBase
{
    private readonly ILogger<ScheduleDivisionController> _logger;
    private readonly IScheduleDivisionService _service;
    private readonly IPairingsService _pairingsService;
    private readonly IJobLookupService _jobLookupService;
    private readonly IFieldRepository _fieldRepo;

    public ScheduleDivisionController(
        ILogger<ScheduleDivisionController> logger,
        IScheduleDivisionService service,
        IPairingsService pairingsService,
        IJobLookupService jobLookupService,
        IFieldRepository fieldRepo)
    {
        _logger = logger;
        _service = service;
        _pairingsService = pairingsService;
        _jobLookupService = jobLookupService;
        _fieldRepo = fieldRepo;
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

    /// <summary>GET /api/schedule-division/agegroups — Division navigator tree (reuses pairings service).</summary>
    [HttpGet("agegroups")]
    public async Task<ActionResult<List<AgegroupWithDivisionsDto>>> GetAgegroups(CancellationToken ct)
    {
        var (jobId, _, error) = await ResolveContext();
        if (error != null) return error;

        var result = await _pairingsService.GetAgegroupsWithDivisionsAsync(jobId!.Value, ct);
        return Ok(result);
    }

    /// <summary>GET /api/schedule-division/{divId}/pairings — Pairings for the selected division.</summary>
    [HttpGet("{divId:guid}/pairings")]
    public async Task<ActionResult<DivisionPairingsResponse>> GetDivisionPairings(
        Guid divId, CancellationToken ct)
    {
        var (jobId, _, error) = await ResolveContext();
        if (error != null) return error;

        var result = await _pairingsService.GetDivisionPairingsAsync(jobId!.Value, divId, ct);
        return Ok(result);
    }

    /// <summary>GET /api/schedule-division/{divId}/teams — Teams in the selected division.</summary>
    [HttpGet("{divId:guid}/teams")]
    public async Task<ActionResult<List<DivisionTeamDto>>> GetDivisionTeams(
        Guid divId, CancellationToken ct)
    {
        var (jobId, _, error) = await ResolveContext();
        if (error != null) return error;

        var result = await _pairingsService.GetDivisionTeamsAsync(jobId!.Value, divId, ct);
        return Ok(result);
    }

    /// <summary>GET /api/schedule-division/{divId}/grid?agegroupId=X — Schedule grid for a division.</summary>
    [HttpGet("{divId:guid}/grid")]
    public async Task<ActionResult<ScheduleGridResponse>> GetScheduleGrid(
        Guid divId, [FromQuery] Guid agegroupId, CancellationToken ct)
    {
        var (jobId, _, error) = await ResolveContext();
        if (error != null) return error;

        var result = await _service.GetScheduleGridAsync(jobId!.Value, agegroupId, divId, ct);
        return Ok(result);
    }

    /// <summary>GET /api/schedule-division/who-plays-who?teamCount=N — Who plays who matrix.</summary>
    [HttpGet("who-plays-who")]
    public async Task<ActionResult<WhoPlaysWhoResponse>> GetWhoPlaysWho(
        [FromQuery] int teamCount, CancellationToken ct)
    {
        var (jobId, _, error) = await ResolveContext();
        if (error != null) return error;

        var result = await _pairingsService.GetWhoPlaysWhoAsync(jobId!.Value, teamCount, ct);
        return Ok(result);
    }

    /// <summary>POST /api/schedule-division/place-game — Place a game from a pairing into a grid slot.</summary>
    [HttpPost("place-game")]
    public async Task<ActionResult<ScheduleGameDto>> PlaceGame(
        [FromBody] PlaceGameRequest request, CancellationToken ct)
    {
        var (jobId, userId, error) = await ResolveContext();
        if (error != null) return error;

        var result = await _service.PlaceGameAsync(jobId!.Value, userId!, request, ct);
        return Ok(result);
    }

    /// <summary>POST /api/schedule-division/move-game — Move or swap a game to a new slot.</summary>
    [HttpPost("move-game")]
    public async Task<ActionResult> MoveGame(
        [FromBody] MoveGameRequest request, CancellationToken ct)
    {
        var (_, userId, error) = await ResolveContext();
        if (error != null) return error;

        await _service.MoveGameAsync(userId!, request, ct);
        return Ok();
    }

    /// <summary>DELETE /api/schedule-division/game/{gid} — Delete a single game.</summary>
    [HttpDelete("game/{gid:int}")]
    public async Task<ActionResult> DeleteGame(int gid, CancellationToken ct)
    {
        var (_, _, error) = await ResolveContext();
        if (error != null) return error;

        await _service.DeleteGameAsync(gid, ct);
        return Ok();
    }

    /// <summary>POST /api/schedule-division/auto-schedule/{divId} — Auto-schedule all round-robin pairings for a division.</summary>
    [HttpPost("auto-schedule/{divId:guid}")]
    public async Task<ActionResult<AutoScheduleResponse>> AutoScheduleDiv(
        Guid divId, CancellationToken ct)
    {
        var (jobId, userId, error) = await ResolveContext();
        if (error != null) return error;

        var result = await _service.AutoScheduleDivAsync(jobId!.Value, userId!, divId, ct);
        return Ok(result);
    }

    /// <summary>POST /api/schedule-division/delete-div-games — Delete all games for a division.</summary>
    [HttpPost("delete-div-games")]
    public async Task<ActionResult> DeleteDivGames(
        [FromBody] DeleteDivGamesRequest request, CancellationToken ct)
    {
        var (jobId, _, error) = await ResolveContext();
        if (error != null) return error;

        await _service.DeleteDivisionGamesAsync(jobId!.Value, request, ct);
        return Ok();
    }

    /// <summary>GET /api/schedule-division/field-directions/{fieldId} — Public field address/directions.</summary>
    [HttpGet("field-directions/{fieldId:guid}")]
    [AllowAnonymous]
    public async Task<ActionResult<FieldDirectionsDto>> GetFieldDirections(
        Guid fieldId, CancellationToken ct)
    {
        var field = await _fieldRepo.GetFieldByIdAsync(fieldId, ct);
        if (field == null) return NotFound();

        return Ok(new FieldDirectionsDto
        {
            Address = field.Address ?? "",
            City = field.City ?? "",
            State = field.State ?? "",
            Zip = field.Zip ?? ""
        });
    }
}
