using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using TSIC.API.Extensions;
using TSIC.API.Services.Shared.Jobs;
using TSIC.Contracts.Dtos.Scheduling;
using TSIC.Contracts.Services;

namespace TSIC.API.Controllers;

/// <summary>
/// Auto-Build Schedule: horizontal-first placement with scoring engine.
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
    /// POST /api/auto-build/undo — Delete all games for the current job.
    /// Protected by AdminOnly policy (Director, SuperDirector, SuperUser).
    /// </summary>
    [HttpPost("undo")]
    public async Task<ActionResult> Undo(CancellationToken ct)
    {
        var (jobId, userId, error) = await ResolveContext();
        if (error != null) return error;

        _logger.LogWarning(
            "Delete-all-games executing. Hostname={Hostname}, Env={Env}, " +
            "JobId={JobId}, UserId={UserId}",
            Environment.MachineName, _env.EnvironmentName, jobId, userId);

        var count = await _service.UndoAsync(jobId!.Value, ct);
        return Ok(new { gamesDeleted = count });
    }

    /// <summary>
    /// GET /api/auto-build/prerequisites — Check pools, pairings, and timeslots readiness.
    /// </summary>
    [HttpGet("prerequisites")]
    public async Task<ActionResult<PrerequisiteCheckResponse>> CheckPrerequisites(CancellationToken ct)
    {
        var (jobId, _, error) = await ResolveContext();
        if (error != null) return error;

        var result = await _service.CheckPrerequisitesAsync(jobId!.Value, ct);
        return Ok(result);
    }

    /// <summary>
    /// POST /api/auto-build/extract-profiles — Extract Q1–Q10 profiles from source schedule.
    /// </summary>
    [HttpPost("extract-profiles")]
    public async Task<ActionResult<ProfileExtractionResponse>> ExtractProfiles(
        [FromBody] AutoBuildAnalyzeRequest request, CancellationToken ct)
    {
        var (jobId, _, error) = await ResolveContext();
        if (error != null) return error;

        var result = await _service.ExtractProfilesAsync(
            jobId!.Value, request.SourceJobId, ct);
        return Ok(result);
    }

    /// <summary>
    /// POST /api/auto-build/execute — Horizontal-first placement with scoring engine.
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
    /// GET /api/auto-build/strategy-profiles — Load strategy profiles (saved, inferred, or defaults).
    /// </summary>
    [HttpGet("strategy-profiles")]
    public async Task<ActionResult<DivisionStrategyProfileResponse>> GetStrategyProfiles(
        [FromQuery] Guid? sourceJobId, CancellationToken ct)
    {
        var (jobId, _, error) = await ResolveContext();
        if (error != null) return error;

        var result = await _service.LoadStrategyProfilesAsync(jobId!.Value, sourceJobId, ct);
        return Ok(result);
    }

    /// <summary>
    /// PUT /api/auto-build/strategy-profiles — Save strategy profiles standalone (no build required).
    /// </summary>
    [HttpPut("strategy-profiles")]
    public async Task<ActionResult<DivisionStrategyProfileResponse>> SaveStrategyProfiles(
        [FromBody] List<DivisionStrategyEntry> strategies, CancellationToken ct)
    {
        var (jobId, _, error) = await ResolveContext();
        if (error != null) return error;

        var result = await _service.SaveStrategyProfilesAsync(jobId!.Value, strategies, ct);
        return Ok(result);
    }

    /// <summary>
    /// POST /api/auto-build/ensure-pairings — Auto-generate round-robin pairings for missing team counts.
    /// </summary>
    [HttpPost("ensure-pairings")]
    public async Task<ActionResult<EnsurePairingsResponse>> EnsurePairings(
        [FromBody] EnsurePairingsRequest request, CancellationToken ct)
    {
        var (jobId, userId, error) = await ResolveContext();
        if (error != null) return error;

        var result = await _service.EnsurePairingsAsync(jobId!.Value, userId!, request, ct);
        return Ok(result);
    }
}
