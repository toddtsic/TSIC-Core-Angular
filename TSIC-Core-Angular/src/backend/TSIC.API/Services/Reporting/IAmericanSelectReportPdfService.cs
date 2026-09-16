using TSIC.Contracts.Dtos;

namespace TSIC.API.Services.Reporting;

/// <summary>
/// Hand-drawn (Syncfusion.Pdf) renderer for the American Select Evaluation and Tryout Check-In
/// reports — the EF replacements for the legacy Crystal "AmericanSelectEvaluation" and
/// "AmericanSelectTournyCheckin" (both fed by <c>reporting.AmericanSelectPlayerData</c>). Job-scoped.
/// (Main Event Rosters are now served by the shared PackedRoster engine — the offer-team
/// rosters are just a packed roster — so there's no bespoke renderer for them here.)
/// </summary>
public interface IAmericanSelectReportPdfService
{
    /// <summary>Evaluator scoring sheet (portrait): grouped by tryout team (page break per team,
    /// team name as subtitle) then by position, one row per player with five blank write-in
    /// score boxes (Physical / PsnSpecific / StickSkills / Notes / Total).</summary>
    Task<ReportExportResult> GenerateEvaluationAsync(Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>Tryout check-in sheet (portrait): one section per tryout team (page break per team,
    /// "{Job}:{GradYear} Tryout Players" title on every page), one row per player sorted by name,
    /// with a blank check-off box and #, GradYr, Position, Player, Club, School, Mom columns.</summary>
    Task<ReportExportResult> GenerateTournyCheckinAsync(Guid jobId, CancellationToken cancellationToken = default);
}
