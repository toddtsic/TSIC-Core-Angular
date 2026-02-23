using TSIC.Contracts.Dtos.Store;
using TSIC.Domain.Entities;

namespace TSIC.Contracts.Repositories;

/// <summary>
/// Repository for StoreCart, StoreCartBatches, StoreCartBatchSkus, and StoreCartBatchAccounting.
/// Handles the full cart → order → payment pipeline.
/// </summary>
public interface IStoreCartRepository
{
    // ── Cart ──

    /// <summary>
    /// Get a cart for a family user within a store.
    /// </summary>
    Task<StoreCart?> GetCartAsync(int storeId, string familyUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Add a new cart.
    /// </summary>
    void AddCart(StoreCart cart);

    // ── Batches ──

    /// <summary>
    /// Get the current unpaid batch for a cart.
    /// Unpaid = batch has no StoreCartBatchAccounting records.
    /// </summary>
    Task<StoreCartBatches?> GetCurrentBatchAsync(int storeCartId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Add a new batch.
    /// </summary>
    void AddBatch(StoreCartBatches batch);

    // ── Batch SKUs (line items) ──

    /// <summary>
    /// Get line items for a batch with item/color/size names.
    /// Complex multi-join query returning DTOs.
    /// </summary>
    Task<List<StoreCartLineItemDto>> GetBatchLineItemsAsync(int storeCartBatchId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a tracked line item entity for updates.
    /// </summary>
    Task<StoreCartBatchSkus?> GetLineItemByIdAsync(int storeCartBatchSkuId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get existing line item for a SKU within a batch (to increment quantity instead of duplicating).
    /// </summary>
    Task<StoreCartBatchSkus?> GetLineItemBySkuAsync(int storeCartBatchId, int storeSkuId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Add a new line item.
    /// </summary>
    void AddLineItem(StoreCartBatchSkus lineItem);

    /// <summary>
    /// Remove a line item from the context.
    /// </summary>
    void RemoveLineItem(StoreCartBatchSkus lineItem);

    // ── Accounting (payment records) ──

    /// <summary>
    /// Add a payment record for a batch.
    /// </summary>
    void AddAccounting(StoreCartBatchAccounting accounting);

    /// <summary>
    /// Check if a batch has any accounting (payment) records.
    /// </summary>
    Task<bool> BatchHasPaymentAsync(int storeCartBatchId, CancellationToken cancellationToken = default);

    // ── Availability queries ──

    /// <summary>
    /// Count total active + paid quantities for a SKU across all batches.
    /// Sold = Active line items in batches that have accounting records.
    /// </summary>
    Task<int> GetSoldCountForSkuAsync(int storeSkuId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Count total active quantities in unpaid batches for a SKU.
    /// InCart = Active line items in batches that have NO accounting records.
    /// </summary>
    Task<int> GetInCartCountForSkuAsync(int storeSkuId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validate availability of all SKUs in a batch before checkout.
    /// Returns list of SKU IDs that are over-committed (sold + inCart > MaxCanSell).
    /// </summary>
    Task<List<int>> ValidateBatchAvailabilityAsync(int storeCartBatchId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all active line items in a batch (tracked for checkout updates).
    /// </summary>
    Task<List<StoreCartBatchSkus>> GetBatchLineItemEntitiesAsync(int storeCartBatchId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Persist all pending changes.
    /// </summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
