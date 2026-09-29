namespace TSIC.Contracts.Repositories;

/// <summary>
/// Reads for the TSIC-Teams Schedules tab: which tournaments a club-job team's club is
/// scheduled in, and which of the club's teams are in one of them.
///
/// A tournament team belongs to a club by its in-event club name -- the club rep
/// registration's <c>club_name</c>, the same route the view-schedule club filter takes. No
/// library ClubId, no stored team link.
/// </summary>
public interface ITeamTournamentsRepository
{
    /// <summary>The app-side (club-job) team. Null for an unknown team.</summary>
    Task<TeamTournamentsContext?> GetTeamContextAsync(Guid teamId, CancellationToken ct = default);

    /// <summary>
    /// Jobs other than <paramref name="excludeJobId"/> where an active team registered under
    /// <paramref name="clubName"/> is in a game (T1 or T2), whose schedule is public, and which
    /// have a game dated <paramref name="today"/> or later. <paramref name="onlyJobId"/> narrows
    /// to one job. The bulletin announce test is NOT applied here -- it is a regex over bulletin
    /// text and runs in the service.
    /// </summary>
    Task<List<ClubTournamentRow>> GetUpcomingPublicTournamentsForClubAsync(
        string clubName, Guid excludeJobId, DateTime today, Guid? onlyJobId = null, CancellationToken ct = default);

    /// <summary>Active teams registered under <paramref name="clubName"/> that are in a game in the job.</summary>
    Task<List<ClubTournamentTeamRow>> GetClubScheduledTeamsAsync(
        Guid tournamentJobId, string clubName, CancellationToken ct = default);
}

public record TeamTournamentsContext
{
    public required Guid TeamId { get; init; }
    public required Guid JobId { get; init; }
    public required string TeamName { get; init; }

    /// <summary>The team's gender, falling back to its age group's.</summary>
    public string? Gender { get; init; }

    public string? AgegroupName { get; init; }
    public string? CustomerName { get; init; }

    /// <summary><c>teamevents.JobFeatures.ScheduleEnabled</c>; no row means off.</summary>
    public required bool ScheduleEnabled { get; init; }
}

public record ClubTournamentRow
{
    public required Guid JobId { get; init; }
    public required string JobName { get; init; }
    public required DateTime NextGameDate { get; init; }
}

public record ClubTournamentTeamRow
{
    public required Guid TeamId { get; init; }
    public required string TeamName { get; init; }
    public string? AgegroupName { get; init; }
}
