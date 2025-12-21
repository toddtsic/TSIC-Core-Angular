namespace TSIC.Contracts.Dtos;

/// <summary>
/// Lightweight projection of a team for player self-rostering display.
/// Fields intentionally limited; expand as wizard requires (e.g., waitlist substitution, fees breakdown).
/// </summary>
public class AvailableTeamDto
{
    public required Guid TeamId { get; set; }
    public required string TeamName { get; set; }
    public Guid AgegroupId { get; set; }
    public string? AgegroupName { get; set; }
    public Guid? DivisionId { get; set; }
    public string? DivisionName { get; set; }
    public int MaxRosterSize { get; set; }
    public int CurrentRosterSize { get; set; }
    public bool RosterIsFull { get; set; }
    public bool? TeamAllowsSelfRostering { get; set; }
    public bool? AgegroupAllowsSelfRostering { get; set; }
    public decimal? PerRegistrantFee { get; set; }
    public decimal? PerRegistrantDeposit { get; set; }
    public bool JobUsesWaitlists { get; set; }
    public Guid? WaitlistTeamId { get; set; } // Placeholder for future waitlist substitution mapping
}
