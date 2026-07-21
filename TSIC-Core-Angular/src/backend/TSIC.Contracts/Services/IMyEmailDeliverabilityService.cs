using TSIC.Contracts.Dtos.EmailTroubleshooter;

namespace TSIC.Contracts.Services;

/// <summary>
/// Player-facing companion to the admin <see cref="IEmailTroubleshooterService"/>. Lets a
/// logged-in family check the SES suppression status of, self-unsuppress, send a test message to,
/// and review the send history of only <b>their own</b> emails (mom/dad/each player, across all
/// jobs). The caller never supplies an address to act on: the sendable set is resolved server-side
/// from the family login, and every mutating action is refused for any address outside that set —
/// so a player can never touch the account-wide SES list, or send through our SES, for anyone else.
/// </summary>
public interface IMyEmailDeliverabilityService
{
    /// <summary>Suppression status of each of the family's sendable emails (account-wide check).</summary>
    Task<IReadOnlyList<SuppressionEntryDto>> GetStatusAsync(
        string familyUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Remove one address from the SES suppression list, only after confirming it is in this
    /// family's sendable set. Returns <c>null</c> when the address is not the caller's own
    /// (surface as 403; no SES call is made).
    /// </summary>
    Task<SuppressionRemoveResultDto?> UnsuppressAsync(
        string familyUserId, string email, CancellationToken cancellationToken = default);

    /// <summary>
    /// Send a real test message to one address, only after confirming it is in this family's
    /// sendable set, and report which side any failure is on (reuses the admin investigate flow).
    /// Returns <c>null</c> when the address is not the caller's own (surface as 403; no send).
    /// </summary>
    Task<EmailInvestigateResultDto?> TestSendAsync(
        string familyUserId, string email, CancellationToken cancellationToken = default);

    /// <summary>
    /// Messages (newest first) our system dispatched to any of this family's own addresses,
    /// across all jobs, each tagged with its job name. Dispatched/accepted by SES is not a
    /// guarantee of inbox delivery — the caller must present it with that caveat.
    /// </summary>
    Task<IReadOnlyList<PlayerSentEmailDto>> GetSentHistoryAsync(
        string familyUserId, CancellationToken cancellationToken = default);
}
