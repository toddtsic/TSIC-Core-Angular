using TSIC.Contracts.Dtos;

namespace TSIC.API.Services.Reporting;

/// <summary>
/// Hand-drawn (Syncfusion.Pdf) renderer for the two American Select reports — the EF replacement for
/// the legacy Crystal "AmericanSelectEvaluation" (<c>reporting.AmericanSelectPlayerData</c>) and
/// "AmericanSelectMainEventRosters" (master-detail proc pair
/// <c>…MainEvent_Teams</c> + <c>…_TeamRoster</c>). Both job-scoped.
/// </summary>
public interface IAmericanSelectReportPdfService
{
    /// <summary>Tryout evaluation sheet: one row per tryout player, grouped by agegroup, with a
    /// check-in box and mom contact (landscape).</summary>
    Task<ReportExportResult> GenerateEvaluationAsync(Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>Main event rosters: per-team roster cards grouped agegroup → team (portrait).</summary>
    Task<ReportExportResult> GenerateMainEventRostersAsync(Guid jobId, CancellationToken cancellationToken = default);
}
