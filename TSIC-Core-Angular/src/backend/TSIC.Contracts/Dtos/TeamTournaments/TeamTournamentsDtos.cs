namespace TSIC.Contracts.Dtos.TeamTournaments;

/// <summary>
/// One tournament on the TSIC-Teams Schedules tab. <see cref="TournamentJobId"/> is the
/// TOURNAMENT's job -- never the club job the app is signed into.
/// </summary>
public record TeamTournamentDto
{
    public required Guid TournamentJobId { get; init; }
    public required string TournamentJobName { get; init; }

    /// <summary>Earliest game in the tournament dated today or later. The list is sorted on it.</summary>
    public required DateTime NextGameDate { get; init; }
}

/// <summary>
/// The club's teams in one tournament, with the app's team matched among them if it can be.
/// <see cref="Teams"/> is ALWAYS the club's full scheduled list -- the user can flip between
/// them. <see cref="MatchedTeamId"/>, when set, is one of <see cref="Teams"/> to preselect.
/// Carries the tournament ids back so the next call is built from this response alone.
/// </summary>
public record TeamTournamentMatchDto
{
    public required Guid TournamentJobId { get; init; }
    public required string TournamentJobName { get; init; }

    /// <summary>The single good match to preselect, or null when nothing matched cleanly.</summary>
    public Guid? MatchedTeamId { get; init; }

    /// <summary>Every club team scheduled in the tournament, sorted by name.</summary>
    public required List<TournamentTeamOptionDto> Teams { get; init; }
}

/// <summary>A club team in the tournament, named as the tournament names it.</summary>
public record TournamentTeamOptionDto
{
    public required Guid TournamentTeamId { get; init; }
    public required string TeamName { get; init; }
}
