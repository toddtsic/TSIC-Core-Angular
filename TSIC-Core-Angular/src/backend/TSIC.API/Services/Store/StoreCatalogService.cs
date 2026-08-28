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
            Modified = DateTime.Now,
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

        // LEGACY (StoreItemsController.GetOrCreateStoreItemAsync): items are get-or-create by
        // StoreId + StoreItemName. Creating with an existing name REUSES that item and adds any
        // missing SKUs — it does NOT create a duplicate, and it does NOT update price or
        // comments on the existing row.
        var item = await _itemRepo.GetItemByNameAsync(storeDto.StoreId, request.StoreItemName);

        if (item == null)
        {
            item = new StoreItems
            {
                StoreId = storeDto.StoreId,
                StoreItemName = request.StoreItemName,
                // Legacy's create modal does not collect comments — the field is commented out of
                // the Razor and the POST sends null. request.StoreItemComments is ignored here.
                StoreItemComments = null,
                StoreItemPrice = request.StoreItemPrice,
                Active = true,
                SortOrder = 0,
                Modified = DateTime.Now,
                LebUserId = userId
            };

            _itemRepo.AddItem(item);
            await _itemRepo.SaveChangesAsync();
        }

        // Existing SKUs are skipped, matching legacy's per-combination skuExists check.
        var existing = await _itemRepo.GetSkusWithAvailabilityAsync(item.StoreItemId);

        var skus = GenerateSkuMatrix(item.StoreItemId, request.ColorIds, request.SizeIds,
            existing, userId);

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

        // LEGACY (StoreItemsController.UpdateItem): "Update active flag and sort order only".
        // Name, comments and price are read-only after creation — the legacy grid marks
        // StoreItemName allowEditing="false" and the action never assigns price or comments.
        // request.StoreItemName / StoreItemComments / StoreItemPrice are deliberately ignored.
        item.Active = request.Active;
        item.SortOrder = request.SortOrder;
        item.Modified = DateTime.Now;
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
        sku.Modified = DateTime.Now;
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
            Modified = DateTime.Now,
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
        color.Modified = DateTime.Now;
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
            Modified = DateTime.Now,
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
        size.Modified = DateTime.Now;
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
    // LEGACY (StoreItemsController.CreateSkusAsync): iterate SIZE outer, COLOUR inner — the
    // insertion order determines StoreSkuId assignment. Skip any combination that already
    // exists. New SKUs are born Active = true, MaxCanSell = 0; legacy has no MaxCanSell field at
    // creation (it is set afterwards on the Skus screen), so request.MaxCanSell is not used.
    private static List<StoreItemSkus> GenerateSkuMatrix(
        int storeItemId, List<int> colorIds, List<int> sizeIds,
        List<StoreSkuDto> existing, string userId)
    {
        var skus = new List<StoreItemSkus>();
        var now = DateTime.Now;

        bool Exists(int? sizeId, int? colorId) =>
            existing.Any(e => e.StoreSizeId == sizeId && e.StoreColorId == colorId);

        StoreItemSkus New(int? sizeId, int? colorId) => new()
        {
            StoreItemId = storeItemId,
            StoreSizeId = sizeId,
            StoreColorId = colorId,
            Active = true,
            MaxCanSell = 0,
            Modified = now,
            LebUserId = userId
        };

        if (sizeIds.Count > 0 && colorIds.Count > 0)
        {
            foreach (var sizeId in sizeIds)
            {
                foreach (var colorId in colorIds)
                {
                    if (!Exists(sizeId, colorId)) skus.Add(New(sizeId, colorId));
                }
            }
        }
        else if (sizeIds.Count > 0)
        {
            foreach (var sizeId in sizeIds)
            {
                if (!Exists(sizeId, null)) skus.Add(New(sizeId, null));
            }
        }
        else if (colorIds.Count > 0)
        {
            foreach (var colorId in colorIds)
            {
                if (!Exists(null, colorId)) skus.Add(New(null, colorId));
            }
        }
        else if (!Exists(null, null))
        {
            skus.Add(New(null, null));
        }

        return skus;
    }
}
