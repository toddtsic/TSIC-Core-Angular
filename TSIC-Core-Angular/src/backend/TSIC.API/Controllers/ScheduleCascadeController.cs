using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TSIC.API.Extensions;
using TSIC.API.Services.Shared.Jobs;
using TSIC.Contracts.Dtos.Scheduling;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Entities;

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
    private readonly IScheduleCascadeRepository _cascadeRepo;
    private readonly IJobLookupService _jobLookupService;
    private readonly ILogger<ScheduleCascadeController> _logger;

    public ScheduleCascadeController(
        IScheduleCascadeService service,
        IScheduleCascadeRepository cascadeRepo,
        IJobLookupService jobLookupService,
        ILogger<ScheduleCascadeController> logger)
    {
        _service = service;
        _cascadeRepo = cascadeRepo;
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

        if (request.BetweenRoundRows > 4)
            return BadRequest(new { message = "BetweenRoundRows must be 0–4" });

        if (request.GameGuarantee < 1)
            return BadRequest(new { message = "GameGuarantee must be at least 1" });

        await _service.SaveEventDefaultsAsync(
            jobId!.Value, request.GamePlacement, request.BetweenRoundRows,
            request.GameGuarantee, userId!, ct);

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
            request.GameGuarantee, wavesByDate, userId!, ct);

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
            request.GameGuarantee, wavesByDate, userId!, ct);

        return Ok(new { message = "Division override saved" });
    }

    /// <summary>
    /// PUT /api/schedule-cascade/waves — Batch-save all wave assignments for a job.
    /// Replaces agegroup + division wave assignments atomically.
    /// </summary>
    [HttpPut("waves")]
    public async Task<ActionResult> SaveBatchWaves(
        [FromBody] SaveBatchWavesRequest request, CancellationToken ct)
    {
        var (jobId, userId, error) = await ResolveContext();
        if (error != null) return error;

        await _service.SaveBatchWavesAsync(jobId!.Value, request, userId!, ct);
        return Ok(new { message = "Wave assignments saved" });
    }

    /// <summary>
    /// PUT /api/schedule-cascade/build-rules — Batch-save event defaults + all agegroup/division overrides.
    /// Single request, single transaction — no concurrent DbContext issues.
    /// </summary>
    [HttpPut("build-rules")]
    public async Task<ActionResult> SaveBatchBuildRules(
        [FromBody] SaveBatchBuildRulesRequest request, CancellationToken ct)
    {
        var (jobId, userId, error) = await ResolveContext();
        if (error != null) return error;

        var ev = request.EventDefaults;
        if (ev.GamePlacement is not ("H" or "V"))
            return BadRequest(new { message = "GamePlacement must be 'H' or 'V'" });
        if (ev.BetweenRoundRows > 4)
            return BadRequest(new { message = "BetweenRoundRows must be 0–4" });
        if (ev.GameGuarantee < 1)
            return BadRequest(new { message = "GameGuarantee must be at least 1" });

        // 1. Event defaults
        await _service.SaveEventDefaultsAsync(
            jobId!.Value, ev.GamePlacement, ev.BetweenRoundRows, ev.GameGuarantee, userId!, ct);

        // 2. Agegroup overrides (sequential — shared DbContext)
        if (request.AgegroupOverrides != null)
        {
            foreach (var (agIdStr, ovr) in request.AgegroupOverrides)
            {
                if (!Guid.TryParse(agIdStr, out var agId)) continue;
                var waves = ParseWavesByDate(ovr.WavesByDate);
                await _service.SaveAgegroupOverrideAsync(
                    agId, ovr.GamePlacement, ovr.BetweenRoundRows, ovr.GameGuarantee, waves, userId!, ct);
            }
        }

        // 3. Division overrides (sequential — shared DbContext)
        if (request.DivisionOverrides != null)
        {
            foreach (var (divIdStr, ovr) in request.DivisionOverrides)
            {
                if (!Guid.TryParse(divIdStr, out var divId)) continue;
                var waves = ParseWavesByDate(ovr.WavesByDate);
                await _service.SaveDivisionOverrideAsync(
                    divId, ovr.GamePlacement, ovr.BetweenRoundRows, ovr.GameGuarantee, waves, userId!, ct);
            }
        }

        return Ok(new { message = "Build rules saved" });
    }

    /// <summary>
    /// POST /api/schedule-cascade/seed-waves — Bulk-seed division wave assignments
    /// from projected config. Only seeds divisions that don't already have waves.
    /// </summary>
    [HttpPost("seed-waves")]
    public async Task<ActionResult<ScheduleCascadeSnapshot>> SeedWaves(
        [FromBody] SeedWavesRequest request, CancellationToken ct)
    {
        var (jobId, userId, error) = await ResolveContext();
        if (error != null) return error;

        // Parse string keys to Guids
        var divisionWaves = new Dictionary<Guid, int>();
        foreach (var (key, wave) in request.DivisionWaves)
        {
            if (Guid.TryParse(key, out var divId))
                divisionWaves[divId] = wave;
        }

        var agegroupDates = new Dictionary<Guid, List<DateTime>>();
        foreach (var (key, dateStrings) in request.AgegroupDates)
        {
            if (!Guid.TryParse(key, out var agId)) continue;
            var dates = dateStrings
                .Select(s => DateTime.TryParse(s, out var d) ? d.Date : (DateTime?)null)
                .Where(d => d.HasValue)
                .Select(d => d!.Value)
                .ToList();
            if (dates.Count > 0)
                agegroupDates[agId] = dates;
        }

        await _service.SeedDivisionWavesAsync(
            jobId!.Value, divisionWaves, agegroupDates, userId!, ct);

        // Return updated snapshot
        var snapshot = await _service.ResolveAsync(jobId!.Value, ct);
        return Ok(snapshot);
    }

    // ══════════════════════════════════════════════════════════
    // Division Processing Order
    // ══════════════════════════════════════════════════════════

    /// <summary>
    /// GET /api/schedule-cascade/processing-order — Get persisted division build order.
    /// Returns empty list when no order has been saved.
    /// </summary>
    [HttpGet("processing-order")]
    public async Task<ActionResult<List<ProcessingOrderEntryDto>>> GetProcessingOrder(
        CancellationToken ct)
    {
        var (jobId, _, error) = await ResolveContext();
        if (error != null) return error;

        var entries = await _cascadeRepo.GetProcessingOrderAsync(jobId!.Value, ct);
        var dtos = entries
            .Select(e => new ProcessingOrderEntryDto
            {
                DivisionId = e.DivisionId,
                SortOrder = e.SortOrder
            })
            .ToList();

        return Ok(dtos);
    }

    /// <summary>
    /// PUT /api/schedule-cascade/processing-order — Save division build order.
    /// Replaces all existing entries for the job.
    /// </summary>
    [HttpPut("processing-order")]
    public async Task<ActionResult> SaveProcessingOrder(
        [FromBody] SaveProcessingOrderRequest request, CancellationToken ct)
    {
        var (jobId, userId, error) = await ResolveContext();
        if (error != null) return error;

        var entities = request.Entries
            .Select(e => new DivisionProcessingOrder
            {
                DivisionId = e.DivisionId,
                SortOrder = e.SortOrder,
                LebUserId = userId
            })
            .ToList();

        await _cascadeRepo.UpsertProcessingOrderAsync(jobId!.Value, entities, ct);
        await _cascadeRepo.SaveChangesAsync(ct);

        return Ok(new { message = "Processing order saved" });
    }

    /// <summary>
    /// DELETE /api/schedule-cascade/processing-order — Clear all processing order entries.
    /// Engine will fall back to alphabetical ordering.
    /// </summary>
    [HttpDelete("processing-order")]
    public async Task<ActionResult> DeleteProcessingOrder(CancellationToken ct)
    {
        var (jobId, _, error) = await ResolveContext();
        if (error != null) return error;

        await _cascadeRepo.DeleteProcessingOrderAsync(jobId!.Value, ct);
        await _cascadeRepo.SaveChangesAsync(ct);

        return Ok(new { message = "Processing order cleared" });
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
