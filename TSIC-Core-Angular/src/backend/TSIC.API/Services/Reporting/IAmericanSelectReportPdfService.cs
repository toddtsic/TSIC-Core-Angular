using TSIC.Contracts.Dtos;

namespace TSIC.API.Services.Reporting;

/// <summary>
/// Hand-drawn (Syncfusion.Pdf) renderer for the American Select Evaluation report — the EF
/// replacement for the legacy Crystal "AmericanSelectEvaluation"
/// (<c>reporting.AmericanSelectPlayerData</c>). Job-scoped.
/// (Main Event Rosters are now served by the shared PackedRoster engine — the offer-team
/// rosters are just a packed roster — so there's no bespoke renderer for them here.)
/// </summary>
public interface IAmericanSelectReportPdfService
{
    /// <summary>Evaluator scoring sheet (portrait): grouped by tryout team (page break per team,
    /// team name as subtitle) then by position, one row per player with five blank write-in
    /// score boxes (Physical / PsnSpecific / StickSkills / Notes / Total).</summary>
    Task<ReportExportResult> GenerateEvaluationAsync(Guid jobId, CancellationToken cancellationToken = default);
}
