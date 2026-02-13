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
    private readonly ILogger<TimeslotController> _logger;
    private readonly ITimeslotService _timeslotService;
    private readonly IJobLookupService _jobLookupService;

    public TimeslotController(
        ILogger<TimeslotController> logger,
        ITimeslotService timeslotService,
        IJobLookupService jobLookupService)
    {
        _logger = logger;
        _timeslotService = timeslotService;
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

        var result = await _timeslotService.AddFieldTimeslotAsync(jobId!.Value, userId!, request, ct);
        return Ok(result);
    }

    [HttpPut("field")]
    public async Task<ActionResult> EditFieldTimeslot(
        [FromBody] EditTimeslotFieldRequest request, CancellationToken ct)
    {
        var (_, userId, error) = await ResolveContext();
        if (error != null) return error;

        try
        {
            await _timeslotService.EditFieldTimeslotAsync(userId!, request, ct);
            return NoContent();
        }
        catch (KeyNotFoundException) { return NotFound(); }
    }

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

    [HttpPost("clone-by-field")]
    public async Task<ActionResult> CloneByField(
        [FromBody] CloneByFieldRequest request, CancellationToken ct)
    {
        var (jobId, userId, error) = await ResolveContext();
        if (error != null) return error;

        await _timeslotService.CloneByFieldAsync(jobId!.Value, userId!, request, ct);
        return NoContent();
    }

    [HttpPost("clone-by-division")]
    public async Task<ActionResult> CloneByDivision(
        [FromBody] CloneByDivisionRequest request, CancellationToken ct)
    {
        var (jobId, userId, error) = await ResolveContext();
        if (error != null) return error;

        await _timeslotService.CloneByDivisionAsync(jobId!.Value, userId!, request, ct);
        return NoContent();
    }

    [HttpPost("clone-by-dow")]
    public async Task<ActionResult> CloneByDow(
        [FromBody] CloneByDowRequest request, CancellationToken ct)
    {
        var (jobId, userId, error) = await ResolveContext();
        if (error != null) return error;

        await _timeslotService.CloneByDowAsync(jobId!.Value, userId!, request, ct);
        return NoContent();
    }

    [HttpPost("clone-field-dow")]
    public async Task<ActionResult<TimeslotFieldDto>> CloneFieldDow(
        [FromBody] CloneFieldDowRequest request, CancellationToken ct)
    {
        var (_, userId, error) = await ResolveContext();
        if (error != null) return error;

        try
        {
            var result = await _timeslotService.CloneFieldDowAsync(userId!, request, ct);
            return Ok(result);
        }
        catch (KeyNotFoundException) { return NotFound(); }
    }
}
