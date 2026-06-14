using TSIC.Contracts.Dtos;

namespace TSIC.API.Services.Reporting;

/// <summary>
/// One-off Syncfusion PDF renders for two American Select showcase schedule reports not covered
/// by the shared Schedule List Designer. Both run off <c>GetScheduleListGamesAsync</c>.
/// </summary>
public interface IShowcaseScheduleReportService
{
    /// <summary>
    /// FieldUtilizationWithNominations — games grouped by date+field with a boxed score and a
    /// blank "Player Nominations" write-in grid per game (nominations recorded by hand).
    /// </summary>
    Task<ReportExportResult> GenerateFieldUtilizationNominationsAsync(
        Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// ScheduleByClubAgTPerPage — one page per team listing that team's games (a game prints on
    /// both teams' pages).
    /// </summary>
    Task<ReportExportResult> GenerateScheduleByTeamAsync(
        Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Schedule_Gamecards — 2-up blank score cards grouped by field; each card carries the
    /// field/date/time, agegroup/division, both team names, and a blank write-in Score box per team.
    /// </summary>
    Task<ReportExportResult> GenerateGameCardsAsync(
        Guid jobId, CancellationToken cancellationToken = default);
}
