namespace TSIC.Contracts.Dtos;

/// <summary>
/// Lightweight projection of a team for player self-rostering display.
/// Fields intentionally limited; expand as wizard requires (e.g., waitlist substitution, fees breakdown).
/// </summary>
public record AvailableTeamDto
{
    public required Guid TeamId { get; init; }
    public required string TeamName { get; init; }
    public required Guid AgegroupId { get; init; }
    public string? AgegroupName { get; init; }
    public Guid? DivisionId { get; init; }
    public string? DivisionName { get; init; }
    public required int MaxRosterSize { get; init; }
    public required int CurrentRosterSize { get; init; }
    public required bool RosterIsFull { get; init; }
    public bool? TeamAllowsSelfRostering { get; init; }
    public bool? AgegroupAllowsSelfRostering { get; init; }
    public decimal? PerRegistrantFee { get; init; }
    public decimal? PerRegistrantDeposit { get; init; }
    public required bool JobUsesWaitlists { get; init; }
    public Guid? WaitlistTeamId { get; init; } // Placeholder for future waitlist substitution mapping
}
