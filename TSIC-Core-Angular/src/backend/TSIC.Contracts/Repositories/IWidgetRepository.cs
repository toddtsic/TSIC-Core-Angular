using TSIC.Contracts.Dtos.Widgets;

namespace TSIC.Contracts.Repositories;

/// <summary>
/// Repository for widget dashboard data access.
/// Handles the three-tier merge: WidgetDefault (Role+JobType) + JobWidget (per-job overrides).
/// </summary>
public interface IWidgetRepository
{
    /// <summary>
    /// Get default widgets for a given job type and role.
    /// Projects Widget + Category navigations into flat WidgetItemProjection.
    /// </summary>
    Task<List<WidgetItemProjection>> GetDefaultsAsync(
        int jobTypeId,
        string roleId,
        CancellationToken ct = default);

    /// <summary>
    /// Get per-job widget overrides and additions for a given job and role.
    /// Projects Widget + Category navigations into flat WidgetItemProjection.
    /// </summary>
    Task<List<WidgetItemProjection>> GetJobWidgetsAsync(
        Guid jobId,
        string roleId,
        CancellationToken ct = default);

    /// <summary>
    /// Get the JobTypeId for a given job.
    /// </summary>
    Task<int?> GetJobTypeIdAsync(Guid jobId, CancellationToken ct = default);

    /// <summary>
    /// Get registration, financial, and team/club aggregate metrics for a job.
    /// Returns a projection with counts and sums — no navigation property loading.
    /// </summary>
    Task<DashboardMetricsDto> GetDashboardMetricsAsync(Guid jobId, CancellationToken ct = default);
}
