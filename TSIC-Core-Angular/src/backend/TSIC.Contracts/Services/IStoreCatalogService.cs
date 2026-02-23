using TSIC.Contracts.Dtos.Store;

namespace TSIC.Contracts.Services;

/// <summary>
/// Service for store catalog management (admin operations).
/// Handles store auto-creation, item CRUD with SKU matrix, colors, sizes, and images.
/// </summary>
public interface IStoreCatalogService
{
    // ── Store ──

    /// <summary>
    /// Get store for job, auto-creating if none exists.
    /// </summary>
    Task<StoreDto> GetOrCreateStoreAsync(Guid jobId, string userId);

    // ── Items ──

    /// <summary>
    /// List all items for a job's store (summary view).
    /// </summary>
    Task<List<StoreItemSummaryDto>> GetItemsAsync(Guid jobId);

    /// <summary>
    /// Get a single item with full SKU details and availability.
    /// </summary>
    Task<StoreItemDto?> GetItemDetailAsync(Guid jobId, int storeItemId);

    /// <summary>
    /// Create item with automatic SKU matrix (size x color cross-product).
    /// </summary>
    Task<StoreItemDto> CreateItemAsync(Guid jobId, string userId, CreateStoreItemRequest request);

    /// <summary>
    /// Update item properties (name, price, comments, active, sort order).
    /// </summary>
    Task<StoreItemDto> UpdateItemAsync(Guid jobId, string userId, int storeItemId, UpdateStoreItemRequest request);

    // ── SKUs ──

    /// <summary>
    /// Get SKUs for an item with availability info.
    /// </summary>
    Task<List<StoreSkuDto>> GetSkusAsync(int storeItemId);

    /// <summary>
    /// Update SKU (active, MaxCanSell).
    /// </summary>
    Task<StoreSkuDto> UpdateSkuAsync(string userId, int storeSkuId, UpdateStoreSkuRequest request);

    // ── Colors ──

    Task<List<StoreColorDto>> GetColorsAsync();
    Task<StoreColorDto> CreateColorAsync(string userId, CreateStoreColorRequest request);
    Task<StoreColorDto> UpdateColorAsync(string userId, int storeColorId, UpdateStoreColorRequest request);
    Task DeleteColorAsync(int storeColorId);

    // ── Sizes ──

    Task<List<StoreSizeDto>> GetSizesAsync();
    Task<StoreSizeDto> CreateSizeAsync(string userId, CreateStoreSizeRequest request);
    Task<StoreSizeDto> UpdateSizeAsync(string userId, int storeSizeId, UpdateStoreSizeRequest request);
    Task DeleteSizeAsync(int storeSizeId);
}
