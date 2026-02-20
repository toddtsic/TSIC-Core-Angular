using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using TSIC.API.Extensions;
using TSIC.API.Services.Shared.Jobs;
using TSIC.Contracts.Dtos.Scheduling;
using TSIC.Contracts.Services;

namespace TSIC.API.Controllers;

/// <summary>
/// Auto-Build Entire Schedule: pattern-replay from prior year's proven schedule.
/// </summary>
[ApiController]
[Route("api/auto-build")]
[Authorize(Policy = "AdminOnly")]
public class AutoBuildController : ControllerBase
{
    private readonly ILogger<AutoBuildController> _logger;
    private readonly IAutoBuildScheduleService _service;
    private readonly IJobLookupService _jobLookupService;

    public AutoBuildController(
        ILogger<AutoBuildController> logger,
        IAutoBuildScheduleService service,
        IJobLookupService jobLookupService)
    {
        _logger = logger;
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

    /// <summary>
    /// GET /api/auto-build/source-jobs — Get candidate prior-year jobs for pattern extraction.
    /// </summary>
    [HttpGet("source-jobs")]
    public async Task<ActionResult<List<AutoBuildSourceJobDto>>> GetSourceJobs(CancellationToken ct)
    {
        var (jobId, _, error) = await ResolveContext();
        if (error != null) return error;

        var result = await _service.GetSourceJobsAsync(jobId!.Value, ct);
        return Ok(result);
    }

    /// <summary>
    /// POST /api/auto-build/analyze — Analyze source pattern, match divisions, compute feasibility.
    /// </summary>
    [HttpPost("analyze")]
    public async Task<ActionResult<AutoBuildAnalysisResponse>> Analyze(
        [FromBody] AutoBuildAnalyzeRequest request, CancellationToken ct)
    {
        var (jobId, _, error) = await ResolveContext();
        if (error != null) return error;

        var result = await _service.AnalyzeAsync(jobId!.Value, request.SourceJobId, ct);
        return Ok(result);
    }

    /// <summary>
    /// POST /api/auto-build/execute — Execute the auto-build with user-provided resolutions.
    /// </summary>
    [HttpPost("execute")]
    public async Task<ActionResult<AutoBuildResult>> Execute(
        [FromBody] AutoBuildRequest request, CancellationToken ct)
    {
        var (jobId, userId, error) = await ResolveContext();
        if (error != null) return error;

        var result = await _service.BuildAsync(jobId!.Value, userId!, request, ct);
        return Ok(result);
    }

    /// <summary>
    /// POST /api/auto-build/undo — Delete all games for the current job.
    /// </summary>
    [HttpPost("undo")]
    public async Task<ActionResult> Undo(CancellationToken ct)
    {
        var (jobId, _, error) = await ResolveContext();
        if (error != null) return error;

        var count = await _service.UndoAsync(jobId!.Value, ct);
        return Ok(new { gamesDeleted = count });
    }

    /// <summary>
    /// GET /api/auto-build/validate — Run post-build QA checks on the current schedule.
    /// </summary>
    [HttpGet("validate")]
    public async Task<ActionResult<AutoBuildQaResult>> Validate(CancellationToken ct)
    {
        var (jobId, _, error) = await ResolveContext();
        if (error != null) return error;

        var result = await _service.ValidateAsync(jobId!.Value, ct);
        return Ok(result);
    }
}

/// <summary>
/// Request body for the analyze endpoint.
/// </summary>
public record AutoBuildAnalyzeRequest
{
    public required Guid SourceJobId { get; init; }
}
