using TSIC.Contracts.Dtos;

namespace TSIC.API.Services.Reporting;

public interface IReportingService
{
    /// <summary>
    /// Returns the reports library visible to the given (job, role-set) — active rows
    /// from <c>reporting.JobReports</c>. Row existence IS the entitlement; no further
    /// gating is applied at this layer.
    /// </summary>
    Task<List<JobReportEntryDto>> GetJobReportsAsync(
        Guid jobId,
        IReadOnlyCollection<string> roleIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Per-row entitlement check for the export-sp endpoint — confirms the caller has
    /// an active stored-proc row in <c>reporting.JobReports</c> for the given spName.
    /// Layered on top of the controller's [Authorize(AdminOnly)] floor.
    /// </summary>
    Task<bool> HasStoredProcedureEntitlementAsync(
        Guid jobId,
        IReadOnlyCollection<string> roleIds,
        string spName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Proxies a Crystal Reports export request to the external CR service.
    /// JobId, RegId, and UserId are derived from JWT claims — never from client parameters.
    /// </summary>
    Task<ReportExportResult> ExportCrystalReportAsync(
        string reportName,
        int exportFormat,
        Guid jobId,
        Guid? regId,
        string userId,
        string? strGids = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a stored procedure and returns the results as an Excel file.
    /// </summary>
    Task<ReportExportResult> ExportStoredProcedureToExcelAsync(
        string spName,
        Guid jobId,
        bool useJobId,
        bool useDateUnscheduled = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes the monthly reconciliation stored procedure and returns Excel.
    /// </summary>
    Task<ReportExportResult> ExportMonthlyReconciliationAsync(
        int settlementMonth,
        int settlementYear,
        bool isMerchandise,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates an iCalendar (.ics) file from selected schedule games.
    /// </summary>
    Task<ReportExportResult> ExportScheduleToICalAsync(
        List<int> gameIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Triggers the external CR service to build last month's job invoices.
    /// </summary>
    Task<bool> BuildLastMonthsJobInvoicesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a report export to the audit trail.
    /// </summary>
    Task RecordExportHistoryAsync(
        Guid? regId,
        string? storedProcedureName,
        string? reportName,
        CancellationToken cancellationToken = default);
}
