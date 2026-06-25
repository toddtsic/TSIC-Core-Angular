using TSIC.Contracts.Dtos.Arb;

namespace TSIC.Contracts.Services;

public interface IArbDefensiveService
{
    Task<List<ArbFlaggedRegistrantDto>> GetFlaggedSubscriptionsAsync(
        Guid jobId, ArbFlagType flagType, CancellationToken ct = default);

    /// <summary>
    /// Starts the ARB defensive batch as a background job and returns a handle immediately.
    /// Sends, opt-out suppression, footer, retry, sender-summary + director-notify all run in the
    /// background engine. Poll the registry for progress/final status (same as every batch path).
    /// </summary>
    Task<EmailBatchHandle> StartDefensiveEmailsAsync(
        ArbSendEmailsRequest request, CancellationToken ct = default);

    Task<ArbSubscriptionInfoDto?> GetSubscriptionInfoAsync(
        Guid registrationId, CancellationToken ct = default);

    Task<ArbUpdateCcResultDto> UpdateSubscriptionCreditCardAsync(
        ArbUpdateCcRequest request, string userId, CancellationToken ct = default);
}
