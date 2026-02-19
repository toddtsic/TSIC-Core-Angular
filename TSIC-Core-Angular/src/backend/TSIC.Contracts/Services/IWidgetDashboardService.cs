using TSIC.Contracts.Dtos.Widgets;

namespace TSIC.Contracts.Services;

/// <summary>
/// Service for assembling the widget dashboard.
/// Merges WidgetDefault (Role+JobType) → JobWidget (per-job overrides) → UserWidget (per-user delta)
/// and returns a structured, workspace/category-grouped response.
/// </summary>
public interface IWidgetDashboardService
{
    /// <summary>
    /// Get the merged widget dashboard for a given job and role.
    /// Accepts role name (from JWT claim); resolves to role GUID internally.
    /// When registrationId is provided, applies per-user customizations (3rd merge layer).
    /// </summary>
    Task<WidgetDashboardResponse> GetDashboardAsync(
        Guid jobId,
        string roleName,
        Guid? registrationId = null,
        CancellationToken ct = default);

    /// <summary>
    /// Get live aggregate metrics (registrations, financials, scheduling) for the dashboard hero.
    /// </summary>
    Task<DashboardMetricsDto> GetMetricsAsync(
        Guid jobId,
        CancellationToken ct = default);

    /// <summary>
    /// Get daily registration time-series data for the dashboard trend chart.
    /// </summary>
    Task<RegistrationTimeSeriesDto> GetRegistrationTimeSeriesAsync(
        Guid jobId,
        CancellationToken ct = default);

    /// <summary>
    /// Get daily player registration time-series (Player role only).
    /// </summary>
    Task<RegistrationTimeSeriesDto> GetPlayerTimeSeriesAsync(
        Guid jobId,
        CancellationToken ct = default);

    /// <summary>
    /// Get daily team registration time-series (ClubRep-paid teams).
    /// </summary>
    Task<RegistrationTimeSeriesDto> GetTeamTimeSeriesAsync(
        Guid jobId,
        CancellationToken ct = default);

    /// <summary>
    /// Get player and team counts per age group.
    /// </summary>
    Task<AgegroupDistributionDto> GetAgegroupDistributionAsync(
        Guid jobId,
        CancellationToken ct = default);

    /// <summary>
    /// Get the primary event contact — earliest-registered admin.
    /// </summary>
    Task<EventContactDto?> GetEventContactAsync(
        Guid jobId,
        CancellationToken ct = default);

    /// <summary>
    /// Get year-over-year registration pace comparison across sibling jobs.
    /// </summary>
    Task<YearOverYearComparisonDto> GetYearOverYearAsync(
        Guid jobId,
        CancellationToken ct = default);
}
