using TSIC.Contracts.Dtos.Scheduling;

namespace TSIC.Contracts.Services;

/// <summary>
/// Service for the Auto-Build Entire Schedule feature.
/// Orchestrates pattern extraction from a prior year's schedule,
/// division matching, and schedule generation.
/// </summary>
public interface IAutoBuildScheduleService
{
    /// <summary>
    /// Phase 1: Get available source jobs for the current job.
    /// Auto-detects candidates: same customer, prior years, with scheduled games.
    /// </summary>
    Task<List<AutoBuildSourceJobDto>> GetSourceJobsAsync(
        Guid jobId, CancellationToken ct = default);

    /// <summary>
    /// Phase 2-3: Analyze source pattern, match divisions, compute feasibility.
    /// Returns the full analysis with division matches and confidence score.
    /// </summary>
    Task<AutoBuildAnalysisResponse> AnalyzeAsync(
        Guid jobId, Guid sourceJobId, CancellationToken ct = default);

    /// <summary>
    /// Phase 6-7: Execute the auto-build with user-provided resolutions
    /// for mismatched divisions.
    /// </summary>
    Task<AutoBuildResult> BuildAsync(
        Guid jobId, string userId, AutoBuildRequest request, CancellationToken ct = default);

    /// <summary>
    /// Undo: Delete all games for the current job.
    /// Returns the count of games deleted.
    /// </summary>
    Task<int> UndoAsync(Guid jobId, CancellationToken ct = default);

    /// <summary>
    /// Phase 8: Run post-build QA validation checks.
    /// </summary>
    Task<AutoBuildQaResult> ValidateAsync(Guid jobId, CancellationToken ct = default);
}
