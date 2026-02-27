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
    /// Game Summary: Get per-division game counts for the current job.
    /// Shows team count, scheduled game count, and expected round-robin game count.
    /// </summary>
    Task<GameSummaryResponse> GetGameSummaryAsync(
        Guid jobId, CancellationToken ct = default);

    /// <summary>
    /// Phase 1: Get available source jobs for the current job.
    /// Auto-detects candidates: same customer, prior years, with scheduled games.
    /// </summary>
    Task<List<AutoBuildSourceJobDto>> GetSourceJobsAsync(
        Guid jobId, CancellationToken ct = default);

    /// <summary>
    /// Phase 1.5: Propose agegroup mappings for user confirmation.
    /// Extracts distinct agegroups from source and current, proposes best-guess mapping.
    /// </summary>
    Task<AgegroupMappingResponse> ProposeAgegroupMappingsAsync(
        Guid jobId, Guid sourceJobId, CancellationToken ct = default);

    /// <summary>
    /// Phase 2-3: Analyze source pattern with confirmed agegroup mappings.
    /// Name-first matching within mapped agegroups, pool-size fallback for unmatched.
    /// </summary>
    Task<AutoBuildAnalysisResponse> AnalyzeAsync(
        Guid jobId, Guid sourceJobId,
        List<ConfirmedAgegroupMapping>? agegroupMappings = null,
        CancellationToken ct = default);

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

    // ── V2 Methods ──────────────────────────────────────────

    /// <summary>
    /// Check the three mandatory prerequisites before auto-build:
    /// Pools assigned, Pairings created, Timeslots configured.
    /// </summary>
    Task<PrerequisiteCheckResponse> CheckPrerequisitesAsync(
        Guid jobId, CancellationToken ct = default);

    /// <summary>
    /// Extract Q1–Q10 attribute profiles from a source job's schedule,
    /// grouped by team count (TCnt).
    /// </summary>
    Task<ProfileExtractionResponse> ExtractProfilesAsync(
        Guid jobId, Guid sourceJobId, CancellationToken ct = default);

    /// <summary>
    /// V2 Build: Horizontal-first placement with weighted scoring,
    /// processing order control, and constraint priorities.
    /// </summary>
    Task<AutoBuildV2Result> BuildV2Async(
        Guid jobId, string userId, AutoBuildV2Request request, CancellationToken ct = default);
}
