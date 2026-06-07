namespace TSIC.Contracts.Dtos;

/// <summary>
/// One (year, month, customer, job) TSIC-fee row — the EF replacement for the
/// <c>adn.tsicFeesYTDAndLastYear</c> stored proc (legacy Crystal "tsicTSICFeesYTD" /
/// "tsicTSICFeesYTDByCustomer"). <see cref="TsicFees"/> is the proc's
/// <c>(NewPlayers × perPlayerCharge) + (NewTeams × perTeamCharge)</c> for that job-month. The
/// report aggregates these into a this-year-YTD vs last-year-YTD comparison (months 1..lastMonth
/// for both years), grouped by customer (and job).
/// </summary>
public record FeeYtdRowDto
{
    public required int Year { get; init; }
    public required int Month { get; init; }
    public required string CustomerName { get; init; }
    public required string JobName { get; init; }
    public required decimal TsicFees { get; init; }
}
