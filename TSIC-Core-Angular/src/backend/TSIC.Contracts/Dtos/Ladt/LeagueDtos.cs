namespace TSIC.Contracts.Dtos.Ladt;

public record LeagueDetailDto
{
    public required Guid LeagueId { get; init; }
    public required string LeagueName { get; init; }
    public required Guid SportId { get; init; }
    public string? SportName { get; init; }
    public required bool BHideContacts { get; init; }
    public required bool BHideStandings { get; init; }
    public string? RescheduleEmailsToAddon { get; init; }
    /// <summary>Standings tiebreaker profile; null = engine default (points → goal diff → goals for).</summary>
    public int? StandingsSortProfileId { get; init; }
}

public record UpdateLeagueRequest
{
    public required string LeagueName { get; init; }
    public required Guid SportId { get; init; }
    public required bool BHideContacts { get; init; }
    public required bool BHideStandings { get; init; }
    public string? RescheduleEmailsToAddon { get; init; }
    public int? StandingsSortProfileId { get; init; }
}

/// <summary>Dropdown option for a league's standings tiebreaker profile, with its ordered
/// rule chain (descriptions when present, else rule names) for display under the select.</summary>
public record StandingsSortProfileOptionDto
{
    public required int StandingsSortProfileId { get; init; }
    public required string StandingsSortProfileName { get; init; }
    public required IReadOnlyList<string> Rules { get; init; }
}

public record SportOptionDto
{
    public required Guid SportId { get; init; }
    public required string SportName { get; init; }
}

/// <summary>
/// Create the FIRST league on a job that has none — the empty-state mini-form in the LADT
/// editor. SuperUser-only: league creation is a job build-out step (a clone taken with
/// LadtScope "none" lands leagueless), never a director action.
///
/// The sport is the JOB's sport, not a per-league choice: <c>Jobs.SportId</c> is what
/// TextSubstitution tokens and related-job matching read, while <c>Leagues.SportId</c> is
/// read only to display and edit itself in the league pane. The form preselects the job's
/// value and the create writes the operator's selection to BOTH, so a job whose sport was
/// never set gets it filled in here.
/// </summary>
public record CreateLeagueRequest
{
    public required string LeagueName { get; init; }
    public required Guid SportId { get; init; }
}
