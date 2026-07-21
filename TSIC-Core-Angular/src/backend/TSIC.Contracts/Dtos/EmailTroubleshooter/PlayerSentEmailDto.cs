namespace TSIC.Contracts.Dtos.EmailTroubleshooter;

/// <summary>
/// One message our system dispatched to a family's own address within a job, for the
/// player-facing "emails we've sent you" list. Sourced from Jobs.emailLogs (one row per send
/// batch), so it records that the address was in a dispatched batch and when — NOT a per-recipient
/// delivery time or status. Accepted by Amazon SES does not guarantee the message reached the inbox.
/// </summary>
public record PlayerSentEmailDto
{
    public required string? Subject { get; init; }
    public required DateTime SentAt { get; init; }
}
