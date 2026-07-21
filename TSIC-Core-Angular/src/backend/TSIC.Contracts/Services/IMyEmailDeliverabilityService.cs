using TSIC.Contracts.Dtos.EmailTroubleshooter;

namespace TSIC.Contracts.Services;

/// <summary>
/// Player-facing companion to the admin <see cref="IEmailTroubleshooterService"/>. Lets a
/// logged-in family check the Amazon SES suppression status of, self-unsuppress, send a test
/// message to, and review this job's send history for only <b>their own</b> emails (mom/dad/each
/// player in the job). The caller never supplies an address to act on: the sendable set is
/// resolved server-side from the family login + job, and every mutating action is refused for any
/// address outside that set — so a player can never touch the account-wide SES list, or send
/// through our SES, for anyone else.
/// </summary>
public interface IMyEmailDeliverabilityService
{
    /// <summary>Amazon SES suppression status of each of the family's sendable emails in the job.</summary>
    Task<IReadOnlyList<SuppressionEntryDto>> GetStatusAsync(
        Guid jobId, string familyUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Remove one address from the Amazon SES suppression list, only after confirming it is in this
    /// family's sendable set for the job. Returns <c>null</c> when the address is not the caller's
    /// own (surface as 403; no SES call is made).
    /// </summary>
    Task<SuppressionRemoveResultDto?> UnsuppressAsync(
        Guid jobId, string familyUserId, string email, CancellationToken cancellationToken = default);

    /// <summary>
    /// Send a real test message to one address, only after confirming it is in this family's
    /// sendable set for the job, and report which side any failure is on (reuses the admin
    /// investigate flow). Returns <c>null</c> when the address is not the caller's own (403; no send).
    /// </summary>
    Task<EmailInvestigateResultDto?> TestSendAsync(
        Guid jobId, string familyUserId, string email, CancellationToken cancellationToken = default);

    /// <summary>
    /// Messages (newest first) our system dispatched to any of this family's own addresses within
    /// the job. Accepted by Amazon SES is not a guarantee of inbox delivery — the caller must
    /// present it with that caveat.
    /// </summary>
    Task<IReadOnlyList<PlayerSentEmailDto>> GetSentHistoryAsync(
        Guid jobId, string familyUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The unresolved body template of one send in the history, only when that batch was dispatched
    /// to one of this family's own addresses in the job. Returns <c>null</c> when the batch is not
    /// the caller's (surface as 404). The template still contains substitution tokens — it is what
    /// produced the message, not the personalized copy any one recipient received.
    /// </summary>
    Task<string?> GetSentTemplateAsync(
        Guid jobId, string familyUserId, int emailId, CancellationToken cancellationToken = default);
}
