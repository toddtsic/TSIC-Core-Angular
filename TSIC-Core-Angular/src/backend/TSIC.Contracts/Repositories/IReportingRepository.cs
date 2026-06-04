using System.Data.Common;
using TSIC.Contracts.Dtos;
using TSIC.Domain.Entities;

namespace TSIC.Contracts.Repositories;

public interface IReportingRepository
{
    /// <summary>
    /// Loads the active rows from <c>reporting.JobReports</c> visible to the given
    /// (job, role-set) — i.e. one row per report the caller is entitled to run.
    /// The reports library UI is the sole consumer; gating is by row existence
    /// (no separate visibility-rules evaluator anymore).
    /// </summary>
    Task<List<JobReportEntryDto>> GetJobReportsAsync(
        Guid jobId,
        IReadOnlyCollection<string> roleIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// SuperUser view: every active row for the job across ALL roles, each tagged with
    /// its assigned <c>RoleName</c>. No role filter — the SU reports library shows all
    /// roles' reports so role assignment is visible (and dedup happens in the UI).
    /// </summary>
    Task<List<JobReportEntryDto>> GetAllActiveJobReportsAsync(
        Guid jobId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns true when <paramref name="spName"/> is bound to an active stored-proc
    /// row in <c>reporting.JobReports</c> for the given (job, role-set). Used by the
    /// export-sp endpoint as a per-row authorization check on top of [Authorize(AdminOnly)].
    /// </summary>
    Task<bool> HasStoredProcedureEntitlementAsync(
        Guid jobId,
        IReadOnlyCollection<string> roleIds,
        string spName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// SuperUser variant: confirms <paramref name="spName"/> is an active stored-proc
    /// report configured for the job under ANY role (no caller-role scoping). Lets SU
    /// run any report visible in its all-roles catalogue while still requiring the SP
    /// to be a real configured report (not an arbitrary proc).
    /// </summary>
    Task<bool> HasStoredProcedureEntitlementAnyRoleAsync(
        Guid jobId,
        string spName,
        CancellationToken cancellationToken = default);

    // ── SuperUser editor (per-Job, per-Role) ──

    /// <summary>
    /// Roles that have at least one row in <c>reporting.JobReports</c> for the given
    /// job — drives the editor's role-picker dropdown.
    /// </summary>
    Task<List<JobReportEditorRoleDto>> GetEditorRolesAsync(
        Guid jobId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// All editor rows for a (Job, Role), ordered for grid display. Includes audit
    /// fields (Modified, LebUserId) absent from <see cref="JobReportEntryDto"/>.
    /// </summary>
    Task<List<JobReportEditorRowDto>> GetEditorRowsAsync(
        Guid jobId,
        string roleId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads a tracked <c>reporting.JobReports</c> row for SU update. Service layer
    /// applies the JobId-match guard before mutating.
    /// </summary>
    Task<JobReports?> GetJobReportForUpdateAsync(
        Guid jobReportId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts a new <c>reporting.JobReports</c> row. Caller (service layer) is
    /// responsible for setting JobId from JWT and validating RoleId.
    /// </summary>
    Task<JobReports> AddJobReportAsync(
        JobReports entity,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists tracked-entity changes queued by <see cref="GetJobReportForUpdateAsync"/>.
    /// </summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>Per-role entitlement check for the export-bold endpoint.</summary>
    Task<bool> HasBoldReportEntitlementAsync(
        Guid jobId,
        IReadOnlyCollection<string> roleIds,
        string reportName,
        CancellationToken cancellationToken = default);

    /// <summary>SuperUser variant of the export-bold entitlement check.</summary>
    Task<bool> HasBoldReportEntitlementAnyRoleAsync(
        Guid jobId,
        string reportName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a stored procedure and returns a DbDataReader for streaming results.
    /// Caller is responsible for closing the reader and connection.
    /// </summary>
    Task<(DbDataReader Reader, DbConnection Connection)> ExecuteStoredProcedureAsync(
        string spName,
        Guid jobId,
        bool useJobId,
        bool useDateUnscheduled = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes the monthly reconciliation stored procedure.
    /// </summary>
    Task<(DbDataReader Reader, DbConnection Connection)> ExecuteMonthlyReconciliationAsync(
        int settlementMonth,
        int settlementYear,
        bool isMerchandise,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a report export to the audit trail.
    /// </summary>
    Task RecordExportHistoryAsync(
        Guid registrationId,
        string? storedProcedureName,
        string? reportName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets schedule games with field data for iCal export.
    /// </summary>
    Task<List<ScheduleGameForICalDto>> GetScheduleGamesForICalAsync(
        List<int> gameIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Raw per-registrant rows for the tournament roster family (packed roster + recruiter
    /// report) — the EF replacement for <c>reporting_migrate.TournamentRosterPacked_Flat</c>.
    /// Scope mirrors the proc: active Staff/Player on active, schedule-listed teams, excluding
    /// WAITLIST/DROPPED agegroups. Returns the unshaped superset; the PDF layer owns all
    /// display shaping, ordering, and last-row detection.
    /// </summary>
    Task<List<TournamentRosterRowDto>> GetTournamentRosterRowsAsync(
        Guid jobId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Flattened schedule + field data for iCal generation.
/// </summary>
public record ScheduleGameForICalDto
{
    public required int Gid { get; init; }
    public DateTime? GDate { get; init; }
    public string? T1Name { get; init; }
    public string? T2Name { get; init; }
    public string? FieldName { get; init; }
    public string? Address { get; init; }
    public string? City { get; init; }
    public string? State { get; init; }
    public string? Zip { get; init; }
}
