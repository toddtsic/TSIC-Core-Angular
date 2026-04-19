using System.Data.Common;
using TSIC.Contracts.Dtos;

namespace TSIC.Contracts.Repositories;

public interface IReportingRepository
{
    /// <summary>
    /// Loads all <c>Active = 1</c> rows from <c>reporting.ReportCatalogue</c>,
    /// ordered by SortOrder. Returns the VisibilityRules JSON so the service
    /// layer can apply per-job gating.
    /// </summary>
    Task<List<ReportCatalogueEntryDto>> GetActiveCatalogueEntriesAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads EVERY row (active + inactive) for the SuperUser editor.
    /// </summary>
    Task<List<ReportCatalogueEntryDto>> GetAllCatalogueEntriesAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts a new catalogue row. Sets <c>Modified = GETDATE()</c> and
    /// <c>LebUserId</c> from the caller's identity.
    /// </summary>
    Task<ReportCatalogueEntryDto> CreateCatalogueEntryAsync(
        ReportCatalogueWriteDto dto,
        string lebUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing catalogue row. Returns null if no row matches.
    /// </summary>
    Task<ReportCatalogueEntryDto?> UpdateCatalogueEntryAsync(
        Guid reportId,
        ReportCatalogueWriteDto dto,
        string lebUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Hard-deletes a catalogue row. Returns false if no row matches.
    /// </summary>
    Task<bool> DeleteCatalogueEntryAsync(
        Guid reportId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks <c>OBJECT_ID(@spName, 'P')</c>. Accepts schema-qualified names
    /// (e.g. <c>reporting.RefAssignmentQA</c>). Used by the editor to warn
    /// before saving a catalogue row pointing at a non-existent proc.
    /// </summary>
    Task<bool> StoredProcedureExistsAsync(
        string spName,
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
