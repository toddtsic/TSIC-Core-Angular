namespace TSIC.Contracts.Dtos;

public record SubmitByCheckRequestDto
{
    public required string JobPath { get; init; } = string.Empty;
    public required List<Guid> RegistrationIds { get; init; } = new();
}

public record SubmitByCheckResponseDto
{
    public required bool Success { get; init; }
    public required string Message { get; init; } = string.Empty;
    public required List<Guid> UpdatedRegistrationIds { get; init; } = new();
    public required List<SubmitByCheckRejectionDto> Rejections { get; init; } = new();

    /// <summary>
    /// Registrants whose held seat had lapsed and was gone when the check was submitted — they
    /// were committed onto the WAITLIST mirror instead of the full team (no money taken; a seat
    /// is only held if one is available). Surfaced separately so the UI can say so plainly.
    /// </summary>
    public required List<SubmitByCheckWaitlistDto> Waitlisted { get; init; } = new();
}

public record SubmitByCheckRejectionDto
{
    public required Guid RegistrationId { get; init; }
    public required string Reason { get; init; } = string.Empty;
}

public record SubmitByCheckWaitlistDto
{
    public required Guid RegistrationId { get; init; }
    public required string WaitlistTeamName { get; init; } = string.Empty;
}
