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
    /// Matches on DirectToRegId so same SKU for different players creates separate lines.
    /// </summary>
    Task<StoreCartBatchSkus?> GetLineItemBySkuAsync(int storeCartBatchId, int storeSkuId, Guid? directToRegId = null, CancellationToken cancellationToken = default);

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

    /// <summary>
    /// Get the first accounting (payment) record for a batch.
    /// </summary>
    Task<StoreCartBatchAccounting?> GetBatchAccountingAsync(int storeCartBatchId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Who a purchase belongs to and who its receipt goes to. Null when the batch does not exist.
    ///
    /// <para>
    /// This is the ONE place a receipt caller learns the batch's owning job and family, and every
    /// receipt path must consult it BEFORE generating or mailing anything — see
    /// <see cref="StoreReceiptContextDto"/>. Do not add a second batch lookup that answers
    /// "who owns this" differently.
    /// </para>
    /// </summary>
    Task<StoreReceiptContextDto?> GetReceiptContextAsync(int storeCartBatchId, CancellationToken cancellationToken = default);

    /// <summary>
    /// One family's purchase history in one job, newest first — legacy's Invoices grid. Scoped by
    /// BOTH job and family in the query itself, so there is no id a caller can pass to widen it.
    /// </summary>
    Task<List<StoreFamilyPurchaseHistoryRowDto>> GetFamilyPurchaseHistoryAsync(
        Guid jobId, string familyUserId, CancellationToken cancellationToken = default);

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

    // ValidateBatchAvailabilityAsync was REMOVED. It answered "which SKUs are over-committed"
    // counting sold + every OTHER unpaid cart against MaxCanSell, and checkout threw on any hit.
    // Both halves were wrong: legacy's checkout basis is sold-only (units in someone else's
    // unpaid cart are not gone — first to pay wins), and legacy trims the cart rather than
    // refusing it. StoreCartService.TrimBatchToAvailabilityAsync now owns both rules. Do not
    // reintroduce a second availability opinion here.

    /// <summary>
    /// Get all active line items in a batch (tracked for checkout updates).
    /// </summary>
    Task<List<StoreCartBatchSkus>> GetBatchLineItemEntitiesAsync(int storeCartBatchId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get sold counts for multiple SKUs in a single query.
    /// Returns dictionary of storeSkuId → soldCount.
    /// </summary>
    Task<Dictionary<int, int>> GetSoldCountsForSkusAsync(List<int> storeSkuIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get in-cart counts for multiple SKUs in a single query.
    /// Returns dictionary of storeSkuId → inCartCount.
    /// </summary>
    Task<Dictionary<int, int>> GetInCartCountsForSkusAsync(List<int> storeSkuIds, CancellationToken cancellationToken = default);

    // ── Family players (for DirectTo dropdown) ──

    /// <summary>
    /// Get registered players in a family for a specific job (for the DirectTo picker).
    /// Returns registrationId + first/last name for each active registration.
    /// </summary>
    Task<List<StoreFamilyPlayerDto>> GetFamilyPlayersForJobAsync(string familyUserId, Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Record one checkout auto-trim: the SKU, and the quantity before and after. Read back by
    /// <see cref="IStoreAnalyticsRepository.GetQuantityAdjustmentsAsync"/>.
    /// </summary>
    void AddQuantityAdjustment(StoreCartBatchSkuQuantityAdjustments adjustment);

    /// <summary>
    /// SKU display labels (item : size : colour) for the given SKU ids.
    /// </summary>
    Task<Dictionary<int, string>> GetSkuLabelsAsync(List<int> storeSkuIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Persist all pending changes.
    /// </summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
