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
    public decimal? PlayerFeeOverride { get; init; }
}

public record UpdateLeagueRequest
{
    public required string LeagueName { get; init; }
    public required Guid SportId { get; init; }
    public required bool BHideContacts { get; init; }
    public required bool BHideStandings { get; init; }
    public string? RescheduleEmailsToAddon { get; init; }
    public decimal? PlayerFeeOverride { get; init; }
}

public record SportOptionDto
{
    public required Guid SportId { get; init; }
    public required string SportName { get; init; }
}
