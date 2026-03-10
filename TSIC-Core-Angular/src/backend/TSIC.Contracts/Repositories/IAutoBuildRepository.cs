using TSIC.Contracts.Dtos.Scheduling;

namespace TSIC.Contracts.Repositories;

/// <summary>
/// Repository for Auto-Build Schedule feature: pattern extraction from prior year,
/// division matching, and field mapping.
/// </summary>
public interface IAutoBuildRepository
{
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
    /// Get normalized addresses for a set of field IDs.
    /// Returns FieldId → "address|city|zip" (lowercased, trimmed) for address-based matching.
    /// Fields with no address data are excluded.
    /// </summary>
    Task<Dictionary<Guid, string>> GetFieldAddressesAsync(
        IEnumerable<Guid> fieldIds, CancellationToken ct = default);

    /// <summary>
    /// Get existing game count per division for the current job.
    /// Used to detect partially-scheduled divisions.
    /// </summary>
    Task<Dictionary<Guid, int>> GetExistingGameCountsByDivisionAsync(
        Guid jobId, CancellationToken ct = default);

    // ── Source Preconfiguration (returning tournament carry-forward) ──

    /// <summary>
    /// Get agegroup-level metadata (color, grad years) from the source job.
    /// Joins Schedule → Agegroups to read structured fields not available in the denormalized schedule.
    /// </summary>
    Task<List<SourceAgegroupMeta>> GetSourceAgegroupMetaAsync(
        Guid sourceJobId, CancellationToken ct = default);

    /// <summary>
    /// Get timeslot dates from the source job, grouped by agegroup name.
    /// Used for date carry-forward — advance dates by yearDelta and map to matching DOW.
    /// </summary>
    Task<Dictionary<string, List<SourceDateEntry>>> GetSourceDatesAsync(
        Guid sourceJobId, CancellationToken ct = default);

    /// <summary>
    /// Get per-agegroup field usage patterns from the source schedule (RR games only).
    /// Returns field name, ID, game count, and DOWs used — grouped by agegroup name.
    /// Used for field constraint learning.
    /// </summary>
    Task<Dictionary<string, List<SourceFieldUsage>>> GetSourceFieldUsageAsync(
        Guid sourceJobId, CancellationToken ct = default);

    /// <summary>
    /// Extract per-agegroup per-day earliest game time from the source Schedule table.
    /// Used to derive correct per-agegroup start times, wave assignments, and chip-stack ordering.
    /// </summary>
    Task<Dictionary<string, List<SourceAgegroupTiming>>> GetSourceAgegroupTimingAsync(
        Guid sourceJobId, CancellationToken ct = default);

    /// <summary>
    /// Extract per-division per-day earliest game time from the source Schedule table.
    /// Groups by (AgegroupName, DivName, DayOfWeek) for per-division wave derivation.
    /// Returns agegroupName → list of (agegroupName, divName, dayOfWeek, earliestTime).
    /// </summary>
    Task<Dictionary<string, List<SourceDivisionTiming>>> GetSourceDivisionTimingAsync(
        Guid sourceJobId, CancellationToken ct = default);

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

    // ── Prerequisite Checks ────────────────────────────────

    /// <summary>
    /// Count active teams with no division assignment (DivId is null) for the given job.
    /// Teams with status WAITLIST, DROPPED, or name "Unassigned" are excluded.
    /// </summary>
    Task<int> GetUnassignedActiveTeamCountAsync(Guid jobId, CancellationToken ct = default);

    /// <summary>
    /// Get agegroup names that have active divisions but no timeslot dates configured.
    /// </summary>
    Task<List<string>> GetAgegroupsMissingTimeslotDatesAsync(
        Guid jobId, string season, string year, CancellationToken ct = default);

    // ── Cross-Event Analysis ──────────────────────────────────

    /// <summary>
    /// Find jobs whose name contains any of the given patterns and that have
    /// at least one scheduled RR game. Returns JobId + JobName pairs.
    /// </summary>
    Task<List<(Guid JobId, string JobName)>> FindJobsByNamePatternsAsync(
        IEnumerable<string> namePatterns, CancellationToken ct = default);

    /// <summary>
    /// Get all RR matchup records across multiple jobs for cross-event analysis.
    /// Returns (Agegroup, TeamClub, TeamName, OpponentClub, OpponentName, JobId) tuples.
    /// </summary>
    Task<List<CrossEventMatchupRaw>> GetCrossEventMatchupsAsync(
        IEnumerable<Guid> jobIds, CancellationToken ct = default);
}
