namespace TSIC.Contracts.Dtos;

/// <summary>
/// Which audience the role-selection suggestions panel is targeting. Family
/// users see Jobs with player registration open; ClubReps see Jobs with team
/// registration open. The two account classes are mutually exclusive per
/// privilege-separation policy, so one user can only be one audience.
/// </summary>
public enum SuggestedEventAudience
{
    Family,
    ClubRep
}

/// <summary>
/// One row on the role-selection "Looking for a new event?" panel — a Job
/// the user has not registered in yet, run by a Customer they HAVE prior
/// history with (either as a Family or as a ClubRep). Returned by GET
/// /api/auth/suggested-events.
/// </summary>
public record SuggestedEventDto
{
    public required Guid JobId { get; init; }
    public required string JobPath { get; init; }
    public required string JobName { get; init; }
    public string? JobLogo { get; init; }
    public required string CustomerName { get; init; }
    public required bool PlayerRegistrationOpen { get; init; }
    public required bool TeamRegistrationOpen { get; init; }
    public required bool StoreOpen { get; init; }
    public required bool SchedulePublished { get; init; }
    public DateTime? RegistrationExpiry { get; init; }
}
