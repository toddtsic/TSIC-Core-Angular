using TSIC.Contracts.Dtos.Scheduling;

namespace TSIC.Contracts.Services;

/// <summary>
/// Centralized schedule quality-assurance service.
/// Used by Auto-Build post-validation AND the standalone QA Results view.
/// </summary>
public interface IScheduleQaService
{
    /// <summary>
    /// Run all QA checks against the current schedule for a job.
    /// </summary>
    Task<AutoBuildQaResult> RunValidationAsync(Guid jobId, CancellationToken ct = default);
}
