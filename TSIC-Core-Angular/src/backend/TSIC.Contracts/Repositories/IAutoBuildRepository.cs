using TSIC.Contracts.Dtos.Scheduling;

namespace TSIC.Contracts.Repositories;

/// <summary>
/// Repository for Auto-Build Schedule feature: pattern extraction from prior year,
/// division matching, and field mapping.
/// </summary>
public interface IAutoBuildRepository
{
    /// <summary>
    /// Find candidate source jobs for auto-build: same CustomerId as target job,
    /// with at least one scheduled game, ordered by year descending.
    /// </summary>
    Task<List<AutoBuildSourceJobDto>> GetSourceJobCandidatesAsync(
        Guid targetJobId, CancellationToken ct = default);

    /// <summary>
    /// Extract the complete game placement pattern from a source job's schedule.
    /// For each game, abstracts the literal date into (DayOfWeek, TimeOfDay, DayOrdinal)
    /// for year-agnostic replay.
    /// </summary>
    Task<List<GamePlacementPattern>> ExtractPatternAsync(
        Guid sourceJobId, CancellationToken ct = default);

    /// <summary>
    /// Get division summaries from the source (prior year) job's schedule.
    /// Groups by (AgegroupName, DivName) with team count from T1No/T2No max
    /// and game count.
    /// </summary>
    Task<List<SourceDivisionSummary>> GetSourceDivisionSummariesAsync(
        Guid sourceJobId, CancellationToken ct = default);

    /// <summary>
    /// Get division summaries from the current year's job.
    /// Includes AgegroupId, DivId, and active team count.
    /// </summary>
    Task<List<CurrentDivisionSummary>> GetCurrentDivisionSummariesAsync(
        Guid currentJobId, CancellationToken ct = default);

    /// <summary>
    /// Get distinct field names used in the source job's schedule.
    /// </summary>
    Task<List<string>> GetSourceFieldNamesAsync(
        Guid sourceJobId, CancellationToken ct = default);

    /// <summary>
    /// Get fields assigned to the current job's league-season for field-name matching.
    /// </summary>
    Task<List<FieldNameMapping>> GetCurrentFieldsAsync(
        Guid leagueId, string season, CancellationToken ct = default);

    /// <summary>
    /// Get existing game count per division for the current job.
    /// Used to detect partially-scheduled divisions.
    /// </summary>
    Task<Dictionary<Guid, int>> GetExistingGameCountsByDivisionAsync(
        Guid jobId, CancellationToken ct = default);

    /// <summary>
    /// Get the job name for a job ID.
    /// </summary>
    Task<string?> GetJobNameAsync(Guid jobId, CancellationToken ct = default);

    /// <summary>
    /// Get the year for a job ID.
    /// </summary>
    Task<string?> GetJobYearAsync(Guid jobId, CancellationToken ct = default);

    /// <summary>
    /// Delete ALL scheduled games for a job (for undo operation).
    /// Returns the count of games deleted.
    /// </summary>
    Task<int> DeleteAllGamesForJobAsync(Guid jobId, CancellationToken ct = default);

    Task<int> SaveChangesAsync(CancellationToken ct = default);

    // ── Post-Build QA Validation ────────────────────────────
    Task<AutoBuildQaResult> RunQaValidationAsync(Guid jobId, CancellationToken ct = default);
}
