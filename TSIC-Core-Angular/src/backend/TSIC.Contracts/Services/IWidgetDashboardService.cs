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
    /// Pass roleName = null for the anonymous/public path — only public widgets
    /// (WidgetDefault/JobWidget rows with RoleId IS NULL) are returned.
    /// When registrationId is provided, applies per-user customizations (3rd merge layer).
    /// </summary>
    Task<WidgetDashboardResponse> GetDashboardAsync(
        Guid jobId,
        string? roleName,
        Guid? registrationId = null,
        CancellationToken ct = default);

    /// <summary>
    /// True when this job + role has at least one DASHBOARD-workspace widget after the
    /// platform defaults and the per-job overrides are merged — i.e. whether the dashboard
    /// would render anything at all. Gates the doors INTO the dashboard, so it must stay
    /// cheap: it counts rows, it does not assemble the dashboard.
    ///
    /// Counts merge layers 1+2 ONLY (WidgetDefault, JobWidget). Layer 3 is the per-user
    /// UserWidget hide list, and folding it in would let an admin who hid every widget
    /// lock themselves out of the Customize dialog that is the only way to unhide.
    /// </summary>
    Task<bool> HasDashboardWidgetsAsync(
        Guid jobId,
        string roleName,
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

    /// <summary>
    /// Portfolio table for the JobRegCountsAndDollars widget — every LIVE job of the
    /// caller current customer, with counts for both units and the ledger totals.
    /// </summary>
    Task<JobRegCountsAndDollarsDto> GetJobRegCountsAndDollarsAsync(
        Guid currentJobId,
        CancellationToken ct = default);

}
