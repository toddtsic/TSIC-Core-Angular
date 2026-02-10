namespace TSIC.Contracts.Dtos.Ladt;

/// <summary>
/// Root response for the LADT tree hierarchy endpoint.
/// Contains the full 4-level tree with aggregate counts.
/// </summary>
public record LadtTreeRootDto
{
    public required List<LadtTreeNodeDto> Leagues { get; init; }
    public required int TotalTeams { get; init; }
    public required int TotalPlayers { get; init; }
    public required List<Guid> ScheduledTeamIds { get; init; }
}

/// <summary>
/// A single node in the LADT hierarchy tree.
/// Level: 0=League, 1=Agegroup, 2=Division, 3=Team
/// </summary>
public record LadtTreeNodeDto
{
    public required Guid Id { get; init; }
    public Guid? ParentId { get; init; }
    public required string Name { get; init; }
    public required int Level { get; init; }
    public required bool IsLeaf { get; init; }
    public required int TeamCount { get; init; }
    public required int PlayerCount { get; init; }
    public required bool Expanded { get; init; }
    public required bool Active { get; init; }
    public string? ClubName { get; init; }
    public string? Color { get; init; }
    public List<LadtTreeNodeDto>? Children { get; init; }
}

/// <summary>
/// Optional request body for stub creation endpoints.
/// When null or when Name is null/empty, the backend uses its default naming logic.
/// </summary>
public record CreateStubRequest
{
    public string? Name { get; init; }
}
