namespace TSIC.Contracts.Dtos;

/// <summary>
/// Result of resolving where a team should be placed (original agegroup or waitlist mirror).
/// </summary>
public record TeamPlacementResult
{
    /// <summary>The agegroup ID to actually use (may be waitlist mirror).</summary>
    public required Guid AgegroupId { get; init; }

    /// <summary>The division ID to actually use (may be waitlist mirror). Null if caller assigns division.</summary>
    public Guid? DivisionId { get; init; }

    /// <summary>League ID (passed through from source agegroup).</summary>
    public required Guid LeagueId { get; init; }

    /// <summary>True if the team was redirected to a waitlist agegroup.</summary>
    public required bool IsWaitlisted { get; init; }

    /// <summary>Name of the waitlist agegroup (for logging/UI). Null if not waitlisted.</summary>
    public string? WaitlistAgegroupName { get; init; }
}

/// <summary>
/// Result of resolving where a player should be rostered when a team is full.
/// If waitlisted, contains the WAITLIST team mirror ID.
/// </summary>
public record RosterPlacementResult
{
    /// <summary>The team ID to roster the player onto (may be waitlist mirror).</summary>
    public required Guid TeamId { get; init; }

    /// <summary>True if the player was redirected to a waitlist team mirror.</summary>
    public required bool IsWaitlisted { get; init; }

    /// <summary>Name of the waitlist team (for logging/UI). Null if not waitlisted.</summary>
    public string? WaitlistTeamName { get; init; }
}
