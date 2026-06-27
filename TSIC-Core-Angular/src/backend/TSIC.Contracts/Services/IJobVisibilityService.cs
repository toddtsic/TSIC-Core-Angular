using TSIC.Contracts.Dtos.JobConfig;

namespace TSIC.Contracts.Services;

/// <summary>
/// "Quick Links" editor — reads/writes the landing-hero CTA visibility flags for a
/// single job. AdminOnly (Director/SuperDirector/SuperUser); the insurance and store
/// flags are SuperUser-only and ignored on write for non-super callers (mirrors the
/// per-field gating in JobConfigService). Partial updates (non-null fields only)
/// support per-toggle saves.
/// </summary>
public interface IJobVisibilityService
{
    Task<JobVisibilityDto> GetAsync(Guid jobId, CancellationToken ct = default);

    Task UpdateAsync(Guid jobId, UpdateJobVisibilityRequest request, bool isSuperUser, CancellationToken ct = default);
}
