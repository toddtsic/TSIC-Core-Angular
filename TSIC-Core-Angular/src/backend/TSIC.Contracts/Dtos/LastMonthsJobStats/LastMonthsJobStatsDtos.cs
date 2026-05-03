namespace TSIC.Contracts.Dtos.LastMonthsJobStats;

public record LastMonthsJobStatRowDto
{
    public required int Aid { get; init; }
    public required string CustomerName { get; init; }
    public required string JobName { get; init; }
    public required int CountActivePlayersToDate { get; init; }
    public required int CountActivePlayersToDateLastMonth { get; init; }
    public required int CountNewPlayersThisMonth { get; init; }
    public required int CountActiveTeamsToDate { get; init; }
    public required int CountActiveTeamsToDateLastMonth { get; init; }
    public required int CountNewTeamsThisMonth { get; init; }
}

public record UpdateLastMonthsJobStatRequest
{
    public required int CountActivePlayersToDate { get; init; }
    public required int CountActivePlayersToDateLastMonth { get; init; }
    public required int CountNewPlayersThisMonth { get; init; }
    public required int CountActiveTeamsToDate { get; init; }
    public required int CountActiveTeamsToDateLastMonth { get; init; }
    public required int CountNewTeamsThisMonth { get; init; }
}
