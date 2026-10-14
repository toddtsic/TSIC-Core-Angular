namespace TSIC.Contracts.Dtos;

/// <summary>
/// Games one team plays on one field on one day, for the "Team Field Distribution" export — the
/// EF replacement for the <c>reporting_migrate.TeamsFieldDistribution</c> proc. Job-scoped.
/// </summary>
public record TeamFieldDistributionExcelRowDto
{
    public string? ClubName { get; init; }
    public string? AgegroupName { get; init; }
    public string? TeamName { get; init; }
    public string? FieldName { get; init; }
    /// <summary>The game's calendar day (no time part); null for an undated game.</summary>
    public DateTime? GameDay { get; init; }
    public required int GameCount { get; init; }
}
