namespace TSIC.Contracts.Dtos.RegistrationSearch;

/// <summary>
/// Outcome of an admin-initiated confirmation resend from the registrant fly-in. The message is
/// written for the admin's toast — for a player it discloses that the confirmation is family-scoped
/// (one email covering every sibling in the job), because the button sits on a single player's panel.
/// </summary>
public record AdminResendConfirmationResultDto
{
    public required bool Sent { get; init; }
    public required string Message { get; init; }

    /// <summary>Addresses the confirmation went to. Empty when nothing was sent.</summary>
    public IReadOnlyList<string> Recipients { get; init; } = [];
}
