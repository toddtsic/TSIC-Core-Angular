namespace TSIC.Contracts.Dtos;

/// <summary>
/// A registered player's current data for the self-roster update screen.
/// </summary>
public record SelfRosterPlayerDto
{
    public required Guid RegistrationId { get; init; }
    public required string FirstName { get; init; }
    public required string LastName { get; init; }
    public string? UniformNo { get; init; }
    public string? Position { get; init; }
    public required Guid TeamId { get; init; }
    public required string TeamName { get; init; }
    public required IReadOnlyList<SelfRosterTeamOptionDto> AvailableTeams { get; init; }
    public required IReadOnlyList<string> AvailablePositions { get; init; }
}

/// <summary>
/// A team option shown in the team dropdown on the self-roster update screen.
/// </summary>
public record SelfRosterTeamOptionDto
{
    public required Guid TeamId { get; init; }
    public required string TeamName { get; init; }
    public required int CurrentCount { get; init; }
    public required int MaxCount { get; init; }
}

/// <summary>
/// Request body for updating a player's self-roster fields.
/// </summary>
public record SelfRosterUpdateRequestDto
{
    public string? UniformNo { get; init; }
    public string? Position { get; init; }
    public required Guid TeamId { get; init; }
}
