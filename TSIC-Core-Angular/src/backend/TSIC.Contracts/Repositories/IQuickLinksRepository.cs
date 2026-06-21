using TSIC.Contracts.Dtos.Widgets;
using TSIC.Domain.Entities;

namespace TSIC.Contracts.Repositories;

/// <summary>
/// Data access for the quicklinks feature — the global LinkType catalog and
/// per-job JobQuickLink overrides. Reads are AsNoTracking; the tracked
/// single-fetch supports upsert against the unique (JobId, LinkKey) constraint.
/// </summary>
public interface IQuickLinksRepository
{
    // ── Picker reference data (job-type → job cascade, mirrors WidgetEditor) ──
    Task<List<JobTypeRefDto>> GetJobTypesAsync(CancellationToken ct = default);
    Task<List<JobRefDto>> GetJobsByJobTypeAsync(int jobTypeId, CancellationToken ct = default);

    /// <summary>Lightweight job header (name + path) for the editor model; null when the job is missing.</summary>
    Task<JobRefDto?> GetJobRefAsync(Guid jobId, CancellationToken ct = default);

    /// <summary>Active catalog link types, ordered by DefaultSortOrder.</summary>
    Task<List<LinkType>> GetActiveLinkTypesAsync(CancellationToken ct = default);

    /// <summary>All per-job override rows for a job.</summary>
    Task<List<JobQuickLink>> GetJobQuickLinksByJobAsync(Guid jobId, CancellationToken ct = default);

    /// <summary>Tracked single override row for upsert; null when none exists.</summary>
    Task<JobQuickLink?> GetJobQuickLinkAsync(Guid jobId, string linkKey, CancellationToken ct = default);

    void AddJobQuickLink(JobQuickLink row);
    void RemoveJobQuickLink(JobQuickLink row);

    Task SaveChangesAsync(CancellationToken ct = default);
}
