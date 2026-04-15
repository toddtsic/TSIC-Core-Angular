namespace TSIC.Contracts.Dtos.ClubRoster;

/// <summary>
/// A club rep's team with player count, for the team selector dropdown.
/// </summary>
public record ClubRosterTeamDto
{
    public required Guid TeamId { get; init; }
    public required string TeamName { get; init; }
    public required string AgegroupName { get; init; }
    public required int PlayerCount { get; init; }
}

/// <summary>
/// A player on a club rep's team roster.
/// </summary>
public record ClubRosterPlayerDto
{
    public required Guid RegistrationId { get; init; }
    public required string PlayerName { get; init; }
    public required string AgegroupName { get; init; }
    public required string TeamName { get; init; }
    public required bool IsActive { get; init; }
    public string? UniformNumber { get; init; }
}

/// <summary>
/// Request to update a player's uniform number.
/// </summary>
public record UpdateUniformNumberRequest
{
    public required Guid RegistrationId { get; init; }
    public string? UniformNumber { get; init; }
}

/// <summary>
/// Request to move one or more players to a different team within the same club.
/// </summary>
public record MovePlayersRequest
{
    public required List<Guid> RegistrationIds { get; init; }
    public required Guid TargetTeamId { get; init; }
}

/// <summary>
/// Request to delete one or more bogus registrations.
/// </summary>
public record DeletePlayersRequest
{
    public required List<Guid> RegistrationIds { get; init; }
}

/// <summary>
/// Result of a roster mutation (move or delete).
/// </summary>
public record ClubRosterMutationResultDto
{
    public required int AffectedCount { get; init; }
    public required string Message { get; init; }
}
