using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TSIC.Contracts.Dtos.Widgets;
using TSIC.Contracts.Services;

namespace TSIC.API.Controllers;

/// <summary>
/// SuperUser-only widget editor for managing widget definitions and default role assignments.
/// </summary>
[ApiController]
[Route("api/widget-editor")]
[Authorize(Policy = "SuperUserOnly")]
public class WidgetEditorController : ControllerBase
{
    private readonly IWidgetEditorService _editorService;

    public WidgetEditorController(IWidgetEditorService editorService)
    {
        _editorService = editorService;
    }

    // ── Reference data ──

    [HttpGet("job-types")]
    public async Task<ActionResult<List<JobTypeRefDto>>> GetJobTypes(CancellationToken ct)
    {
        var result = await _editorService.GetJobTypesAsync(ct);
        return Ok(result);
    }

    [HttpGet("roles")]
    public async Task<ActionResult<List<RoleRefDto>>> GetRoles(CancellationToken ct)
    {
        var result = await _editorService.GetRolesAsync(ct);
        return Ok(result);
    }

    [HttpGet("categories")]
    public async Task<ActionResult<List<WidgetCategoryRefDto>>> GetCategories(CancellationToken ct)
    {
        var result = await _editorService.GetCategoriesAsync(ct);
        return Ok(result);
    }

    // ── Widget definitions ──

    [HttpGet("widgets")]
    public async Task<ActionResult<List<WidgetDefinitionDto>>> GetWidgets(CancellationToken ct)
    {
        var result = await _editorService.GetWidgetDefinitionsAsync(ct);
        return Ok(result);
    }

    [HttpPost("widgets")]
    public async Task<ActionResult<WidgetDefinitionDto>> CreateWidget(
        [FromBody] CreateWidgetRequest request,
        CancellationToken ct)
    {
        try
        {
            var result = await _editorService.CreateWidgetAsync(request, ct);
            return CreatedAtAction(nameof(GetWidgets), null, result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPut("widgets/{widgetId:int}")]
    public async Task<ActionResult<WidgetDefinitionDto>> UpdateWidget(
        int widgetId,
        [FromBody] UpdateWidgetRequest request,
        CancellationToken ct)
    {
        try
        {
            var result = await _editorService.UpdateWidgetAsync(widgetId, request, ct);
            return Ok(result);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpDelete("widgets/{widgetId:int}")]
    public async Task<ActionResult> DeleteWidget(int widgetId, CancellationToken ct)
    {
        try
        {
            await _editorService.DeleteWidgetAsync(widgetId, ct);
            return NoContent();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    // ── Widget defaults matrix ──

    [HttpGet("defaults/{jobTypeId:int}")]
    public async Task<ActionResult<WidgetDefaultMatrixResponse>> GetDefaultsMatrix(
        int jobTypeId,
        CancellationToken ct)
    {
        var result = await _editorService.GetDefaultsMatrixAsync(jobTypeId, ct);
        return Ok(result);
    }

    [HttpPut("defaults/{jobTypeId:int}")]
    public async Task<ActionResult> SaveDefaultsMatrix(
        int jobTypeId,
        [FromBody] SaveWidgetDefaultsRequest request,
        CancellationToken ct)
    {
        if (request.JobTypeId != jobTypeId)
            return BadRequest(new { message = "JobTypeId in URL does not match request body." });

        try
        {
            await _editorService.SaveDefaultsMatrixAsync(request, ct);
            return NoContent();
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    // ── Widget-centric bulk assignment ──

    [HttpGet("widgets/{widgetId:int}/assignments")]
    public async Task<ActionResult<WidgetAssignmentsResponse>> GetWidgetAssignments(
        int widgetId,
        CancellationToken ct)
    {
        try
        {
            var result = await _editorService.GetWidgetAssignmentsAsync(widgetId, ct);
            return Ok(result);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    [HttpPut("widgets/{widgetId:int}/assignments")]
    public async Task<ActionResult> SaveWidgetAssignments(
        int widgetId,
        [FromBody] SaveWidgetAssignmentsRequest request,
        CancellationToken ct)
    {
        if (request.WidgetId != widgetId)
            return BadRequest(new { message = "WidgetId in URL does not match request body." });

        try
        {
            await _editorService.SaveWidgetAssignmentsAsync(request, ct);
            return NoContent();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
