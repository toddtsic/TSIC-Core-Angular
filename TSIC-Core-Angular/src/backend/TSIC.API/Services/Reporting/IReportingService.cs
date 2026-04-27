using TSIC.Contracts.Dtos;

namespace TSIC.API.Services.Reporting;

public interface IReportingService
{
    /// <summary>
    /// Returns the Type 2 report catalogue visible to the given job — active rows
    /// from <c>reporting.ReportCatalogue</c> that pass the shared visibility
    /// evaluator against the job's sport / jobtype / customer / feature flags
    /// and the caller's roles (for cross-customer / SuperUser-only reports).
    /// </summary>
    Task<List<ReportCatalogueEntryDto>> GetCatalogueForJobAsync(
        Guid jobId,
        IEnumerable<string> callerRoles,
        CancellationToken cancellationToken = default);

    // -------- SuperUser catalogue editor --------

    Task<List<ReportCatalogueEntryDto>> GetFullCatalogueAsync(
        CancellationToken cancellationToken = default);

    Task<ReportCatalogueEntryDto> CreateCatalogueEntryAsync(
        ReportCatalogueWriteDto dto,
        string lebUserId,
        CancellationToken cancellationToken = default);

    Task<ReportCatalogueEntryDto?> UpdateCatalogueEntryAsync(
        Guid reportId,
        ReportCatalogueWriteDto dto,
        string lebUserId,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteCatalogueEntryAsync(
        Guid reportId,
        CancellationToken cancellationToken = default);

    Task<VerifyStoredProcedureDto> VerifyStoredProcedureAsync(
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
