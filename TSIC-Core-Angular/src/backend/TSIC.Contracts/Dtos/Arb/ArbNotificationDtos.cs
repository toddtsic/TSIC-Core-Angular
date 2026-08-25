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

/// <summary>
/// One email exactly as it WOULD have been sent, captured on a dry run instead of transmitted.
///
/// This is the whole point of the dry run: the guards, the projection and the token substitution
/// are the parts that can be wrong, and they are invisible in a count. Populated only off
/// Production — on a live run the emails go to families and there is nothing to inspect.
/// </summary>
public record ArbRenderedEmailDto
{
    public required string Registrant { get; init; }
    public required string JobName { get; init; }
    /// <summary>Recipients the sendable-address filter actually resolved, in send order.</summary>
    public required List<string> ToAddresses { get; init; }
    public required string Subject { get; init; }
    /// <summary>The director the family's reply would reach. Null means the reply lands on TSIC support.</summary>
    public required string? ReplyToName { get; init; }
    public required string? ReplyToAddress { get; init; }
    public required string HtmlBody { get; init; }
}

/// <summary>Paired counts for the digest: what was found, what was emailed, what was not.</summary>
public record ArbNotifyResultDto
{
    public required int Found { get; init; }
    public required int Emailed { get; init; }
    public required int Skipped { get; init; }
    public required List<ArbNotifySkipDto> Skips { get; init; }

    /// <summary>
    /// True when nothing was transmitted. <see cref="Emailed"/> then counts messages that WOULD have
    /// been sent, and <see cref="Rendered"/> holds them. Never true on Production.
    /// </summary>
    public bool DryRun { get; init; }

    /// <summary>Populated on a dry run only; empty on a live run.</summary>
    public List<ArbRenderedEmailDto> Rendered { get; init; } = [];

    /// <summary>
    /// The expiring-card pass summary. Rendered and returned on a dry run instead of mailed to
    /// support; on a live run it is mailed AND returned. Null on the failed-draft path, which
    /// reports through the sweep digest rather than a summary of its own.
    /// </summary>
    public string? SummaryHtml { get; init; }

    public static ArbNotifyResultDto Empty => new()
    {
        Found = 0,
        Emailed = 0,
        Skipped = 0,
        Skips = []
    };
}
