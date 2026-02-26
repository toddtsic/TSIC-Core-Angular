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
    private readonly IAutoBuildScheduleService _service;
    private readonly IJobLookupService _jobLookupService;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<AutoBuildController> _logger;

    public AutoBuildController(
        IAutoBuildScheduleService service,
        IJobLookupService jobLookupService,
        IWebHostEnvironment env,
        ILogger<AutoBuildController> logger)
    {
        _service = service;
        _jobLookupService = jobLookupService;
        _env = env;
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
    /// GET /api/auto-build/game-summary — Get current schedule status per agegroup/division.
    /// </summary>
    [HttpGet("game-summary")]
    public async Task<ActionResult<GameSummaryResponse>> GetGameSummary(CancellationToken ct)
    {
        var (jobId, _, error) = await ResolveContext();
        if (error != null) return error;

        var result = await _service.GetGameSummaryAsync(jobId!.Value, ct);
        return Ok(result);
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
    /// POST /api/auto-build/propose-mappings — Propose agegroup mappings for user confirmation.
    /// </summary>
    [HttpPost("propose-mappings")]
    public async Task<ActionResult<AgegroupMappingResponse>> ProposeMappings(
        [FromBody] AutoBuildAnalyzeRequest request, CancellationToken ct)
    {
        var (jobId, _, error) = await ResolveContext();
        if (error != null) return error;

        var result = await _service.ProposeAgegroupMappingsAsync(
            jobId!.Value, request.SourceJobId, ct);
        return Ok(result);
    }

    /// <summary>
    /// POST /api/auto-build/analyze — Analyze source pattern with confirmed agegroup mappings.
    /// </summary>
    [HttpPost("analyze")]
    public async Task<ActionResult<AutoBuildAnalysisResponse>> Analyze(
        [FromBody] AutoBuildAnalyzeRequest request, CancellationToken ct)
    {
        var (jobId, _, error) = await ResolveContext();
        if (error != null) return error;

        var result = await _service.AnalyzeAsync(
            jobId!.Value, request.SourceJobId, request.AgegroupMappings, ct);
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
    /// SAFETY: Only available in Development environment on non-production hosts.
    /// </summary>
    [HttpPost("undo")]
    public async Task<ActionResult> Undo(CancellationToken ct)
    {
        // ── SAFETY: Environment guard (belt + suspenders + hostname) ──
        var hostname = Environment.MachineName;
        if (!_env.IsDevelopment()
            || string.Equals(hostname, "TSIC-PHOENIX", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogCritical(
                "BLOCKED: Delete-all-games attempted in non-dev environment. " +
                "Hostname={Hostname}, Env={Env}",
                hostname, _env.EnvironmentName);
            return StatusCode(403, new
            {
                message = "Delete all games is only available in Development environment."
            });
        }
        // ── End safety guard ─────────────────────────────────────────

        var (jobId, userId, error) = await ResolveContext();
        if (error != null) return error;

        _logger.LogWarning(
            "Delete-all-games executing. Hostname={Hostname}, Env={Env}, " +
            "JobId={JobId}, UserId={UserId}",
            hostname, _env.EnvironmentName, jobId, userId);

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

