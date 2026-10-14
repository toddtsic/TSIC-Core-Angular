namespace TSIC.Contracts.Dtos;

/// <summary>
/// One active team with its club rep's contact details for the "Teams Export (Excel)" export —
/// the EF replacement for the <c>reporting_migrate.League_Teams</c> proc. Job-scoped. Home field
/// is null for a team without one.
/// </summary>
public record LeagueTeamExcelRowDto
{
    public string? AgegroupName { get; init; }
    public string? DivName { get; init; }
    public string? TeamName { get; init; }
    public string? ClubName { get; init; }
    public string? LastName { get; init; }
    public string? FirstName { get; init; }
    public string? Email { get; init; }
    public string? City { get; init; }
    public string? State { get; init; }
    public string? PostalCode { get; init; }
    public string? Cellphone { get; init; }
    public string? TeamComments { get; init; }
    public string? HomeField { get; init; }
    public required Guid TeamId { get; init; }
    public string? LevelOfPlay { get; init; }
}
