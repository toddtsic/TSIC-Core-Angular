using TSIC.Contracts.Dtos.Store;

namespace TSIC.Contracts.Services;

/// <summary>
/// Store sales operations — what a director does to a sale AFTER the money has moved.
/// Port of legacy StoreSalesController (Index, GetCartItemSkuOptions,
/// GetCartBatchHasSettledStatus, UpdateCartSku).
/// </summary>
public interface IStoreSalesOpsService
{
    /// <summary>
    /// Every purchased line in the job's store. <paramref name="walkUpOnly"/> narrows to counter
    /// sales, which is legacy's separate StoreSalesWalkup screen.
    /// </summary>
    Task<List<StoreSaleLineDto>> GetSaleLinesAsync(
        Guid jobId, bool walkUpOnly, CancellationToken ct = default);

    /// <summary>
    /// SKUs this line could be exchanged for: same item, different variant, active, in stock.
    /// </summary>
    Task<List<StoreSwapOptionDto>> GetSwapOptionsAsync(
        Guid jobId, int storeCartBatchSkuId, CancellationToken ct = default);

    /// <summary>
    /// Whether the batch's card charge has settled, which decides whether the admin UI may offer a
    /// partial refund or only a full void.
    /// </summary>
    Task<StoreBatchSettledStatusDto> GetBatchSettledStatusAsync(
        Guid jobId, int storeCartBatchId, CancellationToken ct = default);

    /// <summary>
    /// Exchange units of a line for a different SKU of the same item. No money moves — the price
    /// is unchanged — so paid and refunded amounts are split proportionally when only some units
    /// are swapped.
    /// </summary>
    Task SwapCartSkuAsync(
        Guid jobId, string userId, StoreSwapRequest request, CancellationToken ct = default);

    /// <summary>
    /// Refund a line, or void the whole batch behind it. Returns a result rather than throwing on
    /// a gateway refusal — a declined refund is an answer, not an exception.
    /// </summary>
    Task<StoreRefundResponse> RefundAsync(
        Guid jobId, string userId, StoreRefundRequest request, CancellationToken ct = default);
}
