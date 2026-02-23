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
    /// Get a tracked SKU entity for updates.
    /// </summary>
    Task<StoreItemSkus?> GetSkuByIdAsync(int storeSkuId, CancellationToken cancellationToken = default);

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
}
