namespace TSIC.Contracts.Dtos.Arb;

/// <summary>
/// One failed ARB draft, handed from the sweep to the notification step. The sweep has already
/// imported and audited the transaction by the time this is built; nothing here is money-bearing.
/// <see cref="OwedNow"/> is the sweep's own ComputeInstallmentMath figure, carried across so the
/// family's email and the digest quote the same number.
/// </summary>
public record ArbFailedDraftDto
{
    public required Guid RegistrationId { get; init; }
    public required string InvoiceNumber { get; init; }
    public required string TransId { get; init; }
    /// <summary>ADN transactionStatus — "declined" or "generalError".</summary>
    public required string TransactionStatus { get; init; }
    public required decimal OwedNow { get; init; }
    public required string? SubscriptionStatus { get; init; }
    public required string? Registrant { get; init; }
    public required string JobName { get; init; }
}

/// <summary>
/// A failure the notifier deliberately did NOT email, and why. Every skip is printed in the digest:
/// a family that cannot be reached is the one case a human has to pick up, so it must never be
/// silent. See the unresolved-token guard in ArbNotificationService.
/// </summary>
public record ArbNotifySkipDto
{
    public required string Registrant { get; init; }
    public required string JobName { get; init; }
    public required string Reason { get; init; }
}

/// <summary>Paired counts for the digest: what was found, what was emailed, what was not.</summary>
public record ArbNotifyResultDto
{
    public required int Found { get; init; }
    public required int Emailed { get; init; }
    public required int Skipped { get; init; }
    public required List<ArbNotifySkipDto> Skips { get; init; }

    public static ArbNotifyResultDto Empty => new()
    {
        Found = 0,
        Emailed = 0,
        Skipped = 0,
        Skips = []
    };
}
