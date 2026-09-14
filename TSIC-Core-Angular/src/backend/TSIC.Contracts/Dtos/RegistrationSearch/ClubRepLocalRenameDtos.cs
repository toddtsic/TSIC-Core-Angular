namespace TSIC.Contracts.Dtos.RegistrationSearch;

/// <summary>
/// Director's THIS-EVENT-ONLY rename of the club a Club Rep registration was made under.
/// </summary>
public record RenameClubRepClubLocalRequest
{
    public required string ClubName { get; init; }
}

/// <summary>
/// Result of a local club rename: the name now on the registration and how many round-robin
/// schedule slots in this job were restamped with it.
/// </summary>
public record RenameClubRepClubLocalResponse
{
    public required string ClubName { get; init; }
    public required int ScheduleSlotsUpdated { get; init; }
}
