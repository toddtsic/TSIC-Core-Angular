using TSIC.Contracts.Dtos.Scheduling;
using TSIC.Contracts.Dtos.TeamTournaments;

namespace TSIC.Contracts.Services;

/// <summary>
/// The TSIC-Teams Schedules tab: a club-job team's tournament schedules, found at read time by
/// club name. Nothing is stored linking the team to any event.
///
/// The controller owns AUTHORIZATION (cross-job, reach). This service owns the ENABLEMENT gate
/// and the four AVAILABILITY gates, and re-runs the latter on every call -- a director can pull
/// a schedule or its bulletin while the app is open.
///
/// Null from the tournament-scoped methods means "not available" and nothing more: unknown
/// tournament, gate failed, or a team that is not the club's in it all look the same.
/// </summary>
public interface ITeamTournamentsService
{
    /// <summary><c>teamevents.JobFeatures.ScheduleEnabled</c> for the team's club job.</summary>
    Task<bool> IsEnabledAsync(Guid teamId, CancellationToken ct = default);

    /// <summary>Tournaments passing every gate, soonest next game first. Empty for an unmapped club.</summary>
    Task<List<TeamTournamentDto>> GetTournamentsAsync(Guid teamId, CancellationToken ct = default);

    Task<TeamTournamentMatchDto?> MatchAsync(Guid teamId, Guid tournamentJobId, CancellationToken ct = default);

    /// <summary>
    /// The tournament team's games -- the exact call TSIC-Events makes for a team filter, with
    /// the team filter set here and nothing else passed but paging.
    /// </summary>
    Task<(List<ViewGameDto> Games, int TotalCount)?> GetGamesAsync(
        Guid teamId, Guid tournamentJobId, Guid tournamentTeamId, int? skip, int? take, CancellationToken ct = default);
}
