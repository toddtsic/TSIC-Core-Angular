using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using TSIC.API.Extensions;
using TSIC.API.Services.Shared.Jobs;
using TSIC.Contracts.Dtos.Scheduling;
using TSIC.Contracts.Services;

namespace TSIC.API.Controllers;

[ApiController]
[Route("api/field")]
[Authorize(Policy = "AdminOnly")]
public class FieldController : ControllerBase
{
    private readonly ILogger<FieldController> _logger;
    private readonly IFieldManagementService _fieldService;
    private readonly IJobLookupService _jobLookupService;

    public FieldController(
        ILogger<FieldController> logger,
        IFieldManagementService fieldService,
        IJobLookupService jobLookupService)
    {
        _logger = logger;
        _fieldService = fieldService;
        _jobLookupService = jobLookupService;
    }

    private async Task<(Guid? jobId, string? userId, string? role, ActionResult? error)> ResolveContext()
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null)
            return (null, null, null, BadRequest(new { message = "Scheduling context required" }));

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
            return (null, null, null, Unauthorized());

        var role = User.FindFirstValue(ClaimTypes.Role) ?? "";

        return (jobId, userId, role, null);
    }

    [HttpGet]
    public async Task<ActionResult<FieldManagementResponse>> GetFieldManagementData(CancellationToken ct)
    {
        var (jobId, _, role, error) = await ResolveContext();
        if (error != null) return error;

        var result = await _fieldService.GetFieldManagementDataAsync(jobId!.Value, role!, ct);
        return Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<FieldDto>> CreateField(
        [FromBody] CreateFieldRequest request, CancellationToken ct)
    {
        var (jobId, userId, _, error) = await ResolveContext();
        if (error != null) return error;

        var field = await _fieldService.CreateFieldAsync(jobId!.Value, userId!, request, ct);
        return Ok(field);
    }

    [HttpPut]
    public async Task<ActionResult> UpdateField(
        [FromBody] UpdateFieldRequest request, CancellationToken ct)
    {
        var (_, userId, _, error) = await ResolveContext();
        if (error != null) return error;

        try
        {
            await _fieldService.UpdateFieldAsync(userId!, request, ct);
            return NoContent();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    [HttpDelete("{fieldId:guid}")]
    public async Task<ActionResult> DeleteField(Guid fieldId, CancellationToken ct)
    {
        var (_, _, _, error) = await ResolveContext();
        if (error != null) return error;

        var deleted = await _fieldService.DeleteFieldAsync(fieldId, ct);
        if (!deleted)
            return Conflict(new { message = "Field is referenced by league-seasons, schedules, or timeslots and cannot be deleted." });

        return NoContent();
    }

    [HttpPost("assign")]
    public async Task<ActionResult> AssignFields(
        [FromBody] AssignFieldsRequest request, CancellationToken ct)
    {
        var (jobId, userId, _, error) = await ResolveContext();
        if (error != null) return error;

        await _fieldService.AssignFieldsAsync(jobId!.Value, userId!, request, ct);
        return NoContent();
    }

    [HttpPost("remove")]
    public async Task<ActionResult> RemoveFields(
        [FromBody] RemoveFieldsRequest request, CancellationToken ct)
    {
        var (jobId, _, _, error) = await ResolveContext();
        if (error != null) return error;

        await _fieldService.RemoveFieldsAsync(jobId!.Value, request, ct);
        return NoContent();
    }
}
