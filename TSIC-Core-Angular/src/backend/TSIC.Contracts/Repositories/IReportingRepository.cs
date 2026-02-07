using System.Data.Common;

namespace TSIC.Contracts.Repositories;

public interface IReportingRepository
{
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
