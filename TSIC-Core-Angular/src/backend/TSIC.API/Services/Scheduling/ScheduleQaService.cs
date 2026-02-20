using TSIC.Contracts.Dtos.Scheduling;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;

namespace TSIC.API.Services.Scheduling;

/// <summary>
/// Centralized schedule QA validation.
/// Delegates to AutoBuildRepository for data queries.
/// </summary>
public sealed class ScheduleQaService : IScheduleQaService
{
    private readonly IAutoBuildRepository _repo;

    public ScheduleQaService(IAutoBuildRepository repo)
    {
        _repo = repo;
    }

    public async Task<AutoBuildQaResult> RunValidationAsync(
        Guid jobId, CancellationToken ct = default)
    {
        return await _repo.RunQaValidationAsync(jobId, ct);
    }
}
