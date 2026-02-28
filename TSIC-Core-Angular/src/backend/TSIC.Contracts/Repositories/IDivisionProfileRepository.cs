using TSIC.Domain.Entities;

namespace TSIC.Contracts.Repositories;

/// <summary>
/// Repository for per-division-name scheduling strategy profiles.
/// Keyed by (JobId, DivisionName) — division names are stable across agegroups.
/// </summary>
public interface IDivisionProfileRepository
{
    /// <summary>
    /// Get all saved strategy profiles for a job.
    /// </summary>
    Task<List<DivisionScheduleProfile>> GetByJobIdAsync(
        Guid jobId, CancellationToken ct = default);

    /// <summary>
    /// Upsert a batch of profiles for a job. Existing rows matched by
    /// (JobId, DivisionName) are updated; new rows are inserted.
    /// Does NOT call SaveChanges — caller must follow up.
    /// </summary>
    Task UpsertBatchAsync(
        Guid jobId,
        List<DivisionScheduleProfile> profiles,
        CancellationToken ct = default);

    /// <summary>
    /// Delete all profiles for a job.
    /// Does NOT call SaveChanges — caller must follow up.
    /// </summary>
    Task DeleteByJobIdAsync(Guid jobId, CancellationToken ct = default);

    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
