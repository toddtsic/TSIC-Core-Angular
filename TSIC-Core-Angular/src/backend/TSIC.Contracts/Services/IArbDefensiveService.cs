using TSIC.Contracts.Dtos.Arb;

namespace TSIC.Contracts.Services;

public interface IArbDefensiveService
{
    Task<List<ArbFlaggedRegistrantDto>> GetFlaggedSubscriptionsAsync(
        Guid jobId, ArbFlagType flagType, CancellationToken ct = default);

    Task<ArbEmailResultDto> SendDefensiveEmailsAsync(
        ArbSendEmailsRequest request, CancellationToken ct = default);

    Task<ArbSubscriptionInfoDto?> GetSubscriptionInfoAsync(
        Guid registrationId, CancellationToken ct = default);

    Task<ArbUpdateCcResultDto> UpdateSubscriptionCreditCardAsync(
        ArbUpdateCcRequest request, string userId, CancellationToken ct = default);
}
