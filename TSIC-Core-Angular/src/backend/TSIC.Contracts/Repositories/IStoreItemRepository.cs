using TSIC.Contracts.Dtos.Store;
using TSIC.Domain.Entities;

namespace TSIC.Contracts.Repositories;

/// <summary>
/// Repository for StoreItems and StoreItemSkus entity data access.
/// </summary>
public interface IStoreItemRepository
{
    // ── Items ──

    /// <summary>
    /// Get all items for a store as summary DTOs (no SKU details).
    /// </summary>
    Task<List<StoreItemSummaryDto>> GetItemSummariesAsync(int storeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a single item with its SKUs, color/size names, and availability counts.
    /// Complex multi-join query returning DTO directly.
    /// </summary>
    Task<StoreItemDto?> GetItemWithSkusAsync(int storeItemId, int storeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a tracked item entity for updates.
    /// </summary>
    Task<StoreItems?> GetItemByIdAsync(int storeItemId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Find an item by name within a store. Legacy creates items get-or-create by
    /// StoreId + StoreItemName (StoreItemsController.GetOrCreateStoreItemAsync), so a
    /// second create with an existing name adds SKUs rather than a duplicate item.
    /// </summary>
    Task<StoreItems?> GetItemByNameAsync(int storeId, string storeItemName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Add a new item.
    /// </summary>
    void AddItem(StoreItems item);

    /// <summary>
    /// Batch add SKUs (used during SKU matrix creation).
    /// </summary>
    void AddSkus(IEnumerable<StoreItemSkus> skus);

    /// <summary>
    /// Persist all pending changes.
    /// </summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    // ── SKUs ──

    /// <summary>
    /// Get SKUs for an item with color/size names and pre-computed availability counts.
    /// Complex multi-join query returning DTOs.
    /// </summary>
    Task<List<StoreSkuDto>> GetSkusWithAvailabilityAsync(int storeItemId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every SKU in a store, same shape and same counts as the per-item read. Backs the
    /// Skus grid's Excel export, which is store-wide.
    /// </summary>
    Task<List<StoreSkuDto>> GetAllSkusWithAvailabilityAsync(int storeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a tracked SKU entity for updates.
    /// </summary>
    Task<StoreItemSkus?> GetSkuByIdAsync(int storeSkuId, CancellationToken cancellationToken = default);

    /// <summary>
    /// How many of each SKU may be sold AT ALL, in one query.
    /// </summary>
    /// <remarks>
    /// Legacy <c>StoreItemSkuMaxCanSell</c>: <c>(sku.Active AND item.Active) ? MaxCanSell : 0</c>.
    /// Deactivating the parent ITEM takes every one of its SKUs off the shelf, so a raw
    /// <c>MaxCanSell</c> read is not the ceiling — it is the ceiling only while both flags hold.
    /// SKU ids with no row are absent from the dictionary; treat a miss as zero.
    /// </remarks>
    Task<Dictionary<int, int>> GetEffectiveMaxCanSellAsync(
        List<int> storeSkuIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Count sold items for a specific SKU.
    /// Sold = Active line items in batches that have accounting records (paid).
    /// </summary>
    Task<int> GetSoldCountAsync(int storeSkuId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Count items sitting in unpaid carts for a specific SKU.
    /// InCart = Active line items in batches that have NO accounting records.
    /// </summary>
    Task<int> GetInCartCountAsync(int storeSkuId, CancellationToken cancellationToken = default);

    /// <summary>
    /// All SKUs belonging to an item, tracked, for deletion.
    /// </summary>
    Task<List<StoreItemSkus>> GetSkusForItemAsync(int storeItemId, CancellationToken cancellationToken = default);

    /// <summary>
    /// True when any cart line — paid or unpaid — references this SKU. A referenced SKU cannot be
    /// deleted: the row is part of a purchase record.
    /// </summary>
    Task<bool> IsSkuReferencedAsync(int storeSkuId, CancellationToken cancellationToken = default);

    /// <summary>
    /// True when any cart line references any SKU of this item.
    /// </summary>
    Task<bool> IsItemReferencedAsync(int storeItemId, CancellationToken cancellationToken = default);

    void RemoveSku(StoreItemSkus sku);

    void RemoveSkus(IEnumerable<StoreItemSkus> skus);

    void RemoveItem(StoreItems item);

    // ── Images ──

    /// <summary>
    /// Id + name for every item in a store. The images surface is job-wide and lists every item,
    /// including ones with no photo, so it needs the full roster of items rather than a page of
    /// them (legacy IStoreService.GetJobItemsPictures).
    /// </summary>
    Task<List<StoreItemKeyDto>> GetItemKeysForStoreAsync(int storeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tracked StoreItemImage rows for these items, so the index can be re-synced from disk.
    /// </summary>
    Task<List<StoreItemImage>> GetImageRowsForItemsAsync(IEnumerable<int> storeItemIds, CancellationToken cancellationToken = default);

    void AddImageRows(IEnumerable<StoreItemImage> images);

    void RemoveImageRows(IEnumerable<StoreItemImage> images);
}
