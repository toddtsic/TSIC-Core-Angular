namespace TSIC.Contracts.Dtos.EmailTroubleshooter;

/// <summary>
/// One message our system dispatched to a family's own address within a job, for the
/// player-facing "emails we've sent you" list. Sourced from Jobs.emailLogs (one row per send
/// batch), so it records that the address was in a dispatched batch and when — NOT a per-recipient
/// delivery time or status. Accepted by Amazon SES does not guarantee the message reached the inbox.
/// </summary>
public record PlayerSentEmailDto
{
    /// <summary>Batch id (emailLogs.EmailId) — the key to fetch this send's template on demand.</summary>
    public required int EmailId { get; init; }
    public required string? Subject { get; init; }

    /// <summary>The From address the batch was sent as (emailLogs.SendFrom) — what the family sees in their inbox.</summary>
    public required string? EmailFrom { get; init; }
    public required DateTime SentAt { get; init; }
}
