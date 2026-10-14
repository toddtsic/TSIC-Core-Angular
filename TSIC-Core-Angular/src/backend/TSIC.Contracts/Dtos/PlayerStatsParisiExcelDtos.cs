namespace TSIC.Contracts.Dtos;

/// <summary>
/// One active player's combine results for the "Player Stats Export - Parisi (Excel)" export —
/// the EF replacement for the <c>reporting_migrate.PlayerStats_ParisiResults</c> proc. Job-scoped.
/// </summary>
public record PlayerStatsParisiExcelRowDto
{
    public string? AgegroupName { get; init; }
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? TeamName { get; init; }
    public string? UniformNo { get; init; }
    public string? SchoolName { get; init; }
    public string? ClubName { get; init; }
    public double? FastestShot { get; init; }
    public double? FiveTenFive { get; init; }
    public double? FortyYardDash { get; init; }
    public double? ThreeHundredShuttle { get; init; }
}
