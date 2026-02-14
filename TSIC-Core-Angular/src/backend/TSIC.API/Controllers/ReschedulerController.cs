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
public class ReschedulerController : ControllerBase
{
    private readonly IReschedulerService _service;
    private readonly IJobLookupService _jobLookupService;

    public ReschedulerController(
        IReschedulerService service,
        IJobLookupService jobLookupService)
    {
        _service = service;
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

    /// <summary>GET /api/rescheduler/filter-options — CADT tree + game days + fields.</summary>
    [HttpGet("filter-options")]
    public async Task<ActionResult<ScheduleFilterOptionsDto>> GetFilterOptions(CancellationToken ct)
    {
        var (jobId, _, error) = await ResolveContext();
        if (error != null) return error;

        var result = await _service.GetFilterOptionsAsync(jobId!.Value, ct);
        return Ok(result);
    }

    /// <summary>POST /api/rescheduler/grid — Cross-division schedule grid with filters.</summary>
    [HttpPost("grid")]
    public async Task<ActionResult<ScheduleGridResponse>> GetGrid(
        [FromBody] ReschedulerGridRequest request, CancellationToken ct)
    {
        var (jobId, _, error) = await ResolveContext();
        if (error != null) return error;

        var result = await _service.GetReschedulerGridAsync(jobId!.Value, request, ct);
        return Ok(result);
    }

    /// <summary>POST /api/rescheduler/move-game — Move or swap a game to a new slot.</summary>
    [HttpPost("move-game")]
    public async Task<ActionResult> MoveGame(
        [FromBody] MoveGameRequest request, CancellationToken ct)
    {
        var (_, userId, error) = await ResolveContext();
        if (error != null) return error;

        await _service.MoveGameAsync(userId!, request, ct);
        return Ok();
    }

    /// <summary>GET /api/rescheduler/affected-count — Preview: how many games would be affected.</summary>
    [HttpGet("affected-count")]
    public async Task<ActionResult<AffectedGameCountResponse>> GetAffectedCount(
        [FromQuery] DateTime preFirstGame, [FromQuery] List<Guid> fieldIds, CancellationToken ct)
    {
        var (jobId, _, error) = await ResolveContext();
        if (error != null) return error;

        var result = await _service.GetAffectedGameCountAsync(jobId!.Value, preFirstGame, fieldIds, ct);
        return Ok(result);
    }

    /// <summary>POST /api/rescheduler/adjust-weather — Execute weather adjustment via stored procedure.</summary>
    [HttpPost("adjust-weather")]
    public async Task<ActionResult<AdjustWeatherResponse>> AdjustWeather(
        [FromBody] AdjustWeatherRequest request, CancellationToken ct)
    {
        var (jobId, _, error) = await ResolveContext();
        if (error != null) return error;

        var result = await _service.AdjustForWeatherAsync(jobId!.Value, request, ct);
        if (!result.Success)
            return BadRequest(result);

        return Ok(result);
    }

    /// <summary>GET /api/rescheduler/recipient-count — Preview: estimated email recipient count.</summary>
    [HttpGet("recipient-count")]
    public async Task<ActionResult<EmailRecipientCountResponse>> GetRecipientCount(
        [FromQuery] DateTime firstGame, [FromQuery] DateTime lastGame,
        [FromQuery] List<Guid> fieldIds, CancellationToken ct)
    {
        var (jobId, _, error) = await ResolveContext();
        if (error != null) return error;

        var result = await _service.GetEmailRecipientCountAsync(jobId!.Value, firstGame, lastGame, fieldIds, ct);
        return Ok(result);
    }

    /// <summary>POST /api/rescheduler/email-participants — Send bulk email to game participants.</summary>
    [HttpPost("email-participants")]
    public async Task<ActionResult<EmailParticipantsResponse>> EmailParticipants(
        [FromBody] EmailParticipantsRequest request, CancellationToken ct)
    {
        var (jobId, userId, error) = await ResolveContext();
        if (error != null) return error;

        var result = await _service.EmailParticipantsAsync(jobId!.Value, userId!, request, ct);
        return Ok(result);
    }
}
