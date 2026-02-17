namespace TSIC.Contracts.Dtos.Widgets;

/// <summary>
/// The primary contact for an event — the earliest-registered administrator.
/// </summary>
public record EventContactDto
{
    public required string FirstName { get; init; }
    public required string LastName { get; init; }
    public required string Email { get; init; }
}
