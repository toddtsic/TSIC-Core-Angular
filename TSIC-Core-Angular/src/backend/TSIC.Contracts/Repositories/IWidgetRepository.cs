using TSIC.Domain.Entities;

namespace TSIC.Contracts.Repositories;

/// <summary>
/// Repository for widget dashboard data access.
/// Handles the three-tier merge: WidgetDefault (Role+JobType) + JobWidget (per-job overrides).
/// </summary>
public interface IWidgetRepository
{
    /// <summary>
    /// Get default widgets for a given job type and role.
    /// Includes Widget and Category navigation properties.
    /// </summary>
    Task<List<WidgetDefault>> GetDefaultsAsync(
        int jobTypeId,
        string roleId,
        CancellationToken ct = default);

    /// <summary>
    /// Get per-job widget overrides and additions for a given job and role.
    /// Includes Widget and Category navigation properties.
    /// </summary>
    Task<List<JobWidget>> GetJobWidgetsAsync(
        Guid jobId,
        string roleId,
        CancellationToken ct = default);

    /// <summary>
    /// Get the JobTypeId for a given job.
    /// </summary>
    Task<int?> GetJobTypeIdAsync(Guid jobId, CancellationToken ct = default);
}
