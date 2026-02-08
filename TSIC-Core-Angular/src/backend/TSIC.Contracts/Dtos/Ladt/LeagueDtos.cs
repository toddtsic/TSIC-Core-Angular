namespace TSIC.Contracts.Dtos.Ladt;

public record LeagueDetailDto
{
    public required Guid LeagueId { get; init; }
    public required string LeagueName { get; init; }
    public required Guid SportId { get; init; }
    public string? SportName { get; init; }
    public required bool BAllowCoachScoreEntry { get; init; }
    public required bool BHideContacts { get; init; }
    public required bool BHideStandings { get; init; }
    public required bool BShowScheduleToTeamMembers { get; init; }
    public required bool BTakeAttendance { get; init; }
    public required bool BTrackPenaltyMinutes { get; init; }
    public required bool BTrackSportsmanshipScores { get; init; }
    public string? RescheduleEmailsToAddon { get; init; }
    public decimal? PlayerFeeOverride { get; init; }
    public int? StandingsSortProfileId { get; init; }
    public int? PointsMethod { get; init; }
    public string? StrLop { get; init; }
    public string? StrGradYears { get; init; }
}

public record UpdateLeagueRequest
{
    public required string LeagueName { get; init; }
    public required Guid SportId { get; init; }
    public required bool BAllowCoachScoreEntry { get; init; }
    public required bool BHideContacts { get; init; }
    public required bool BHideStandings { get; init; }
    public required bool BShowScheduleToTeamMembers { get; init; }
    public required bool BTakeAttendance { get; init; }
    public required bool BTrackPenaltyMinutes { get; init; }
    public required bool BTrackSportsmanshipScores { get; init; }
    public string? RescheduleEmailsToAddon { get; init; }
    public decimal? PlayerFeeOverride { get; init; }
    public int? StandingsSortProfileId { get; init; }
    public int? PointsMethod { get; init; }
    public string? StrLop { get; init; }
    public string? StrGradYears { get; init; }
}
