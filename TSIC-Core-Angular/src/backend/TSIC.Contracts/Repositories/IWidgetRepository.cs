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
    /// Always returns public widgets (RoleId IS NULL) plus widgets matching the given role.
    /// Pass roleId = null to get public widgets only (anonymous path).
    /// Projects Widget + Category navigations into flat WidgetItemProjection.
    /// </summary>
    Task<List<WidgetItemProjection>> GetDefaultsAsync(
        int jobTypeId,
        string? roleId,
        CancellationToken ct = default);

    /// <summary>
    /// Get per-job widget overrides and additions for a given job and role.
    /// Always returns public widgets (RoleId IS NULL) plus widgets matching the given role.
    /// Pass roleId = null to get public widgets only (anonymous path).
    /// Projects Widget + Category navigations into flat WidgetItemProjection.
    /// </summary>
    Task<List<WidgetItemProjection>> GetJobWidgetsAsync(
        Guid jobId,
        string? roleId,
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

    /// <summary>
    /// Get daily registration counts and revenue grouped by RegistrationTs date.
    /// Returns raw daily data; cumulative totals are computed server-side in the service layer.
    /// </summary>
    Task<RegistrationTimeSeriesDto> GetRegistrationTimeSeriesAsync(Guid jobId, CancellationToken ct = default);

    /// <summary>
    /// Get daily player registration time-series (RoleId = Player, active only).
    /// Revenue reflects player-paid registrations (FeeTotal > 0).
    /// </summary>
    Task<RegistrationTimeSeriesDto> GetPlayerTimeSeriesAsync(Guid jobId, CancellationToken ct = default);

    /// <summary>
    /// Get daily team registration time-series (ClubRepRegistrationId valued, active only).
    /// Revenue reflects team-level fees (Teams.PaidTotal).
    /// </summary>
    Task<RegistrationTimeSeriesDto> GetTeamTimeSeriesAsync(Guid jobId, CancellationToken ct = default);

    /// <summary>
    /// Get player and team counts per age group for a job.
    /// Players counted via AssignedTeamId → Team.AgegroupId; teams via Teams.AgegroupId.
    /// </summary>
    Task<AgegroupDistributionDto> GetAgegroupDistributionAsync(Guid jobId, CancellationToken ct = default);

    /// <summary>
    /// Get the primary event contact — the earliest-registered administrator for a job.
    /// Returns null if no admin registration exists.
    /// </summary>
    Task<EventContactDto?> GetEventContactAsync(Guid jobId, CancellationToken ct = default);

    /// <summary>
    /// Get year-over-year registration pace comparison for sibling jobs
    /// (same customer + type + sport + season, different years).
    /// Returns up to 4 most recent years.
    /// </summary>
    Task<YearOverYearComparisonDto> GetYearOverYearAsync(Guid currentJobId, CancellationToken ct = default);

    /// <summary>
    /// Portfolio table for the JobRegCountsAndDollars widget: every LIVE job
    /// (ExpiryUsers > now) belonging to the same customer as <paramref name="currentJobId"/>,
    /// with both count units and the registration ledger totals, plus a rollup.
    /// </summary>
    Task<JobRegCountsAndDollarsDto> GetJobRegCountsAndDollarsAsync(
        Guid currentJobId,
        CancellationToken ct = default);

    /// <summary>
    /// Names for the jobs owned by the same customer as <paramref name="currentJobId"/>,
    /// restricted to <paramref name="jobIds"/>.
    ///
    /// This is the SCOPE GATE for the UsageStatsPerJob widget, not a convenience lookup.
    /// The usage aggregate comes from TSICLogs and is not customer-filtered -- it cannot
    /// be, the two databases do not join -- so a job id that survives this call is a job
    /// the caller is entitled to see, and one that does not is silently dropped. Do not
    /// "helpfully" fall back to naming ids outside the customer.
    ///
    /// Unlike the portfolio widget this does NOT filter on ExpiryUsers: usage is a
    /// historical record, and a window that reaches back before an event ended should
    /// still show the traffic that event received.
    /// </summary>
    Task<Dictionary<Guid, string>> GetCustomerJobNamesAsync(
        Guid currentJobId,
        IReadOnlyCollection<Guid> jobIds,
        CancellationToken ct = default);

}
