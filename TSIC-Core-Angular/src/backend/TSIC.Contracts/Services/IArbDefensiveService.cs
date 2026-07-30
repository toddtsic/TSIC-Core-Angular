using TSIC.Contracts.Dtos.Arb;

namespace TSIC.Contracts.Services;

public interface IArbDefensiveService
{
    Task<List<ArbFlaggedRegistrantDto>> GetFlaggedSubscriptionsAsync(
        Guid jobId, ArbFlagType flagType, CancellationToken ct = default);

    /// <summary>
    /// THE chokepoint for syncing stored ARB statuses from Authorize.Net: checks every
    /// registration in the job with a subscription ID (regardless of bActive) and writes
    /// back any drift. Read paths consume the stored status; they do not call ADN.
    /// </summary>
    Task<ArbRefreshStatusesResultDto> RefreshSubscriptionStatusesAsync(
        Guid jobId, CancellationToken ct = default);

    /// <summary>
    /// Starts the ARB defensive batch as a background job and returns a handle immediately.
    /// Sends, opt-out suppression, footer, retry, sender-summary + director-notify all run in the
    /// background engine. Poll the registry for progress/final status (same as every batch path).
    /// </summary>
    Task<EmailBatchHandle> StartDefensiveEmailsAsync(
        ArbSendEmailsRequest request, CancellationToken ct = default);

    /// <summary>
    /// Sandbox-only: renders the composed defensive email for one flagged registrant and delivers
    /// it for real to a single test inbox. Never a live send path.
    /// </summary>
    Task<EmailTestSendResponse> SendTestEmailAsync(
        ArbTestSendRequest request, CancellationToken ct = default);

    Task<ArbSubscriptionInfoDto?> GetSubscriptionInfoAsync(
        Guid registrationId, CancellationToken ct = default);

    Task<ArbUpdateCcResultDto> UpdateSubscriptionCreditCardAsync(
        ArbUpdateCcRequest request, string userId, CancellationToken ct = default);
}
