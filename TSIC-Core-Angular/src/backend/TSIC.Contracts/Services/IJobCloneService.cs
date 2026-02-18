using TSIC.Contracts.Dtos.JobClone;

namespace TSIC.Contracts.Services;

public interface IJobCloneService
{
    /// <summary>
    /// Clone a source job into a new job with the given parameters.
    /// Clones: Job, DisplayOptions, OwlImages, Bulletins, AgeRanges,
    /// Menus (with items), Admin Registrations, League, Agegroups, Divisions.
    /// </summary>
    Task<JobCloneResponse> CloneJobAsync(JobCloneRequest request, string superUserId, CancellationToken ct = default);

    /// <summary>
    /// List all jobs available as clone sources (for the frontend picker).
    /// </summary>
    Task<List<JobCloneSourceDto>> GetCloneableJobsAsync(CancellationToken ct = default);
}
