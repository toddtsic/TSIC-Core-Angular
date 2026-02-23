namespace TSIC.Contracts.Dtos.Scoring;

// --- Response DTOs ---

public record MobileScorerDto
{
    public required Guid RegistrationId { get; init; }
    public required string Username { get; init; }
    public required string? FirstName { get; init; }
    public required string? LastName { get; init; }
    public required string? Email { get; init; }
    public required string? Cellphone { get; init; }
    public required bool BActive { get; init; }
}

// --- Request DTOs ---

public record CreateMobileScorerRequest
{
    public required string Username { get; init; }
    public required string FirstName { get; init; }
    public required string LastName { get; init; }
    public string? Email { get; init; }
    public string? Cellphone { get; init; }
}

public record UpdateMobileScorerRequest
{
    public string? Email { get; init; }
    public string? Cellphone { get; init; }
    public required bool BActive { get; init; }
}
