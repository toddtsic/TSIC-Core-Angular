using TSIC.Contracts.Dtos;

namespace TSIC.API.Services.Reporting;

/// <summary>
/// Hand-drawn (Syncfusion.Pdf) renderer for the E120 player-stats entry form — the EF replacement for
/// the legacy Crystal "PlayerStats_E120", backed by <c>reporting.PlayerStats_E120</c>. Job-scoped;
/// active Players grouped agegroup → team with write-in cells for the four athletic-combine stats
/// (fastest shot, 5-10-5, 40-yd dash, 300 shuttle), pre-filled where a value already exists.
/// </summary>
public interface IPlayerStatsReportPdfService
{
    Task<ReportExportResult> GenerateE120Async(Guid jobId, CancellationToken cancellationToken = default);
}
