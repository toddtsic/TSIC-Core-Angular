using TSIC.Contracts.Dtos.Widgets;

namespace TSIC.Contracts.Services;

/// <summary>
/// TSIC-TEAMS app usage for one event, for the TeamsAppUsage dashboard widget.
///
/// Two databases, merged here: TSICV5 names the population (rostered players and staff on
/// the event's active teams), TSICLogs says who among them used the app on which days.
/// They are matched by registration id, never joined in SQL.
/// </summary>
public interface ITeamsAppUsageService
{
    /// <summary>
    /// Usage over the last <paramref name="windowDays"/> calendar days including today.
    /// The job always comes from the caller's token, never from the request.
    /// </summary>
    Task<TeamsAppUsageDto> GetTeamsAppUsageAsync(Guid jobId, int windowDays, CancellationToken ct = default);
}
