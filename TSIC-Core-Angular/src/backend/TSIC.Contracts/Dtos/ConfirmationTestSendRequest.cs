namespace TSIC.Contracts.Dtos;

/// <summary>
/// Non-prod request to deliver a rendered confirmation email to a single test inbox instead of the
/// registrant. Carries only the destination — WHAT gets rendered is determined server-side from the
/// caller's own registration, never from the request, so this can't be used to render someone else's
/// confirmation to an arbitrary address.
/// </summary>
public record ConfirmationTestSendRequest
{
    public required string TestRecipient { get; init; }

    /// <summary>Prepends the eCheck settlement-pending banner, matching a just-submitted eCheck.</summary>
    public bool IsEcheckPending { get; init; }
}
