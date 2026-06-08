using TSIC.Contracts.Dtos;

namespace TSIC.API.Services.Reporting;

/// <summary>
/// Hand-drawn (Syncfusion.Pdf) renderer for the Game Boards report (Crystal "Schedule_ByAgegroup").
/// A blank game-day scoring board: per agegroup → division, a standings write-in box (Wins / Losses /
/// Ties / Goals Against) over the division's teams, then each game as a Date / Time / Field row with
/// the home (right) and away (left) teams flanking two blank score boxes. Bracket games (seed slots,
/// no assigned team) collect into a per-agegroup "Championship Round" section with no standings.
/// EF data: <see cref="IReportingRepository.GetScheduleListGamesAsync"/> +
/// <see cref="IReportingRepository.GetScheduleStandingsTeamsAsync"/>.
/// </summary>
public interface IGameBoardsPdfService
{
    Task<ReportExportResult> GenerateAsync(Guid jobId, CancellationToken cancellationToken = default);
}
