using TSIC.Contracts.Dtos.Scheduling;

namespace TSIC.Contracts.Services;

/// <summary>
/// Computes the ordered scheduling-readiness checklist for a job.
/// Strictly read-only — unlike the cascade resolver, this never self-heals or seeds rows.
/// </summary>
public interface ISchedulingChecklistService
{
    Task<SchedulingChecklistDto> GetChecklistAsync(Guid jobId, CancellationToken ct = default);
}
