using TSIC.Contracts.Dtos.JobConfig;

namespace TSIC.Contracts.Services;

/// <summary>
/// SuperUser "Quick Links" editor — reads/writes the landing-hero CTA visibility
/// flags for a single job. Partial updates (non-null fields only) support
/// per-toggle saves.
/// </summary>
public interface IJobVisibilityService
{
    Task<JobVisibilityDto> GetAsync(Guid jobId, CancellationToken ct = default);

    Task UpdateAsync(Guid jobId, UpdateJobVisibilityRequest request, CancellationToken ct = default);
}
