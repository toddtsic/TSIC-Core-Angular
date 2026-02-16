using TSIC.Contracts.Dtos.Widgets;

namespace TSIC.Contracts.Services;

/// <summary>
/// Service for assembling the widget dashboard.
/// Merges WidgetDefault (Role+JobType) with JobWidget (per-job overrides)
/// and returns a structured, section-grouped response.
/// </summary>
public interface IWidgetDashboardService
{
    /// <summary>
    /// Get the merged widget dashboard for a given job and role.
    /// Accepts role name (from JWT claim); resolves to role GUID internally.
    /// </summary>
    Task<WidgetDashboardResponse> GetDashboardAsync(
        Guid jobId,
        string roleName,
        CancellationToken ct = default);

    /// <summary>
    /// Get live aggregate metrics (registrations, financials, scheduling) for the dashboard hero.
    /// </summary>
    Task<DashboardMetricsDto> GetMetricsAsync(
        Guid jobId,
        CancellationToken ct = default);
}
