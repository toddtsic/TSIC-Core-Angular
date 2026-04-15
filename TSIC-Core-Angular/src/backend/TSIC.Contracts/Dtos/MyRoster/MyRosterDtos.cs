namespace TSIC.Contracts.Dtos.MyRoster;

/// <summary>
/// Response from GET /api/my-roster. When Allowed=false, the frontend renders a
/// friendly alert; other fields will be null.
/// </summary>
public record MyRosterResponseDto
{
    public required bool Allowed { get; init; }
    public string? Reason { get; init; }
    public Guid? TeamId { get; init; }
    public string? TeamName { get; init; }
    public List<MyRosterPlayerDto>? Players { get; init; }
}

/// <summary>
/// A single roster row for the Player/Staff "View Rosters" view.
/// Includes contact fields required for the batch-email feature.
/// </summary>
public record MyRosterPlayerDto
{
    public required Guid RegistrationId { get; init; }
    public required string PlayerName { get; init; }
    public required string RoleName { get; init; }
    public required bool BActive { get; init; }
    public string? Email { get; init; }
    public string? Cellphone { get; init; }
    public int? GradYear { get; init; }
    public string? Position { get; init; }
    public string? Gender { get; init; }
}

/// <summary>
/// Request to send a batch email to caller's teammates.
/// If RegistrationIds is null/empty, the server emails the entire team roster.
/// Otherwise, server validates that every id is on the caller's team.
/// </summary>
public record MyRosterBatchEmailRequest
{
    public List<Guid>? RegistrationIds { get; init; }
    public required string Subject { get; init; }
    public required string BodyTemplate { get; init; }
}
