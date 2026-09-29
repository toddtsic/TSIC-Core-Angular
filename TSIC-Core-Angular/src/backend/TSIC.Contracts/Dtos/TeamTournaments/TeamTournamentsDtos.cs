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
/// Result of matching the app's team to the club's teams in one tournament. Exactly one of the
/// two outcomes: <see cref="MatchedTeamId"/> set and <see cref="Candidates"/> empty, or
/// <see cref="MatchedTeamId"/> null and <see cref="Candidates"/> holding the teams to pick from.
/// Carries the tournament ids back so the next call is built from this response alone.
/// </summary>
public record TeamTournamentMatchDto
{
    public required Guid TournamentJobId { get; init; }
    public required string TournamentJobName { get; init; }

    /// <summary>The single matched TOURNAMENT team, or null when the user must pick.</summary>
    public Guid? MatchedTeamId { get; init; }

    public required List<TournamentTeamOptionDto> Candidates { get; init; }
}

/// <summary>A club team in the tournament, named as the tournament names it.</summary>
public record TournamentTeamOptionDto
{
    public required Guid TournamentTeamId { get; init; }
    public required string TeamName { get; init; }
}
