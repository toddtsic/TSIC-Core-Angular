namespace TSIC.Contracts.Dtos;

/// <summary>
/// One row on the role-selection "Looking for a new event?" panel — a Job
/// that the family has not registered for yet, run by a Customer the family
/// HAS prior history with. Returned by GET /api/auth/suggested-events.
/// </summary>
public record SuggestedEventDto
{
    public required Guid JobId { get; init; }
    public required string JobPath { get; init; }
    public required string JobName { get; init; }
    public string? JobLogo { get; init; }
    public required string CustomerName { get; init; }
    public required bool PlayerRegistrationOpen { get; init; }
    public required bool StoreOpen { get; init; }
    public required bool SchedulePublished { get; init; }
    public DateTime? RegistrationExpiry { get; init; }
}
