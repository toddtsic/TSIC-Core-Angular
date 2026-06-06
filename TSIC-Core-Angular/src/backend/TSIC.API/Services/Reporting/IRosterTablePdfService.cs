using TSIC.Contracts.Dtos;

namespace TSIC.API.Services.Reporting;

/// <summary>
/// Hand-drawn (Syncfusion.Pdf) renderer for the director-built Roster Table Designer — a
/// full-width, column-pickable per-player roster. One broad EF query (the registrant field
/// universe) + a render config replaces the wide-roster Crystal family (Club Rosters,
/// No-Medical, Coaches, WithClubRep, STEPS, Recruiting roster): no RDL, no Crystal.
/// </summary>
public interface IRosterTablePdfService
{
    /// <summary>
    /// Static metadata for the columns the Designer can place. Drives the frontend field
    /// picker so the available pool is never hard-coded client-side.
    /// </summary>
    IReadOnlyList<RosterTableFieldDto> GetAvailableFields();

    /// <summary>
    /// Renders the roster-table PDF for a job from the given Designer config.
    /// </summary>
    Task<ReportExportResult> GenerateAsync(
        RosterTableRequestDto request,
        Guid jobId,
        CancellationToken cancellationToken = default);
}
