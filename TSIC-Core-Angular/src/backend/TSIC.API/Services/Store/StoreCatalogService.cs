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

    public Task<StoreFrontInfoDto> GetStoreFrontInfoAsync(Guid jobId, CancellationToken ct = default)
        => _storeRepo.GetStoreFrontInfoAsync(jobId, ct);

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
                // Legacy assigns model.ItemComments here. The create modal does not collect it
                // (the field is commented out of the Razor), so in practice this is null.
                StoreItemComments = request.StoreItemComments,
                StoreItemPrice = request.StoreItemPrice,
                Active = true,
                SortOrder = 0,
                Modified = DateTime.Now,
                LebUserId = userId
            };

            _itemRepo.AddItem(item);
            await _itemRepo.SaveChangesAsync();
        }

        // Legacy resolves size/colour NAMES against the global dictionaries, creating any that
        // do not exist (ProcessSizesAsync / ProcessColorsAsync).
        var sizeIds = await ResolveSizeIdsAsync(request.ItemSizes, userId);
        var colorIds = await ResolveColorIdsAsync(request.ItemColors, userId);

        // Existing SKUs are skipped, matching legacy's per-combination skuExists check.
        var existing = await _itemRepo.GetSkusWithAvailabilityAsync(item.StoreItemId);

        var skus = GenerateSkuMatrix(item.StoreItemId, colorIds, sizeIds, existing, userId);

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

    public async Task<List<StoreSkuDto>> GetSkusAsync(Guid jobId, int storeItemId)
    {
        // The same assert the delete paths already used. It was written; these two just did not
        // call it, which is why a store admin could read and write another job's stock.
        await AssertItemBelongsToJobAsync(jobId, storeItemId);
        return await _itemRepo.GetSkusWithAvailabilityAsync(storeItemId);
    }

    public async Task<StoreSkuDto> UpdateSkuAsync(
        Guid jobId, string userId, int storeSkuId, UpdateStoreSkuRequest request)
    {
        var store = await _storeRepo.GetByJobIdAsync(jobId)
            ?? throw new InvalidOperationException("Store not found for this job.");

        var sku = await _itemRepo.GetSkuInStoreAsync(storeSkuId, store.StoreId)
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

    /// <summary>
    /// LEGACY (StoreSkusController.UpdateSku, action "remove"): delete a single SKU row outright.
    ///
    /// Legacy calls Remove and lets a foreign-key violation surface as an unhandled exception when
    /// the SKU has been sold. We refuse up front with a message instead — same outcome, deletion
    /// blocked, but the caller learns why. A SKU referenced by a cart line is part of a purchase
    /// record and must never be removed.
    /// </summary>
    public async Task DeleteSkuAsync(Guid jobId, int storeSkuId)
    {
        var store = await _storeRepo.GetByJobIdAsync(jobId)
            ?? throw new InvalidOperationException("Store not found for this job.");

        var sku = await _itemRepo.GetSkuInStoreAsync(storeSkuId, store.StoreId)
            ?? throw new InvalidOperationException($"SKU {storeSkuId} not found.");

        if (await _itemRepo.IsSkuReferencedAsync(storeSkuId))
            throw new InvalidOperationException(
                "Cannot delete a SKU that appears in a cart or purchase. Deactivate it instead.");

        _itemRepo.RemoveSku(sku);
        await _itemRepo.SaveChangesAsync();
    }

    /// <summary>
    /// LEGACY (StoreSkusController.UpdateSku, action "batch"): remove every SKU of the item first,
    /// then the item itself — the grouped grid deletes a whole product in one gesture.
    /// Refused when any SKU has been sold or is sitting in a cart, for the same reason as above.
    /// </summary>
    public async Task DeleteItemAsync(Guid jobId, int storeItemId)
    {
        var item = await AssertItemBelongsToJobAsync(jobId, storeItemId);

        if (await _itemRepo.IsItemReferencedAsync(storeItemId))
            throw new InvalidOperationException(
                "Cannot delete an item that has been sold or is in a cart. Deactivate it instead.");

        // SKUs first, then the item — the FK runs SKU -> item.
        var skus = await _itemRepo.GetSkusForItemAsync(storeItemId);
        _itemRepo.RemoveSkus(skus);
        _itemRepo.RemoveItem(item);
        await _itemRepo.SaveChangesAsync();
    }

    /// <summary>
    /// Deletion is keyed by StoreItemId/StoreSkuId, neither of which is job-scoped, so confirm the
    /// item belongs to the caller's job before touching it.
    /// </summary>
    private async Task<StoreItems> AssertItemBelongsToJobAsync(Guid jobId, int storeItemId)
    {
        var store = await _storeRepo.GetByJobIdAsync(jobId)
            ?? throw new InvalidOperationException("Store not found for this job.");

        var item = await _itemRepo.GetItemByIdAsync(storeItemId)
            ?? throw new InvalidOperationException($"Item {storeItemId} not found.");

        if (item.StoreId != store.StoreId)
            throw new InvalidOperationException($"Item {storeItemId} not found.");

        return item;
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

    /// <summary>
    /// Get-or-create by name, which is what legacy does (<c>StoreItemsController</c>: look up
    /// <c>StoreColorName</c>, insert only on a miss) and what <see cref="ResolveColorIdsAsync"/>
    /// already did on the item-create path.
    ///
    /// <para>
    /// This one inserted unconditionally, so the Colors tab's Add button could put a second
    /// "Blue" into a dictionary that every store on the platform reads from — indistinguishable
    /// from the first in every dropdown thereafter, and impossible to tell apart when picking one
    /// for a SKU. Returning the existing row instead is silent and correct: the director asked
    /// for a colour called Blue and there is one.
    /// </para>
    /// </summary>
    public async Task<StoreColorDto> CreateColorAsync(
        string userId, CreateStoreColorRequest request)
    {
        var name = request.StoreColorName.Trim();

        var color = await _storeRepo.GetColorByNameAsync(name);
        if (color == null)
        {
            color = new StoreColors
            {
                StoreColorName = name,
                Modified = DateTime.Now,
                LebUserId = userId
            };

            _storeRepo.AddColor(color);
            await _storeRepo.SaveChangesAsync();
        }

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

    /// <summary>Get-or-create by name. See <see cref="CreateColorAsync"/> for why.</summary>
    public async Task<StoreSizeDto> CreateSizeAsync(
        string userId, CreateStoreSizeRequest request)
    {
        var name = request.StoreSizeName.Trim();

        var size = await _storeRepo.GetSizeByNameAsync(name);
        if (size == null)
        {
            size = new StoreSizes
            {
                StoreSizeName = name,
                Modified = DateTime.Now,
                LebUserId = userId
            };

            _storeRepo.AddSize(size);
            await _storeRepo.SaveChangesAsync();
        }

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
    /// <summary>
    /// LEGACY (StoreItemsController.ProcessSizesAsync): split on ';' discarding empties, trim each
    /// name, then look it up in the GLOBAL StoreSizes table and create it if absent. No store or
    /// job scoping — every customer shares one dictionary.
    /// </summary>
    private async Task<List<int>> ResolveSizeIdsAsync(string? itemSizes, string userId)
    {
        var ids = new List<int>();
        if (string.IsNullOrWhiteSpace(itemSizes)) return ids;

        foreach (var raw in itemSizes.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var name = raw.Trim();
            var size = await _storeRepo.GetSizeByNameAsync(name);

            if (size == null)
            {
                size = new StoreSizes
                {
                    StoreSizeName = name,
                    Modified = DateTime.Now,
                    LebUserId = userId
                };
                _storeRepo.AddSize(size);
                await _storeRepo.SaveChangesAsync();
            }

            ids.Add(size.StoreSizeId);
        }

        return ids;
    }

    /// <summary>
    /// LEGACY (StoreItemsController.ProcessColorsAsync) — see ResolveSizeIdsAsync.
    /// </summary>
    private async Task<List<int>> ResolveColorIdsAsync(string? itemColors, string userId)
    {
        var ids = new List<int>();
        if (string.IsNullOrWhiteSpace(itemColors)) return ids;

        foreach (var raw in itemColors.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var name = raw.Trim();
            var color = await _storeRepo.GetColorByNameAsync(name);

            if (color == null)
            {
                color = new StoreColors
                {
                    StoreColorName = name,
                    Modified = DateTime.Now,
                    LebUserId = userId
                };
                _storeRepo.AddColor(color);
                await _storeRepo.SaveChangesAsync();
            }

            ids.Add(color.StoreColorId);
        }

        return ids;
    }

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
