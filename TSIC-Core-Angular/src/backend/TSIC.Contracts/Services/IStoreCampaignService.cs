using TSIC.Contracts.Dtos.Store;

namespace TSIC.Contracts.Services;

/// <summary>
/// The store's three email campaigns — abandoned carts, families that never ordered, families that
/// have ordered — as ONE code path.
///
/// Legacy shipped them as three controllers that were byte-for-byte identical below the audience
/// query: same address resolution, same substitution loop, same EmailLogs row, same sender
/// confirmation. Three copies meant three places to fix anything, and they had already drifted
/// (only the abandoned-carts screen ever gained a selectable grid). Here the audience query and the
/// seeded template are the only things that vary; everything else is the shared batch engine.
/// </summary>
public interface IStoreCampaignService
{
    /// <summary>
    /// Everything one campaign screen needs on open. <paramref name="minAgeHours"/> /
    /// <paramref name="maxAgeHours"/> apply only to <see cref="StoreCampaignKind.AbandonedCarts"/>
    /// and default to legacy's 6 and 24.
    /// </summary>
    Task<StoreCampaignSetupDto> GetSetupAsync(
        Guid jobId,
        StoreCampaignKind kind,
        int? minAgeHours = null,
        int? maxAgeHours = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Queues the campaign on the background batch engine and returns immediately. Poll the engine's
    /// job registry for progress; the sender gets a completion receipt when it drains.
    /// </summary>
    Task<StoreCampaignSendResponse> SendAsync(
        Guid jobId,
        string senderUserId,
        StoreCampaignKind kind,
        StoreCampaignSendRequest request,
        CancellationToken cancellationToken = default);
}
