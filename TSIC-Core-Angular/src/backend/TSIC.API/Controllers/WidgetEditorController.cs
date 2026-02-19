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
    private readonly IWebHostEnvironment _env;

    public WidgetEditorController(IWidgetEditorService editorService, IWebHostEnvironment env)
    {
        _editorService = editorService;
        _env = env;
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

    // ── Per-job overrides ──

    [HttpGet("jobs-by-type/{jobTypeId:int}")]
    public async Task<ActionResult<List<JobRefDto>>> GetJobsByJobType(
        int jobTypeId,
        CancellationToken ct)
    {
        var result = await _editorService.GetJobsByJobTypeAsync(jobTypeId, ct);
        return Ok(result);
    }

    [HttpGet("job-overrides/{jobId:guid}")]
    public async Task<ActionResult<JobOverridesResponse>> GetJobOverrides(
        Guid jobId,
        CancellationToken ct)
    {
        try
        {
            var result = await _editorService.GetJobOverridesAsync(jobId, ct);
            return Ok(result);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    [HttpPut("job-overrides/{jobId:guid}")]
    public async Task<ActionResult> SaveJobOverrides(
        Guid jobId,
        [FromBody] SaveJobOverridesRequest request,
        CancellationToken ct)
    {
        if (request.JobId != jobId)
            return BadRequest(new { message = "JobId in URL does not match request body." });

        try
        {
            await _editorService.SaveJobOverridesAsync(request, ct);
            return NoContent();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    // ── Export SQL ──

    [HttpGet("export-sql")]
    public async Task<ActionResult<object>> ExportSql(CancellationToken ct)
    {
        var sql = await _editorService.ExportWidgetSqlAsync(ct);
        return Ok(new { sql });
    }

    // ── Seed script sync (dev only) ──

    [HttpPost("sync-seed-script")]
    public async Task<ActionResult<SeedScriptSyncResult>> SyncSeedScript(CancellationToken ct)
    {
        if (!_env.IsDevelopment())
            return BadRequest(new { message = "Seed script sync is only available in development." });

        var scriptPath = ResolveSeedScriptPath();
        if (scriptPath is null)
            return BadRequest(new { message = "Could not resolve scripts/ directory. Is the API running from the repository?" });

        try
        {
            var result = await _editorService.GenerateSeedScriptAsync(scriptPath, ct);
            return Ok(result);
        }
        catch (IOException ex)
        {
            return StatusCode(500, new { message = $"Failed to write seed script: {ex.Message}" });
        }
    }

    /// <summary>
    /// Resolve the path to scripts/seed-widget-dashboard.sql by walking up from ContentRootPath.
    /// ContentRootPath = .../TSIC-Core-Angular/src/backend/TSIC.API
    /// Repo root        = .../TSIC-Core-Angular  (3 levels up from src/backend/TSIC.API)
    /// Scripts dir      = .../TSIC-Core-Angular/scripts/
    /// </summary>
    private string? ResolveSeedScriptPath()
    {
        var dir = _env.ContentRootPath;
        for (var i = 0; i < 3; i++)
        {
            dir = Path.GetDirectoryName(dir);
            if (dir is null) return null;
        }

        var scriptsDir = Path.Combine(dir, "scripts");
        return Directory.Exists(scriptsDir)
            ? Path.Combine(scriptsDir, "seed-widget-dashboard.sql")
            : null;
    }
}
