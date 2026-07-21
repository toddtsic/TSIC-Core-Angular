namespace TSIC.Contracts.Dtos.EmailTroubleshooter;

/// <summary>
/// Body for the player-facing unsuppress / test-send calls. Carries a single address; the server
/// still validates it against the caller's own family set before touching SES — a convenience,
/// not a trust input.
/// </summary>
public record MyEmailAddressRequest
{
    public required string Email { get; init; }
}
