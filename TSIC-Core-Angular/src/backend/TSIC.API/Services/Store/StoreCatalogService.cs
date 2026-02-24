using TSIC.Contracts.Dtos.Store;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Entities;

namespace TSIC.API.Services.Store;

/// <summary>
/// Service for store catalog management (admin operations).
/// </summary>
public sealed class StoreCatalogService : IStoreCatalogService
{
    private readonly IStoreRepository _storeRepo;
    private readonly IStoreItemRepository _itemRepo;

    public StoreCatalogService(
        IStoreRepository storeRepo,
        IStoreItemRepository itemRepo)
    {
        _storeRepo = storeRepo;
        _itemRepo = itemRepo;
    }

    // ── Store ──

    public async Task<StoreDto> GetOrCreateStoreAsync(Guid jobId, string userId)
    {
        var store = await _storeRepo.GetByJobIdAsync(jobId);

        if (store != null)
        {
            return new StoreDto { StoreId = store.StoreId, JobId = store.JobId };
        }

        var newStore = new Stores
        {
            JobId = jobId,
            Modified = DateTime.UtcNow,
            LebUserId = userId
        };

        _storeRepo.Add(newStore);
        await _storeRepo.SaveChangesAsync();

        return new StoreDto { StoreId = newStore.StoreId, JobId = newStore.JobId };
    }

    // ── Items ──

    public async Task<List<StoreItemSummaryDto>> GetItemsAsync(Guid jobId)
    {
        var store = await _storeRepo.GetByJobIdAsync(jobId);
        if (store == null) return [];

        return await _itemRepo.GetItemSummariesAsync(store.StoreId);
    }

    public async Task<StoreItemDto?> GetItemDetailAsync(Guid jobId, int storeItemId)
    {
        var store = await _storeRepo.GetByJobIdAsync(jobId);
        if (store == null) return null;

        return await _itemRepo.GetItemWithSkusAsync(storeItemId, store.StoreId);
    }

    public async Task<StoreItemDto> CreateItemAsync(
        Guid jobId, string userId, CreateStoreItemRequest request)
    {
        // Get or create store
        var storeDto = await GetOrCreateStoreAsync(jobId, userId);

        // Create the item
        var item = new StoreItems
        {
            StoreId = storeDto.StoreId,
            StoreItemName = request.StoreItemName,
            StoreItemComments = request.StoreItemComments,
            StoreItemPrice = request.StoreItemPrice,
            Active = true,
            SortOrder = 0,
            Modified = DateTime.UtcNow,
            LebUserId = userId
        };

        _itemRepo.AddItem(item);
        await _itemRepo.SaveChangesAsync();

        // Generate SKU matrix
        var skus = GenerateSkuMatrix(item.StoreItemId, request.ColorIds, request.SizeIds,
            request.MaxCanSell, userId);

        if (skus.Count > 0)
        {
            _itemRepo.AddSkus(skus);
            await _itemRepo.SaveChangesAsync();
        }

        // Return the created item with its SKUs
        return (await _itemRepo.GetItemWithSkusAsync(item.StoreItemId, storeDto.StoreId))!;
    }

    public async Task<StoreItemDto> UpdateItemAsync(
        Guid jobId, string userId, int storeItemId, UpdateStoreItemRequest request)
    {
        var store = await _storeRepo.GetByJobIdAsync(jobId)
            ?? throw new InvalidOperationException("Store not found for this job.");

        var item = await _itemRepo.GetItemByIdAsync(storeItemId)
            ?? throw new InvalidOperationException($"Item {storeItemId} not found.");

        item.StoreItemName = request.StoreItemName;
        item.StoreItemComments = request.StoreItemComments;
        item.StoreItemPrice = request.StoreItemPrice;
        item.Active = request.Active;
        item.SortOrder = request.SortOrder;
        item.Modified = DateTime.UtcNow;
        item.LebUserId = userId;

        await _itemRepo.SaveChangesAsync();

        return (await _itemRepo.GetItemWithSkusAsync(storeItemId, store.StoreId))!;
    }

    // ── SKUs ──

    public async Task<List<StoreSkuDto>> GetSkusAsync(int storeItemId)
    {
        return await _itemRepo.GetSkusWithAvailabilityAsync(storeItemId);
    }

    public async Task<StoreSkuDto> UpdateSkuAsync(
        string userId, int storeSkuId, UpdateStoreSkuRequest request)
    {
        var sku = await _itemRepo.GetSkuByIdAsync(storeSkuId)
            ?? throw new InvalidOperationException($"SKU {storeSkuId} not found.");

        sku.Active = request.Active;
        sku.MaxCanSell = request.MaxCanSell;
        sku.Modified = DateTime.UtcNow;
        sku.LebUserId = userId;

        await _itemRepo.SaveChangesAsync();

        // Return updated SKU with availability
        var skus = await _itemRepo.GetSkusWithAvailabilityAsync(sku.StoreItemId);
        return skus.First(s => s.StoreSkuId == storeSkuId);
    }

    // ── Colors ──

    public async Task<List<StoreColorDto>> GetColorsAsync()
    {
        var colors = await _storeRepo.GetAllColorsAsync();
        return colors.Select(c => new StoreColorDto
        {
            StoreColorId = c.StoreColorId,
            StoreColorName = c.StoreColorName
        }).ToList();
    }

    public async Task<StoreColorDto> CreateColorAsync(
        string userId, CreateStoreColorRequest request)
    {
        var color = new StoreColors
        {
            StoreColorName = request.StoreColorName,
            Modified = DateTime.UtcNow,
            LebUserId = userId
        };

        _storeRepo.AddColor(color);
        await _storeRepo.SaveChangesAsync();

        return new StoreColorDto
        {
            StoreColorId = color.StoreColorId,
            StoreColorName = color.StoreColorName
        };
    }

