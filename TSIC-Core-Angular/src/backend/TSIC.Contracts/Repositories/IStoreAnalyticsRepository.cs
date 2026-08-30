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
    /// Sales pivot: units NET of restocks and revenue NET of refunds, by item, sku and year-month.
    /// The one dataset behind all three of legacy's Store Dashboard pivots.
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

    // ── Fulfilment (bag labels, pickup signoff, per-family pivot) ──

    /// <summary>
    /// Every paid SKU line in the store, with family, player, placement and item, for the three
    /// fulfilment reports. Ordered agegroup → club → team → player → item, matching the order a
    /// director packs in.
    ///
    /// <para>Replaces the Crystal procs <c>reporting.StoreLabels</c> and
    /// <c>reporting.StorePickupConfirmation</c>. Unlike those, the club-rep, team and registration
    /// joins are all OPTIONAL — see <see cref="StoreFulfillmentRowDto"/> for why that matters
    /// (the Crystal version silently dropped every walk-up sale).</para>
    /// </summary>
    Task<List<StoreFulfillmentRowDto>> GetFulfillmentRowsAsync(
        int storeId, CancellationToken cancellationToken = default);

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
    /// Every checkout auto-trim recorded for a job, newest first (legacy
    /// StoreCartQuantityAdjustments). The WRITE side lives on
    /// <see cref="IStoreCartRepository.AddQuantityAdjustment"/>, next to the cart mutation
    /// that causes it.
    /// </summary>
    Task<List<StoreQuantityAdjustmentDto>> GetQuantityAdjustmentsAsync(
        Guid jobId,
        CancellationToken cancellationToken = default);

    // ── Pickup ──

    /// <summary>
    /// Get a tracked batch entity for sign-off updates, scoped to one store.
    /// </summary>
    ///
    /// <para>
    /// The store is not optional and there is deliberately no by-id-alone variant. Sign-off is a
    /// STAFF action on a shopper's order inside the director's own job; taking a bare batch id let
    /// a store admin in one job sign an order in another, because a batch id is a small integer
    /// and the screen asked the director to type one. Same rule, same reason, as
    /// <c>IStoreCartRepository.GetLineItemInStoreAsync</c>.
    /// </para>
    Task<StoreCartBatches?> GetBatchInStoreAsync(
        int storeCartBatchId, int storeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Persist all pending changes.
    /// </summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    // ── Sales operations ──

    /// <summary>
    /// Every PURCHASED LINE in the store — the grain of legacy's StoreSales grid, and the row the
    /// Swap and Refund commands act on. <paramref name="walkUpOnly"/> narrows to counter sales
    /// (legacy StoreSalesWalkup/Index).
    /// </summary>
    Task<List<StoreSaleLineDto>> GetSaleLinesAsync(
        int storeId, bool walkUpOnly, CancellationToken cancellationToken = default);

    /// <summary>
    /// One purchased line, TRACKED, with its SKU and owning batch — for swap and refund.
    /// </summary>
    Task<StoreCartBatchSkus?> GetTrackedLineAsync(
        int storeCartBatchSkuId, CancellationToken cancellationToken = default);

    /// <summary>Every line on a batch, TRACKED — a void reverses all of them.</summary>
    Task<List<StoreCartBatchSkus>> GetTrackedBatchLinesAsync(
        int storeCartBatchId, CancellationToken cancellationToken = default);

    /// <summary>
    /// A batch's accounting rows, TRACKED, OLDEST FIRST so the original charge is identifiable.
    /// </summary>
    Task<List<StoreCartBatchAccounting>> GetTrackedBatchAccountingAsync(
        int storeCartBatchId, CancellationToken cancellationToken = default);

    void AddAccounting(StoreCartBatchAccounting accounting);

    /// <summary>Record that one line was split off another by a SKU swap.</summary>
    void AddSkuEdit(StoreCartBatchSkuEdits edit);

    /// <summary>Add the new line a partial swap splits off.</summary>
    void AddLine(StoreCartBatchSkus line);
}
