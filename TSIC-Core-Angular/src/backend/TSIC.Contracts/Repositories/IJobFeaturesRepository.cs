using TSIC.Domain.Entities;

namespace TSIC.Contracts.Repositories;

/// <summary>
/// Per-job Team Events feature switches (<c>teamevents.JobFeatures</c>), one row per job.
/// A job with no row has every feature off — callers treat null as "all disabled".
/// </summary>
public interface IJobFeaturesRepository
{
    /// <summary>Read-only. Null when the job has no row (all features off).</summary>
    Task<JobFeatures?> GetByJobIdAsync(Guid jobId, CancellationToken ct = default);

    Task<JobFeatures?> GetTrackedAsync(Guid jobId, CancellationToken ct = default);
    void Add(JobFeatures features);
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
