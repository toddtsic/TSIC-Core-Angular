using TSIC.Contracts.Dtos;

namespace TSIC.API.Services.Reporting;

/// <summary>
/// Hand-drawn (Syncfusion.Pdf) renderer for the director-built Schedule List Designer.
/// Replaces the canned Schedule_ExportExcel report family (ScheduleMaster, ScheduleByDay,
/// FieldUtilization*, Schedule_Export) plus the Score_Input blank-score sheet — one stored
/// proc (the full game-field universe) + a render config, no RDL, no Crystal.
/// </summary>
public interface IScheduleListReportService
{
    /// <summary>
    /// Static metadata for the columns the Designer can place. Drives the frontend field
    /// picker so the available pool is never hard-coded client-side.
    /// </summary>
    IReadOnlyList<ScheduleListFieldDto> GetAvailableFields();

    /// <summary>
    /// Renders the schedule-list PDF for a job from the given Designer config.
    /// </summary>
    Task<ReportExportResult> GenerateAsync(
        ScheduleListRequestDto request,
        Guid jobId,
        CancellationToken cancellationToken = default);
}
