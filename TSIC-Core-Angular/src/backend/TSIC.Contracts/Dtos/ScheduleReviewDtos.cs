namespace TSIC.Contracts.Dtos;

/// <summary>
/// One club rep who should receive the "review the schedule" letter: an ACTIVE club-rep
/// registration on the job holding at least one team that actually appears in the built
/// schedule.
///
/// The audience is shown to the director before they send rather than being a count they
/// have to trust — these letters get forwarded to coaches, so who is on the list matters.
/// </summary>
public record ScheduleReviewRecipientDto
{
    public required Guid RegistrationId { get; init; }

    /// <summary>Rep's display name, from their login. Either part may be missing.</summary>
    public required string? FirstName { get; init; }
    public required string? LastName { get; init; }

    /// <summary>The registrant's own contact email — the address the batch engine sends to.</summary>
    public required string? Email { get; init; }

    /// <summary>In-event club name off the registration (never the club's identity record).</summary>
    public required string? ClubName { get; init; }

    /// <summary>How many of this rep's teams appear in the schedule. Always 1 or more.</summary>
    public required int ScheduledTeamCount { get; init; }

    /// <summary>
    /// True when this rep has opted out of email. They stay on the list, visibly, because a
    /// director counting heads should see why the send count is short — the batch engine
    /// filters them at send time regardless.
    /// </summary>
    public required bool EmailOptOut { get; init; }
}
