using TSIC.Contracts.Dtos;

namespace TSIC.API.Services.Reporting;

/// <summary>
/// Hand-drawn (Syncfusion.Pdf) renderer for the daily registration-counts report — the EF
/// replacement for the legacy Crystal "JobPlayers_TSICDaily" (proc
/// <c>reporting.Get_Registrations_TSIC_Today</c>). Cross-job/public ops report: today's active
/// registrations and the running to-date total, grouped Customer → Job → role.
/// </summary>
public interface IDailyRegCountsPdfService
{
    /// <summary>
    /// Renders the daily registration-counts PDF for the server-local "today". No job scoping —
    /// the report spans every job that took a registration today.
    /// </summary>
    Task<ReportExportResult> GenerateAsync(CancellationToken cancellationToken = default);
}