    public async Task<StoreColorDto> UpdateColorAsync(
        string userId, int storeColorId, UpdateStoreColorRequest request)
    {
        var color = await _storeRepo.GetColorByIdAsync(storeColorId)
            ?? throw new InvalidOperationException($"Color {storeColorId} not found.");

        color.StoreColorName = request.StoreColorName;
        color.Modified = DateTime.UtcNow;
        color.LebUserId = userId;

        await _storeRepo.SaveChangesAsync();

        return new StoreColorDto
        {
            StoreColorId = color.StoreColorId,
            StoreColorName = color.StoreColorName
        };
    }

    public async Task DeleteColorAsync(int storeColorId)
    {
        var inUse = await _storeRepo.IsColorInUseAsync(storeColorId);
        if (inUse)
            throw new InvalidOperationException("Cannot delete color that is in use by SKUs.");

        var color = await _storeRepo.GetColorByIdAsync(storeColorId)
            ?? throw new InvalidOperationException($"Color {storeColorId} not found.");

        _storeRepo.RemoveColor(color);
        await _storeRepo.SaveChangesAsync();
    }

    // ── Sizes ──

    public async Task<List<StoreSizeDto>> GetSizesAsync()
    {
        var sizes = await _storeRepo.GetAllSizesAsync();
        return sizes.Select(s => new StoreSizeDto
        {
            StoreSizeId = s.StoreSizeId,
            StoreSizeName = s.StoreSizeName
        }).ToList();
    }

    public async Task<StoreSizeDto> CreateSizeAsync(
        string userId, CreateStoreSizeRequest request)
    {
        var size = new StoreSizes
        {
            StoreSizeName = request.StoreSizeName,
            Modified = DateTime.UtcNow,
            LebUserId = userId
        };

        _storeRepo.AddSize(size);
        await _storeRepo.SaveChangesAsync();

        return new StoreSizeDto
        {
            StoreSizeId = size.StoreSizeId,
            StoreSizeName = size.StoreSizeName
        };
    }

    public async Task<StoreSizeDto> UpdateSizeAsync(
        string userId, int storeSizeId, UpdateStoreSizeRequest request)
    {
        var size = await _storeRepo.GetSizeByIdAsync(storeSizeId)
            ?? throw new InvalidOperationException($"Size {storeSizeId} not found.");

        size.StoreSizeName = request.StoreSizeName;
        size.Modified = DateTime.UtcNow;
        size.LebUserId = userId;

        await _storeRepo.SaveChangesAsync();

        return new StoreSizeDto
        {
            StoreSizeId = size.StoreSizeId,
            StoreSizeName = size.StoreSizeName
        };
    }

    public async Task DeleteSizeAsync(int storeSizeId)
    {
        var inUse = await _storeRepo.IsSizeInUseAsync(storeSizeId);
        if (inUse)
            throw new InvalidOperationException("Cannot delete size that is in use by SKUs.");

        var size = await _storeRepo.GetSizeByIdAsync(storeSizeId)
            ?? throw new InvalidOperationException($"Size {storeSizeId} not found.");

        _storeRepo.RemoveSize(size);
        await _storeRepo.SaveChangesAsync();
    }

    // ── Private helpers ──

    /// <summary>
    /// Generate SKU matrix from color and size lists.
    /// Colors × Sizes → one SKU per combination.
    /// Colors only → one SKU per color (no size).
    /// Sizes only → one SKU per size (no color).
    /// Neither → one default SKU.
    /// </summary>
    private static List<StoreItemSkus> GenerateSkuMatrix(
        int storeItemId, List<int> colorIds, List<int> sizeIds, int maxCanSell, string userId)
    {
        var skus = new List<StoreItemSkus>();
        var now = DateTime.UtcNow;

        if (colorIds.Count > 0 && sizeIds.Count > 0)
        {
            // Cross-product: Color × Size
            foreach (var colorId in colorIds)
            {
                foreach (var sizeId in sizeIds)
                {
                    skus.Add(new StoreItemSkus
                    {
                        StoreItemId = storeItemId,
                        StoreColorId = colorId,
                        StoreSizeId = sizeId,
                        Active = true,
                        MaxCanSell = maxCanSell,
                        Modified = now,
                        LebUserId = userId
                    });
                }
            }
        }
        else if (colorIds.Count > 0)
        {
            // Colors only
            foreach (var colorId in colorIds)
            {
                skus.Add(new StoreItemSkus
                {
                    StoreItemId = storeItemId,
                    StoreColorId = colorId,
                    StoreSizeId = null,
                    Active = true,
                    MaxCanSell = maxCanSell,
                    Modified = now,
                    LebUserId = userId
                });
            }
        }
        else if (sizeIds.Count > 0)
        {
            // Sizes only
            foreach (var sizeId in sizeIds)
            {
                skus.Add(new StoreItemSkus
                {
                    StoreItemId = storeItemId,
                    StoreColorId = null,
                    StoreSizeId = sizeId,
                    Active = true,
                    MaxCanSell = maxCanSell,
                    Modified = now,
                    LebUserId = userId
                });
            }
        }
        else
        {
            // Default SKU (no variant)
            skus.Add(new StoreItemSkus
            {
                StoreItemId = storeItemId,
                StoreColorId = null,
                StoreSizeId = null,
                Active = true,
                MaxCanSell = maxCanSell,
                Modified = now,
                LebUserId = userId
            });
        }

        return skus;
    }
}
