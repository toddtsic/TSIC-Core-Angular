namespace TSIC.Contracts.Dtos;

/// <summary>
/// A single event appearance for a club team, with aggregated results.
/// </summary>
public sealed record ClubTeamEventSummaryDto
{
    public required Guid TeamId { get; init; }
    public required Guid JobId { get; init; }
    public required string JobName { get; init; }
    public required string JobPath { get; init; }
    public required string AgegroupName { get; init; }
    public required string? DivisionName { get; init; }
    public required DateTime? EventStartDate { get; init; }
    public required int? Wins { get; init; }
    public required int? Losses { get; init; }
    public required int? Ties { get; init; }
    public required int? GoalsFor { get; init; }
    public required int? GoalsVs { get; init; }
    public required int? GamesPlayed { get; init; }
    public required int? StandingsRank { get; init; }
}

/// <summary>
/// A club team from the library with its full event history across all TSIC events.
/// </summary>
public sealed record ClubTeamLibraryEntryDto
{
    public required int ClubTeamId { get; init; }
    public required string ClubTeamName { get; init; }
    public required string ClubTeamGradYear { get; init; }
    public required string? ClubTeamLevelOfPlay { get; init; }
    public required bool Active { get; init; }
    public required List<ClubTeamEventSummaryDto> EventHistory { get; init; }
}

/// <summary>
/// Full team library for a club, including all teams and their cross-event history.
/// </summary>
public sealed record ClubTeamLibraryResponse
{
    public required int ClubId { get; init; }
    public required string ClubName { get; init; }
    public required List<ClubTeamLibraryEntryDto> Teams { get; init; }
}
