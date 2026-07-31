using TSIC.Contracts.Dtos;

namespace TSIC.API.Services.Players;

public interface IPlayerRegConfirmationService
{
    Task<PlayerRegConfirmationDto> BuildAsync(Guid jobId, string familyUserId, CancellationToken ct);
    Task<PlayerRegConfirmationDto> BuildAsync(string jobPath, string familyUserId, CancellationToken ct);
    /// <summary>
    /// Builds the registration confirmation email body. When <paramref name="isEcheckPending"/>
    /// is true, prepends an inline-styled "paid by eCheck" banner (drafts finalize in 3–5
    /// business days; a returned draft restores the balance automatically). The payment is
    /// booked at submit either way.
    /// </summary>
    Task<(string Subject, string Html)> BuildEmailAsync(Guid jobId, string familyUserId, CancellationToken ct, bool isEcheckPending = false);
    Task<(string Subject, string Html)> BuildEmailAsync(string jobPath, string familyUserId, CancellationToken ct, bool isEcheckPending = false);

    /// <summary>
    /// Sends the family's confirmation email — the redelivery chokepoint shared by the wizard's
    /// "Re-Send" button and the admin fly-in resend. Recipients are mom ∪ dad ∪ every family player
    /// email in the job. Redelivery semantics: the job's Reply-To is applied, its CC/BCC list is NOT
    /// (the copies audience got the original; a redelivery is not a new confirmation event). Player
    /// confirmations carry no BConfirmationSent latch — this always sends.
    /// </summary>
    Task<PlayerConfirmationSendResult> SendConfirmationAsync(Guid jobId, string familyUserId, CancellationToken ct);
    Task<PlayerConfirmationSendResult> SendConfirmationAsync(string jobPath, string familyUserId, CancellationToken ct);
}

public enum PlayerConfirmationSendFailure { None, JobNotFound, NoRecipients, NoContent, SendFailed }

public sealed record PlayerConfirmationSendResult
{
    public required bool Sent { get; init; }
    public required PlayerConfirmationSendFailure Failure { get; init; }
    public IReadOnlyList<string> Recipients { get; init; } = [];
}
