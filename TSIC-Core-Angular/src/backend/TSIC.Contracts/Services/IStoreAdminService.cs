using TSIC.Contracts.Dtos.Store;

namespace TSIC.Contracts.Services;

/// <summary>
/// Service for store admin operations: analytics, refunds, restocks, pickup signing.
/// </summary>
public interface IStoreAdminService
{
    // ── Analytics ──

    Task<List<StoreSalesPivotDto>> GetSalesPivotAsync(Guid jobId);
    Task<List<StoreSalesByItemDto>> GetSalesByItemAsync(Guid jobId);
    Task<List<StorePaymentDetailDto>> GetPaymentDetailsAsync(Guid jobId, bool walkUpOnly);
    Task<List<StoreFamilyPurchaseDto>> GetFamilyPurchasesAsync(Guid jobId);
    Task<StoreFamilyPurchaseDto?> GetFamilyPurchaseHistoryAsync(Guid jobId, string familyUserId);

    // ── Refunds ──

    Task<List<StoreRefundedItemDto>> GetRefundedItemsAsync(Guid jobId);

    // ── Restocks ──

    Task<List<StoreRestockedItemDto>> GetRestockedItemsAsync(Guid jobId);
    Task LogRestockAsync(Guid jobId, string userId, LogRestockRequest request);

    // ── Pickup ──

    Task SignForPickupAsync(Guid jobId, string userId, SignForPickupRequest request);
}
