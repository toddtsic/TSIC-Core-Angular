using TSIC.Contracts.Dtos.Scheduling;

namespace TSIC.Contracts.Services;

/// <summary>
/// Service for the Auto-Build Entire Schedule feature.
/// Horizontal-first placement with weighted scoring engine.
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
    /// Undo: Delete all games for the current job.
    /// Returns the count of games deleted.
    /// </summary>
    Task<int> UndoAsync(Guid jobId, CancellationToken ct = default);

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
    /// Build: Horizontal-first placement with weighted scoring,
    /// processing order control, and constraint priorities.
    /// </summary>
    Task<AutoBuildResult> BuildAsync(
        Guid jobId, string userId, AutoBuildRequest request, CancellationToken ct = default);

    /// <summary>
    /// Load strategy profiles for a job. Three-layer resolution:
    /// 1. Saved profiles from DB → Source="saved"
    /// 2. Inferred from source job via AttributeExtractor → Source="inferred"
    /// 3. Defaults from current division names → Source="defaults"
    /// </summary>
    Task<DivisionStrategyProfileResponse> LoadStrategyProfilesAsync(
        Guid jobId, Guid? sourceJobId, CancellationToken ct = default);

    /// <summary>
    /// Save strategy profiles for a job (standalone — does not require a build).
    /// Upserts all entries and returns the reloaded response.
    /// </summary>
    Task<DivisionStrategyProfileResponse> SaveStrategyProfilesAsync(
        Guid jobId, List<DivisionStrategyEntry> strategies, CancellationToken ct = default);

    /// <summary>
    /// Auto-generate round-robin pairings for team counts that don't have them yet.
    /// </summary>
    Task<EnsurePairingsResponse> EnsurePairingsAsync(
        Guid jobId, string userId, EnsurePairingsRequest request, CancellationToken ct = default);
}
