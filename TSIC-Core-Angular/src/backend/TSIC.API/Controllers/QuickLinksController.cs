using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TSIC.API.Services.Shared.Jobs;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Dtos.Widgets;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;

namespace TSIC.API.Controllers;

/// <summary>
/// QuickLinks — a public resolved-links read for the landing hero, plus
/// SuperUser editor endpoints (job-type → job picker, editor model, batch save).
/// </summary>
[ApiController]
[Route("api/quicklinks")]
[Authorize]
public class QuickLinksController : ControllerBase
{
    private readonly IQuickLinksService _service;
    private readonly IQuickLinksRepository _repo;
    private readonly IJobLookupService _jobLookupService;

    public QuickLinksController(
        IQuickLinksService service,
        IQuickLinksRepository repo,
        IJobLookupService jobLookupService)
    {
        _service = service;
        _repo = repo;
        _jobLookupService = jobLookupService;
    }

    // ─── Public read (landing hero) ─────────────────────────────────

    /// <summary>Resolve a job's quicklinks by path (anonymous; mirrors /api/nav/merged).</summary>
    [AllowAnonymous]
    [HttpGet("resolved")]
    public async Task<ActionResult<List<QuickLinkResolvedDto>>> GetResolved([FromQuery] string jobPath, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(jobPath))
            return BadRequest("jobPath is required");

        var jobId = await _jobLookupService.GetJobIdByPathAsync(jobPath);
        if (jobId == null)
            return NotFound(new { message = $"Job not found: {jobPath}" });

        return Ok(await _service.ResolveForJobAsync(jobId.Value, ct));
    }

    // ─── SuperUser editor ───────────────────────────────────────────

    /// <summary>Job types for the picker.</summary>
    [HttpGet("job-types")]
    [Authorize(Policy = "SuperUserOnly")]
    public async Task<ActionResult<List<JobTypeRefDto>>> GetJobTypes(CancellationToken ct)
        => Ok(await _repo.GetJobTypesAsync(ct));

    /// <summary>Jobs in a job type for the picker.</summary>
    [HttpGet("jobs-by-type/{jobTypeId:int}")]
    [Authorize(Policy = "SuperUserOnly")]
    public async Task<ActionResult<List<JobRefDto>>> GetJobsByJobType(int jobTypeId, CancellationToken ct)
        => Ok(await _repo.GetJobsByJobTypeAsync(jobTypeId, ct));

    /// <summary>Editor model for a chosen job.</summary>
    [HttpGet("editor/{jobId:guid}")]
    [Authorize(Policy = "SuperUserOnly")]
    public async Task<ActionResult<QuickLinkEditorModelDto>> GetEditorModel(Guid jobId, CancellationToken ct)
    {
        var model = await _service.GetEditorModelAsync(jobId, ct);
        if (model == null)
            return NotFound(new { message = $"Job not found: {jobId}" });

        return Ok(model);
    }

    /// <summary>Batch save the job's quicklink overrides.</summary>
    [HttpPut("editor/{jobId:guid}")]
    [Authorize(Policy = "SuperUserOnly")]
    public async Task<IActionResult> SaveEditor(Guid jobId, [FromBody] SaveQuickLinksRequest request, CancellationToken ct)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
            return Unauthorized("User ID not found in token");

        await _service.SaveAsync(jobId, request, userId, ct);
        return NoContent();
    }
}
