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
    /// Check availability for a specific SKU.
    /// </summary>
    Task<SkuAvailabilityDto> CheckAvailabilityAsync(int storeSkuId);

    /// <summary>
    /// Check availability for multiple SKUs in a batch (2 DB queries instead of 2N).
    /// </summary>
    Task<List<SkuAvailabilityDto>> CheckAvailabilityBatchAsync(List<int> storeSkuIds);

    /// <summary>
    /// Validate cart, recalculate totals, record payment, mark items paid.
    /// </summary>
    Task<StoreCheckoutResultDto> CheckoutAsync(Guid jobId, string familyUserId, string userId,
        StoreCheckoutRequest request);
}
