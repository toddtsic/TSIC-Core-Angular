using TSIC.Domain.Entities;

namespace TSIC.Contracts.Repositories;

/// <summary>
/// Repository for the 3-level scheduling cascade tables:
///   EventScheduleDefaults → AgegroupScheduleProfile → DivisionScheduleProfile
///   AgegroupWaveAssignment → DivisionWaveAssignment
/// All methods filter through JobLeagues to scope by jobId.
/// </summary>
public interface IScheduleCascadeRepository
{
    // ── Event Defaults (keyed by JobId) ──

    Task<EventScheduleDefaults?> GetEventDefaultsAsync(
        Guid jobId, CancellationToken ct = default);

    /// <summary>
    /// Insert or update the event-level defaults row.
    /// Does NOT call SaveChanges — caller must follow up.
    /// </summary>
    Task UpsertEventDefaultsAsync(
        EventScheduleDefaults defaults, CancellationToken ct = default);

    // ── Agegroup Schedule Profile (keyed by AgegroupId) ──

    /// <summary>
    /// Get all agegroup-level profiles for agegroups belonging to this job.
    /// </summary>
    Task<List<AgegroupScheduleProfile>> GetAgegroupProfilesAsync(
        Guid jobId, CancellationToken ct = default);

    /// <summary>
    /// Insert or update a single agegroup profile.
    /// Does NOT call SaveChanges.
    /// </summary>
    Task UpsertAgegroupProfileAsync(
        AgegroupScheduleProfile profile, CancellationToken ct = default);

    Task DeleteAgegroupProfileAsync(
        Guid agegroupId, CancellationToken ct = default);

    // ── Division Schedule Profile (keyed by DivisionId) ──

    /// <summary>
    /// Get all division-level profiles for divisions belonging to this job.
    /// </summary>
    Task<List<DivisionScheduleProfile>> GetDivisionProfilesAsync(
        Guid jobId, CancellationToken ct = default);

    /// <summary>
    /// Insert or update a single division profile.
    /// Does NOT call SaveChanges.
    /// </summary>
    Task UpsertDivisionProfileAsync(
        DivisionScheduleProfile profile, CancellationToken ct = default);

    Task DeleteDivisionProfileAsync(
        Guid divisionId, CancellationToken ct = default);

    // ── Agegroup Wave Assignment (keyed by AgegroupId + GameDate) ──

    /// <summary>
    /// Get all agegroup-level wave assignments for this job.
    /// </summary>
    Task<List<AgegroupWaveAssignment>> GetAgegroupWavesAsync(
        Guid jobId, CancellationToken ct = default);

    /// <summary>
    /// Replace all wave assignments for a single agegroup (delete + insert batch).
    /// Does NOT call SaveChanges.
    /// </summary>
    Task UpsertAgegroupWavesAsync(
        Guid agegroupId, List<AgegroupWaveAssignment> waves, CancellationToken ct = default);

    Task DeleteAgegroupWavesAsync(
        Guid agegroupId, CancellationToken ct = default);

    // ── Division Wave Assignment (keyed by DivisionId + GameDate) ──

    /// <summary>
    /// Get all division-level wave assignments for this job.
    /// </summary>
    Task<List<DivisionWaveAssignment>> GetDivisionWavesAsync(
        Guid jobId, CancellationToken ct = default);

    /// <summary>
    /// Replace all wave assignments for a single division (delete + insert batch).
    /// Does NOT call SaveChanges.
    /// </summary>
    Task UpsertDivisionWavesAsync(
        Guid divisionId, List<DivisionWaveAssignment> waves, CancellationToken ct = default);

    Task DeleteDivisionWavesAsync(
        Guid divisionId, CancellationToken ct = default);

    // ── Single-entity wave add (for cascade date migration) ──

    void AddAgegroupWave(AgegroupWaveAssignment wave);
    void AddDivisionWave(DivisionWaveAssignment wave);

    // ── Date-scoped wave queries (for cascade date operations) ──

    /// <summary>Get agegroup wave assignments for a specific date across all agegroups in the job.</summary>
    Task<List<AgegroupWaveAssignment>> GetAgegroupWavesByDateAsync(
        Guid jobId, DateTime gameDate, CancellationToken ct = default);

    /// <summary>Delete agegroup wave assignments for a specific date across all agegroups in the job.</summary>
    Task DeleteAgegroupWavesByDateAsync(
        Guid jobId, DateTime gameDate, CancellationToken ct = default);

    /// <summary>Get division wave assignments for a specific date across all divisions in the job.</summary>
    Task<List<DivisionWaveAssignment>> GetDivisionWavesByDateAsync(
        Guid jobId, DateTime gameDate, CancellationToken ct = default);

    /// <summary>Delete division wave assignments for a specific date across all divisions in the job.</summary>
    Task DeleteDivisionWavesByDateAsync(
        Guid jobId, DateTime gameDate, CancellationToken ct = default);

    // ── Division Processing Order (keyed by JobId + DivisionId) ──

    /// <summary>
    /// Get persisted division build ordering for this job, sorted by SortOrder.
    /// Returns empty list when no ordering has been saved.
    /// </summary>
    Task<List<DivisionProcessingOrder>> GetProcessingOrderAsync(
        Guid jobId, CancellationToken ct = default);

    /// <summary>
    /// Replace all processing order rows for a job (delete + insert batch).
    /// Does NOT call SaveChanges.
    /// </summary>
    Task UpsertProcessingOrderAsync(
        Guid jobId, List<DivisionProcessingOrder> entries, CancellationToken ct = default);

    Task DeleteProcessingOrderAsync(
        Guid jobId, CancellationToken ct = default);

    // ── Bulk operations ──

    /// <summary>
    /// Delete ALL cascade data for a job (all tables).
    /// Calls SaveChanges internally.
    /// </summary>
    Task DeleteAllForJobAsync(Guid jobId, CancellationToken ct = default);

    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
