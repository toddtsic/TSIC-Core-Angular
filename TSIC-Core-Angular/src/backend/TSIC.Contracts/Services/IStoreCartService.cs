using TSIC.Contracts.Dtos.Store;

namespace TSIC.Contracts.Services;

/// <summary>
/// Service for customer-facing cart operations: browse, add-to-cart, update, checkout.
/// </summary>
public interface IStoreCartService
{
    /// <summary>
    /// Get or create cart and current batch for a family user.
    /// </summary>
    Task<StoreCartBatchDto> GetCurrentCartAsync(Guid jobId, string familyUserId);

    /// <summary>
    /// This family's completed purchases in this job, newest first — legacy's Invoices screen.
    /// Also the source of the storefront's purchase-history badge count.
    /// </summary>
    Task<List<StoreFamilyPurchaseHistoryRowDto>> GetPurchaseHistoryAsync(
        Guid jobId, string familyUserId, CancellationToken ct = default);

    /// <summary>
    /// Add a SKU to the cart (with availability check).
    /// </summary>
    Task<StoreCartBatchDto> AddToCartAsync(Guid jobId, string familyUserId, string userId, AddToCartRequest request);

    /// <summary>
    /// Update line item quantity (recalculates fees/tax).
    /// </summary>
    Task<StoreCartBatchDto> UpdateQuantityAsync(Guid jobId, string familyUserId, string userId,
        int storeCartBatchSkuId, UpdateCartQuantityRequest request);

    /// <summary>
    /// Remove a line item from the cart.
    /// </summary>
    Task<StoreCartBatchDto> RemoveFromCartAsync(Guid jobId, string familyUserId, string userId,
        int storeCartBatchSkuId);

    /// <summary>
    /// Check availability for a specific SKU in this job's store.
    ///
    /// <para>Both of these took a bare SKU id and answered for ANY job's stock. The jobId is the
    /// boundary; a SKU outside it is simply absent from the answer.</para>
    /// </summary>
    Task<SkuAvailabilityDto> CheckAvailabilityAsync(Guid jobId, int storeSkuId);

    /// <summary>
    /// Check availability for multiple SKUs in this job's store (2 DB queries instead of 2N).
    /// SKUs outside the job are dropped rather than reported, so the response cannot be used to
    /// probe which ids exist elsewhere.
    /// </summary>
    Task<List<SkuAvailabilityDto>> CheckAvailabilityBatchAsync(Guid jobId, List<int> storeSkuIds);

    /// <summary>
    /// Validate cart, recalculate totals, record payment, mark items paid.
    /// </summary>
    /// <summary>
    /// Load the cart for the checkout page, first trimming any line whose stock has gone since it
    /// was added (legacy StoreFamilyController.Checkout GET). Returns what changed so the page can
    /// say so before the shopper pays.
    /// </summary>
    Task<StoreCheckoutPrepareDto> PrepareCheckoutAsync(Guid jobId, string familyUserId, string userId);

    Task<StoreCheckoutResultDto> CheckoutAsync(Guid jobId, string familyUserId, string userId,
        StoreCheckoutRequest request);
}
