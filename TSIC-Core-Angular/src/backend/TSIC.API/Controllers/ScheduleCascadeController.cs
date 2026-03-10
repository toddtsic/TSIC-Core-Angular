using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TSIC.API.Extensions;
using TSIC.API.Services.Shared.Jobs;
using TSIC.Contracts.Dtos.Scheduling;
using TSIC.Contracts.Services;

namespace TSIC.API.Controllers;

/// <summary>
/// Scheduling Cascade: 3-level strategy + wave configuration.
/// Event → Agegroup → Division, each level can override the parent.
/// </summary>
[ApiController]
[Route("api/schedule-cascade")]
[Authorize(Policy = "AdminOnly")]
public class ScheduleCascadeController : ControllerBase
{
    private readonly IScheduleCascadeService _service;
    private readonly IJobLookupService _jobLookupService;
    private readonly ILogger<ScheduleCascadeController> _logger;

    public ScheduleCascadeController(
        IScheduleCascadeService service,
        IJobLookupService jobLookupService,
        ILogger<ScheduleCascadeController> logger)
    {
        _service = service;
        _jobLookupService = jobLookupService;
        _logger = logger;
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

    /// <summary>
    /// GET /api/schedule-cascade — Full resolved cascade snapshot.
    /// Returns event defaults + per-agegroup + per-division overrides + per-date waves.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<ScheduleCascadeSnapshot>> GetCascade(CancellationToken ct)
    {
        var (jobId, _, error) = await ResolveContext();
        if (error != null) return error;

        var snapshot = await _service.ResolveAsync(jobId!.Value, ct);
        return Ok(snapshot);
    }

    /// <summary>
    /// PUT /api/schedule-cascade/event-defaults — Save event-level defaults.
    /// GamePlacement and BetweenRoundRows are non-nullable at this level.
    /// </summary>
    [HttpPut("event-defaults")]
    public async Task<ActionResult> SaveEventDefaults(
        [FromBody] SaveEventDefaultsRequest request, CancellationToken ct)
    {
        var (jobId, userId, error) = await ResolveContext();
        if (error != null) return error;

        if (request.GamePlacement is not ("H" or "V"))
            return BadRequest(new { message = "GamePlacement must be 'H' or 'V'" });

        if (request.BetweenRoundRows > 2)
            return BadRequest(new { message = "BetweenRoundRows must be 0, 1, or 2" });

        await _service.SaveEventDefaultsAsync(
            jobId!.Value, request.GamePlacement, request.BetweenRoundRows, userId!, ct);

        return Ok(new { message = "Event defaults saved" });
    }

    /// <summary>
    /// PUT /api/schedule-cascade/agegroup/{agegroupId} — Save agegroup-level overrides + waves.
    /// Null values = inherit from event. Empty wavesByDate = clear all waves.
    /// </summary>
    [HttpPut("agegroup/{agegroupId:guid}")]
    public async Task<ActionResult> SaveAgegroupOverride(
        Guid agegroupId, [FromBody] SaveCascadeLevelRequest request, CancellationToken ct)
    {
        var (_, userId, error) = await ResolveContext();
        if (error != null) return error;

        if (request.GamePlacement != null && request.GamePlacement is not ("H" or "V"))
            return BadRequest(new { message = "GamePlacement must be 'H', 'V', or null" });

        if (request.BetweenRoundRows != null && request.BetweenRoundRows > 2)
            return BadRequest(new { message = "BetweenRoundRows must be 0, 1, 2, or null" });

        var wavesByDate = ParseWavesByDate(request.WavesByDate);

        await _service.SaveAgegroupOverrideAsync(
            agegroupId, request.GamePlacement, request.BetweenRoundRows,
            wavesByDate, userId!, ct);

        return Ok(new { message = "Agegroup override saved" });
    }

    /// <summary>
    /// PUT /api/schedule-cascade/division/{divisionId} — Save division-level overrides + waves.
    /// Null values = inherit from agegroup. Empty wavesByDate = clear all waves.
    /// </summary>
    [HttpPut("division/{divisionId:guid}")]
    public async Task<ActionResult> SaveDivisionOverride(
        Guid divisionId, [FromBody] SaveCascadeLevelRequest request, CancellationToken ct)
    {
        var (_, userId, error) = await ResolveContext();
        if (error != null) return error;

        if (request.GamePlacement != null && request.GamePlacement is not ("H" or "V"))
            return BadRequest(new { message = "GamePlacement must be 'H', 'V', or null" });

        if (request.BetweenRoundRows != null && request.BetweenRoundRows > 2)
            return BadRequest(new { message = "BetweenRoundRows must be 0, 1, 2, or null" });

        var wavesByDate = ParseWavesByDate(request.WavesByDate);

        await _service.SaveDivisionOverrideAsync(
            divisionId, request.GamePlacement, request.BetweenRoundRows,
            wavesByDate, userId!, ct);

        return Ok(new { message = "Division override saved" });
    }

    /// <summary>
    /// Parse ISO date strings to DateTime for wave assignments.
    /// </summary>
    private static Dictionary<DateTime, byte>? ParseWavesByDate(Dictionary<string, byte>? input)
    {
        if (input == null) return null;

        var result = new Dictionary<DateTime, byte>();
        foreach (var (dateStr, wave) in input)
        {
            if (DateTime.TryParse(dateStr, out var date))
                result[date.Date] = wave;
        }
        return result;
    }
}
