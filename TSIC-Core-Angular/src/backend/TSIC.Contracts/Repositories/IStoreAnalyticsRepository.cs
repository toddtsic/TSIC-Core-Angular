using TSIC.Contracts.Dtos.Store;
using TSIC.Domain.Entities;

namespace TSIC.Contracts.Repositories;

/// <summary>
/// Repository for store analytics queries and admin operations (refunds, restocks, pickup).
/// All read methods are complex multi-join queries returning DTOs directly.
/// </summary>
public interface IStoreAnalyticsRepository
{
    // ── Sales Analytics ──

    /// <summary>
    /// Sales pivot: units and revenue by item by year-month.
    /// </summary>
    Task<List<StoreSalesPivotDto>> GetSalesPivotAsync(int storeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sales totals by item (for pie chart).
    /// </summary>
    Task<List<StoreSalesByItemDto>> GetSalesByItemAsync(int storeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Full payment records with customer details and payment method.
    /// Optionally filter to walk-up orders only.
    /// </summary>
    Task<List<StorePaymentDetailDto>> GetPaymentDetailsAsync(int storeId, bool walkUpOnly, CancellationToken cancellationToken = default);

    /// <summary>
    /// All families with purchase history and transaction details.
    /// </summary>
    Task<List<StoreFamilyPurchaseDto>> GetFamilyPurchasesAsync(int storeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// A specific family's purchase history.
    /// </summary>
    Task<StoreFamilyPurchaseDto?> GetFamilyPurchaseHistoryAsync(int storeId, string familyUserId, CancellationToken cancellationToken = default);

    // ── Refunds ──

    /// <summary>
    /// Get items with RefundedTotal greater than 0.
    /// </summary>
    Task<List<StoreRefundedItemDto>> GetRefundedItemsAsync(int storeId, CancellationToken cancellationToken = default);

    // ── Restocks ──

    /// <summary>
    /// Get restock history entries.
    /// </summary>
    Task<List<StoreRestockedItemDto>> GetRestockedItemsAsync(int storeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Log a restock entry.
    /// </summary>
    void AddRestock(StoreCartBatchSkuRestocks restock);

    /// <summary>
    /// Log a quantity adjustment entry.
    /// </summary>
    void AddQuantityAdjustment(StoreCartBatchSkuQuantityAdjustments adjustment);

    // ── Pickup ──

    /// <summary>
    /// Get a tracked batch entity for sign-off updates.
    /// </summary>
    Task<StoreCartBatches?> GetBatchByIdAsync(int storeCartBatchId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Persist all pending changes.
    /// </summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
