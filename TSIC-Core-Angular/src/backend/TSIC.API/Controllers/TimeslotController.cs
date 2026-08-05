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
public class TimeslotController : ControllerBase
{
    private readonly ITimeslotService _timeslotService;
    private readonly IAutoBuildScheduleService _autoBuildService;
    private readonly IJobLookupService _jobLookupService;

    public TimeslotController(
        ITimeslotService timeslotService,
        IAutoBuildScheduleService autoBuildService,
        IJobLookupService jobLookupService)
    {
        _timeslotService = timeslotService;
        _autoBuildService = autoBuildService;
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

    // ── Readiness ──

    [HttpGet("readiness")]
    public async Task<ActionResult<CanvasReadinessResponse>> GetReadiness(CancellationToken ct)
    {
        var (jobId, _, error) = await ResolveContext();
        if (error != null) return error;

        var result = await _timeslotService.GetReadinessAsync(jobId!.Value, ct);
        return Ok(result);
    }

    // ── Auto-Seed from Source ──

    [HttpPost("auto-seed-from-source")]
    public async Task<ActionResult<AutoSeedFieldTimeslotsResult>> AutoSeedFromSource(
        [FromBody] AutoSeedFromSourceRequest request, CancellationToken ct)
    {
        var (jobId, userId, error) = await ResolveContext();
        if (error != null) return error;

        var result = await _autoBuildService.AutoSeedFieldTimeslotsFromSourceAsync(
            jobId!.Value, userId!, request.SourceJobId, ct);
        return Ok(result);
    }

    // ── Configuration ──

    [HttpGet("{agegroupId:guid}")]
    public async Task<ActionResult<TimeslotConfigurationResponse>> GetConfiguration(
        Guid agegroupId, CancellationToken ct)
    {
        var (jobId, _, error) = await ResolveContext();
        if (error != null) return error;

        var result = await _timeslotService.GetConfigurationAsync(jobId!.Value, agegroupId, ct);
        return Ok(result);
    }

    [HttpGet("{agegroupId:guid}/capacity")]
    public async Task<ActionResult<List<CapacityPreviewDto>>> GetCapacityPreview(
        Guid agegroupId, CancellationToken ct)
    {
        var (jobId, _, error) = await ResolveContext();
        if (error != null) return error;

        var result = await _timeslotService.GetCapacityPreviewAsync(jobId!.Value, agegroupId, ct);
        return Ok(result);
    }

    // ── Dates CRUD ──

    [HttpPost("date")]
    public async Task<ActionResult<TimeslotDateDto>> AddDate(
        [FromBody] AddTimeslotDateRequest request, CancellationToken ct)
    {
        var (jobId, userId, error) = await ResolveContext();
        if (error != null) return error;

        var result = await _timeslotService.AddDateAsync(jobId!.Value, userId!, request, ct);
        return Ok(result);
    }

    [HttpPut("date")]
    public async Task<ActionResult> EditDate(
        [FromBody] EditTimeslotDateRequest request, CancellationToken ct)
    {
        var (_, userId, error) = await ResolveContext();
        if (error != null) return error;

        try
        {
            await _timeslotService.EditDateAsync(userId!, request, ct);
            return NoContent();
        }
        catch (KeyNotFoundException) { return NotFound(); }
    }

    [HttpDelete("date/{ai:int}")]
    public async Task<ActionResult> DeleteDate(int ai, CancellationToken ct)
    {
        var (_, _, error) = await ResolveContext();
        if (error != null) return error;

        try
        {
            await _timeslotService.DeleteDateAsync(ai, ct);
            return NoContent();
        }
        catch (KeyNotFoundException) { return NotFound(); }
    }

    [HttpPost("date/clone")]
    public async Task<ActionResult<TimeslotDateDto>> CloneDateRecord(
        [FromBody] CloneDateRecordRequest request, CancellationToken ct)
    {
        var (_, userId, error) = await ResolveContext();
        if (error != null) return error;

        try
        {
            var result = await _timeslotService.CloneDateRecordAsync(userId!, request, ct);
            return Ok(result);
        }
        catch (KeyNotFoundException) { return NotFound(); }
    }

    [HttpDelete("dates/{agegroupId:guid}")]
    public async Task<ActionResult> DeleteAllDates(Guid agegroupId, CancellationToken ct)
    {
        var (jobId, _, error) = await ResolveContext();
        if (error != null) return error;

        await _timeslotService.DeleteAllDatesAsync(jobId!.Value, agegroupId, ct);
        return NoContent();
    }

    // ── Field timeslots CRUD ──

    [HttpPost("field")]
    public async Task<ActionResult<List<TimeslotFieldDto>>> AddFieldTimeslot(
        [FromBody] AddTimeslotFieldRequest request, CancellationToken ct)
    {
        var (jobId, userId, error) = await ResolveContext();
        if (error != null) return error;

        try
        {
            var result = await _timeslotService.AddFieldTimeslotAsync(jobId!.Value, userId!, request, ct);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    // PARKED 2026-08-04 — per-row edit. Assign Timeslots dropped its row actions: the setup
    // dialog authors every game day at once, so nothing in the UI edits a single row any more.
    // The service method and DTO are left intact; uncomment to bring the route back.
    //
    // [HttpPut("field")]
    // public async Task<ActionResult> EditFieldTimeslot(
    //     [FromBody] EditTimeslotFieldRequest request, CancellationToken ct)
    // {
    //     var (_, userId, error) = await ResolveContext();
    //     if (error != null) return error;
    //
    //     try
    //     {
    //         await _timeslotService.EditFieldTimeslotAsync(userId!, request, ct);
    //         return NoContent();
    //     }
    //     catch (KeyNotFoundException) { return NotFound(); }
    // }

    [HttpDelete("field/{ai:int}")]
    public async Task<ActionResult> DeleteFieldTimeslot(int ai, CancellationToken ct)
    {
        var (_, _, error) = await ResolveContext();
        if (error != null) return error;

        try
        {
            await _timeslotService.DeleteFieldTimeslotAsync(ai, ct);
            return NoContent();
        }
        catch (KeyNotFoundException) { return NotFound(); }
    }

    [HttpDelete("fields/{agegroupId:guid}")]
    public async Task<ActionResult> DeleteAllFieldTimeslots(Guid agegroupId, CancellationToken ct)
    {
        var (jobId, _, error) = await ResolveContext();
        if (error != null) return error;

        await _timeslotService.DeleteAllFieldTimeslotsAsync(jobId!.Value, agegroupId, ct);
        return NoContent();
    }

    /// <summary>
    /// Set up an agegroup's timeslots, one game day at a time. Replaces each day named in the
    /// request and leaves days it does not name alone.
    /// </summary>
    [HttpPut("setup")]
    public async Task<ActionResult<SaveTimeslotSetupResponse>> SaveTimeslotSetup(
        [FromBody] SaveTimeslotSetupRequest request, CancellationToken ct)
    {
        var (jobId, userId, error) = await ResolveContext();
        if (error != null) return error;

        var result = await _timeslotService.SaveTimeslotSetupAsync(jobId!.Value, userId!, request, ct);
        return Ok(result);
    }

    // ── Cloning operations ──

    [HttpPost("clone-dates")]
    public async Task<ActionResult> CloneDates(
        [FromBody] CloneDatesRequest request, CancellationToken ct)
    {
        var (jobId, userId, error) = await ResolveContext();
        if (error != null) return error;

        await _timeslotService.CloneDatesAsync(jobId!.Value, userId!, request, ct);
        return NoContent();
    }

    [HttpPost("clone-fields")]
    public async Task<ActionResult> CloneFields(
        [FromBody] CloneFieldsRequest request, CancellationToken ct)
    {
        var (jobId, userId, error) = await ResolveContext();
        if (error != null) return error;

        await _timeslotService.CloneFieldsAsync(jobId!.Value, userId!, request, ct);
        return NoContent();
    }

    // PARKED 2026-08-04 — the three intra-agegroup copy grains, all reached only from the copy
    // row that Assign Timeslots removed. Ticking a field on a second day in the setup dialog is
    // now the copy. Ranked by how likely each is to be wanted back:
    //
    //   clone-by-dow      legacy has this (Timeslots/Index.cshtml) and it is the plausible
    //                     bulk tool if per-day copying is ever asked for again.
    //   clone-by-field    legacy's #aCloneFieldsByField handler exists but no element ever
    //                     renders it — unreachable there, so it was never a real feature.
    //   clone-by-division does not exist in legacy at all; it was invented from the API surface.
    //
    // All three REPLACE the target (fixed 2026-08-04 — they used to append, so copying twice
    // doubled the target). Keep that in mind if any is revived.
    //
    // [HttpPost("clone-by-field")]
    // public async Task<ActionResult> CloneByField(
    //     [FromBody] CloneByFieldRequest request, CancellationToken ct)
    // {
    //     var (jobId, userId, error) = await ResolveContext();
    //     if (error != null) return error;
    //
    //     await _timeslotService.CloneByFieldAsync(jobId!.Value, userId!, request, ct);
    //     return NoContent();
    // }
    //
    // [HttpPost("clone-by-division")]
    // public async Task<ActionResult> CloneByDivision(
    //     [FromBody] CloneByDivisionRequest request, CancellationToken ct)
    // {
    //     var (jobId, userId, error) = await ResolveContext();
    //     if (error != null) return error;
    //
    //     await _timeslotService.CloneByDivisionAsync(jobId!.Value, userId!, request, ct);
    //     return NoContent();
    // }
    //
    // [HttpPost("clone-by-dow")]
    // public async Task<ActionResult> CloneByDow(
    //     [FromBody] CloneByDowRequest request, CancellationToken ct)
    // {
    //     var (jobId, userId, error) = await ResolveContext();
    //     if (error != null) return error;
    //
    //     await _timeslotService.CloneByDowAsync(jobId!.Value, userId!, request, ct);
    //     return NoContent();
    // }

    // ── Cascade date operations ──

    [HttpPut("date/cascade")]
    public async Task<ActionResult<CascadeDateChangeResponse>> CascadeEditDate(
        [FromBody] CascadeDateChangeRequest request, CancellationToken ct)
    {
        var (jobId, userId, error) = await ResolveContext();
        if (error != null) return error;

        try
        {
            var result = await _timeslotService.CascadeEditDateAsync(jobId!.Value, userId!, request, ct);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    [HttpPost("date/cascade-delete")]
    public async Task<ActionResult<CascadeDateDeleteResponse>> CascadeDeleteDate(
        [FromBody] CascadeDateDeleteRequest request, CancellationToken ct)
    {
        var (jobId, _, error) = await ResolveContext();
        if (error != null) return error;

        var result = await _timeslotService.CascadeDeleteDateAsync(jobId!.Value, request, ct);
        return Ok(result);
    }

    // ── Field config update ──

    // PARKED 2026-08-04 — bulk GSI/start/max update. Already had no frontend consumer before
    // the Assign Timeslots rework; the setup dialog covers the same ground per game day. Note
    // it deliberately does NOT touch TimeslotsLeagueSeasonDates, so R/day and wave assignments
    // survive it — the setup path does not have that property.
    //
    // [HttpPut("field-config")]
    // public async Task<ActionResult<UpdateFieldConfigResponse>> UpdateFieldConfig(
    //     [FromBody] UpdateFieldConfigRequest request, CancellationToken ct)
    // {
    //     var (jobId, userId, error) = await ResolveContext();
    //     if (error != null) return error;
    //
    //     var result = await _timeslotService.UpdateFieldConfigAsync(jobId!.Value, userId!, request, ct);
    //     return Ok(result);
    // }

    // ── Bulk operations ──

    [HttpPost("bulk-assign")]
    public async Task<ActionResult<BulkDateAssignResponse>> BulkAssignDate(
        [FromBody] BulkDateAssignRequest request, CancellationToken ct)
    {
        var (jobId, userId, error) = await ResolveContext();
        if (error != null) return error;

        var result = await _timeslotService.BulkAssignDateAsync(jobId!.Value, userId!, request, ct);
        return Ok(result);
    }

    // PARKED 2026-08-04 — "clone this row to the next day", the +D row action Assign Timeslots
    // removed. Legacy has the equivalent, so this is the second-likeliest of the parked set to
    // come back. It appends one row per call and never replaces.
    //
    // [HttpPost("clone-field-dow")]
    // public async Task<ActionResult<TimeslotFieldDto>> CloneFieldDow(
    //     [FromBody] CloneFieldDowRequest request, CancellationToken ct)
    // {
    //     var (_, userId, error) = await ResolveContext();
    //     if (error != null) return error;
    //
    //     try
    //     {
    //         var result = await _timeslotService.CloneFieldDowAsync(userId!, request, ct);
    //         return Ok(result);
    //     }
    //     catch (KeyNotFoundException) { return NotFound(); }
    // }

    // ── Field assignments ──

    [HttpPut("field-assignments")]
    public async Task<ActionResult<SaveFieldAssignmentsResponse>> SaveFieldAssignments(
        [FromBody] SaveFieldAssignmentsRequest request, CancellationToken ct)
    {
        var (jobId, userId, error) = await ResolveContext();
        if (error != null) return error;

        var result = await _timeslotService.SaveFieldAssignmentsAsync(jobId!.Value, userId!, request, ct);
        return Ok(result);
    }
}
