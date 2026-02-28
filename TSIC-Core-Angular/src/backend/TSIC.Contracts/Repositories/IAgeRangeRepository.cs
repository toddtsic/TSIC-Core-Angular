using TSIC.Contracts.Dtos.AgeRange;
using TSIC.Domain.Entities;

namespace TSIC.Contracts.Repositories;

public interface IAgeRangeRepository
{
    /// <summary>
    /// Get all age ranges for a job, ordered by RangeLeft ascending.
    /// </summary>
    Task<List<AgeRangeDto>> GetAllForJobAsync(
        Guid jobId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a single age range by ID (tracked entity for updates).
    /// </summary>
    Task<JobAgeRanges?> GetByIdAsync(
        int ageRangeId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if a range name already exists for this job (case-insensitive).
    /// Excludes the given ID when editing.
    /// </summary>
    Task<bool> ExistsWithNameAsync(
        Guid jobId,
        string rangeName,
        int? excludeId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if a date range overlaps with any existing range for this job.
    /// Returns the overlapping range name if found.
    /// </summary>
    Task<(bool Overlaps, string? OverlappingName)> HasOverlapAsync(
        Guid jobId,
        DateTime rangeLeft,
        DateTime rangeRight,
        int? excludeId = null,
        CancellationToken cancellationToken = default);

    void Add(JobAgeRanges entity);
    void Remove(JobAgeRanges entity);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
