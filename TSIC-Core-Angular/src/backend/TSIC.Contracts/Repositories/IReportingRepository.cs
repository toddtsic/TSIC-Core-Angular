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

    /// <summary>
    /// One flat row per scheduled game for the Schedule List Designer — the EF replacement for
    /// <c>reporting_migrate.ScheduleList_Flat</c>. Mirrors the proc: inner joins on agegroup
    /// (color), league (name), and field (name) — so games with no assigned field are excluded,
    /// as in the proc — plus a left club-rep chain per side. Denormalized AgegroupName/DivName
    /// and team names ride straight off <c>Schedule</c>; the PDF layer applies the TBD-slot
    /// bracket labels and score/rep-name shaping.
    /// </summary>
    Task<List<ScheduleListGameDto>> GetScheduleListGamesAsync(
        Guid jobId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// One flat row per active, team-assigned registrant for the Roster Table Designer — the EF
    /// replacement for the wide-roster Crystal family (Club Rosters, No-Medical, Coaches,
    /// WithClubRep, STEPS, Recruiting roster). Scope mirrors those procs: bActive=1 registrants on
    /// active teams, NOT schedule-gated (unlike the tournament packed query). <paramref name="playersOnly"/>
    /// restricts to the Player role (recruiting / STEPS); otherwise Staff + Player are both returned
    /// (club rosters). Returns the unshaped superset; the PDF layer picks/orders/formats columns.
    /// </summary>
    Task<List<RosterTableRowDto>> GetRosterTableRowsAsync(
        Guid jobId,
        bool playersOnly,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Per-(customer, job, role) registration counts for every job that took at least one active
    /// registration on <paramref name="asOfLocal"/>'s date — the EF replacement for
    /// <c>reporting.Get_Registrations_TSIC_Today</c> (legacy Crystal "JobPlayers_TSICDaily").
    /// Mirrors the proc: inner joins Job→Customer, role, and user, filters bActive=1, and reports
    /// today's count plus the running active to-date total for each combo. Cross-job (no jobId
    /// scoping); only combos with same-day activity appear, exactly as the proc's @t-join did.
    /// </summary>
    Task<List<DailyRegCountRowDto>> GetDailyRegCountsAsync(
        DateTime asOfLocal,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Flat per-payment lines for the monthly client-invoice report — the EF replacement for
    /// <c>adn.rpt_invoice</c> (legacy Crystal "invoices2015" / "invoices2015SummariesOnly").
    /// Concatenates the proc's player + team UNION branches for the given settlement month,
    /// querying the base <c>adn.Txs</c> table directly (NOT the <c>adn.vTxs</c> view) and leaving
    /// ADN's text settlement date/amount RAW (year/month filtered from fixed substring positions,
    /// no datetime coercion / schema change). Each line carries its job's fee rates and that
    /// month's <c>adn.Monthly_Job_Stats</c> counts denormalized, so the whole report renders from
    /// this one flat set (no subreport). All money parsing, credit negation, CC-fee computation,
    /// and per-venue summary aggregation happen in the service layer.
    /// </summary>
    Task<List<InvoiceLineRawDto>> GetInvoiceLinesAsync(
        int settlementYear,
        int settlementMonth,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Per-(year, month, customer, job) TSIC-fee rows for the YTD comparison reports — the EF
    /// replacement for <c>adn.tsicFeesYTDAndLastYear</c> (legacy Crystal "tsicTSICFeesYTD" /
    /// "...ByCustomer"). Returns months 1..lastMonth for both this year and last year (last month
    /// derived from <paramref name="asOfLocal"/>), each row's fee = NewPlayers×perPlayerCharge +
    /// NewTeams×perTeamCharge off <c>adn.Monthly_Job_Stats</c>. Mirrors the proc's
    /// <c>isnumeric(Jobs.year)=1</c> guard (jobs with a numeric text year only).
    /// </summary>
    Task<List<FeeYtdRowDto>> GetFeeYtdRowsAsync(
        DateTime asOfLocal,
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
