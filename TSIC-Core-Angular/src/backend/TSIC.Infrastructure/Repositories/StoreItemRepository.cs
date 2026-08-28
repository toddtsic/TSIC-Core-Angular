using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Dtos.Store;
using TSIC.Contracts.Repositories;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Data.SqlDbContext;

namespace TSIC.Infrastructure.Repositories;

/// <summary>
/// Repository for StoreItems and StoreItemSkus entity data access.
/// </summary>
public class StoreItemRepository : IStoreItemRepository
{
    private readonly SqlDbContext _context;

    public StoreItemRepository(SqlDbContext context)
    {
        _context = context;
    }

    // ── Items ──

    public async Task<List<StoreItemSummaryDto>> GetItemSummariesAsync(
        int storeId, CancellationToken cancellationToken = default)
    {
        return await _context.StoreItems
            .Where(i => i.StoreId == storeId)
            // LEGACY storefront order (IStoreService:360):
            //   .OrderBy(item => (item.sortOrder == 0) ? 10000 : item.sortOrder)
            //   .ThenBy(item => item.storeItemName)
            // SortOrder 0 means "unranked" and sorts LAST, not first. Plain OrderBy(SortOrder)
            // put every newly created item at the head of the catalog.
            .OrderBy(i => i.SortOrder == 0 ? 10000 : i.SortOrder)
            .ThenBy(i => i.StoreItemName)
            .Select(i => new StoreItemSummaryDto
            {
                StoreItemId = i.StoreItemId,
                StoreId = i.StoreId,
                StoreItemName = i.StoreItemName,
                StoreItemPrice = i.StoreItemPrice,
                Active = i.Active,
                SortOrder = i.SortOrder,
                SkuCount = i.StoreItemSkus.Count,
                ActiveSkuCount = i.StoreItemSkus.Count(s => s.Active),
                ImageUrls = i.StoreItemImage
                    .OrderBy(img => img.DisplayOrder)
                    .Select(img => img.ImageUrl)
                    .ToList(),
                SingleSkuId = i.StoreItemSkus.Count(s => s.Active) == 1
                    ? i.StoreItemSkus.First(s => s.Active).StoreSkuId
                    : (int?)null
            })
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public async Task<StoreItemDto?> GetItemWithSkusAsync(
        int storeItemId, int storeId, CancellationToken cancellationToken = default)
    {
        var item = await _context.StoreItems
            .Where(i => i.StoreItemId == storeItemId && i.StoreId == storeId)
            .Select(i => new ItemShell
            {
                StoreItemId = i.StoreItemId,
                StoreId = i.StoreId,
                StoreItemName = i.StoreItemName,
                StoreItemComments = i.StoreItemComments,
                StoreItemPrice = i.StoreItemPrice,
                Active = i.Active,
                SortOrder = i.SortOrder,
                Modified = i.Modified,
                // Same legacy count semantics as GetSkusWithAvailabilityAsync - see ToSkuDto.
                Skus = i.StoreItemSkus
                    .OrderBy(sku => sku.StoreSize!.StoreSizeName)
                    .ThenBy(sku => sku.StoreColor!.StoreColorName)
                    .Select(sku => new SkuCountsRow(
                        sku.StoreSkuId,
                        sku.StoreItemId,
                        i.StoreItemName,
                        sku.StoreColorId,
                        sku.StoreColor != null ? sku.StoreColor.StoreColorName : null,
                        sku.StoreSizeId,
                        sku.StoreSize != null ? sku.StoreSize.StoreSizeName : null,
                        sku.Active,
                        sku.MaxCanSell,
                        sku.StoreCartBatchSkus
                            .Where(cbs => cbs.StoreCartBatch.StoreCartBatchAccounting.Any())
                            .Sum(cbs => cbs.Quantity - cbs.Restocked),
                        sku.StoreCartBatchSkus
                            .Where(cbs => cbs.PaidTotal == 0)
                            .Sum(cbs => cbs.Quantity - cbs.Restocked),
                        sku.StoreCartBatchSkus
                            .Where(cbs => cbs.StoreCartBatch.StoreCartBatchAccounting.Any()
                                && cbs.StoreCartBatch.SignedForDate != null)
                            .Sum(cbs => cbs.Quantity - cbs.Restocked)))
                    .ToList(),
                ImageUrls = i.StoreItemImage
                    .OrderBy(img => img.DisplayOrder)
                    .Select(img => img.ImageUrl)
                    .ToList()
            })
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken);

        if (item == null) return null;

        return new StoreItemDto
        {
            StoreItemId = item.StoreItemId,
            StoreId = item.StoreId,
            StoreItemName = item.StoreItemName,
            StoreItemComments = item.StoreItemComments,
            StoreItemPrice = item.StoreItemPrice,
            Active = item.Active,
            SortOrder = item.SortOrder,
            Modified = item.Modified,
            Skus = item.Skus.Select(ToSkuDto).ToList(),
            ImageUrls = item.ImageUrls
        };
    }

    /// <summary>Database-shaped item projection, before SKU labels are built in memory.</summary>
    private sealed class ItemShell
    {
        public required int StoreItemId { get; init; }
        public required int StoreId { get; init; }
        public required string StoreItemName { get; init; }
        public string? StoreItemComments { get; init; }
        public required decimal StoreItemPrice { get; init; }
        public required bool Active { get; init; }
        public required int SortOrder { get; init; }
        public required DateTime Modified { get; init; }
        public required List<SkuCountsRow> Skus { get; init; }
        public required List<string> ImageUrls { get; init; }
    }

    public async Task<StoreItems?> GetItemByIdAsync(
        int storeItemId, CancellationToken cancellationToken = default)
    {
        return await _context.StoreItems
            .FirstOrDefaultAsync(i => i.StoreItemId == storeItemId, cancellationToken);
    }

    public async Task<StoreItems?> GetItemByNameAsync(
        int storeId, string storeItemName, CancellationToken cancellationToken = default)
    {
        return await _context.StoreItems
            .FirstOrDefaultAsync(
                i => i.StoreId == storeId && i.StoreItemName == storeItemName,
                cancellationToken);
    }

    public void AddItem(StoreItems item)
    {
        _context.StoreItems.Add(item);
    }

    public void AddSkus(IEnumerable<StoreItemSkus> skus)
    {
        _context.StoreItemSkus.AddRange(skus);
    }

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return await _context.SaveChangesAsync(cancellationToken);
    }

    // ── SKUs ──

    public async Task<List<StoreSkuDto>> GetSkusWithAvailabilityAsync(
        int storeItemId, CancellationToken cancellationToken = default)
    {
        var rows = await _context.StoreItemSkus
            .Where(sku => sku.StoreItemId == storeItemId)
            .OrderBy(sku => sku.StoreItem.StoreItemName)
            .ThenBy(sku => sku.StoreSize!.StoreSizeName)
            .ThenBy(sku => sku.StoreColor!.StoreColorName)
            .Select(sku => new SkuCountsRow(
                sku.StoreSkuId,
                sku.StoreItemId,
                sku.StoreItem.StoreItemName,
                sku.StoreColorId,
                sku.StoreColor != null ? sku.StoreColor.StoreColorName : null,
                sku.StoreSizeId,
                sku.StoreSize != null ? sku.StoreSize.StoreSizeName : null,
                sku.Active,
                sku.MaxCanSell,
                sku.StoreCartBatchSkus
                    .Where(cbs => cbs.StoreCartBatch.StoreCartBatchAccounting.Any())
                    .Sum(cbs => cbs.Quantity - cbs.Restocked),
                sku.StoreCartBatchSkus
                    .Where(cbs => cbs.PaidTotal == 0)
                    .Sum(cbs => cbs.Quantity - cbs.Restocked),
                sku.StoreCartBatchSkus
                    .Where(cbs => cbs.StoreCartBatch.StoreCartBatchAccounting.Any()
                        && cbs.StoreCartBatch.SignedForDate != null)
                    .Sum(cbs => cbs.Quantity - cbs.Restocked)))
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return rows.Select(ToSkuDto).ToList();
    }

    /// <summary>
    /// Raw per-SKU counts straight from the database, before the in-memory label build.
    /// Kept separate because the legacy label needs Trim(char), which EF cannot translate.
    /// </summary>
    private sealed record SkuCountsRow(
        int StoreSkuId, int StoreItemId, string ItemName,
        int? ColorId, string? ColorName, int? SizeId, string? SizeName,
        bool Active, int MaxCanSell, int Sold, int InCart, int PickedUp);

    /// <summary>
    /// LEGACY COUNT SEMANTICS (IStoreService.CartBatchSkuItemsSold / …SignedFor / …InCarts):
    ///   Sold     = SUM(Quantity - Restocked) over batches that HAVE accounting rows
    ///   InCart   = SUM(Quantity - Restocked) where PaidTotal = 0
    ///   PickedUp = Sold, further restricted to batches with a SignedForDate
    ///   UnSold   = MaxCanSell - Sold           (NO in-cart deduction - stock on hand)
    /// Two things we previously got wrong: restocked units were counted as still sold, and the
    /// counts were filtered on StoreCartBatchSkus.Active, which legacy never filters on.
    /// </summary>
    private static StoreSkuDto ToSkuDto(SkuCountsRow r) => new()
    {
        StoreSkuId = r.StoreSkuId,
        StoreItemId = r.StoreItemId,
        StoreColorId = r.ColorId,
        StoreColorName = r.ColorName,
        StoreSizeId = r.SizeId,
        StoreSizeName = r.SizeName,
        Active = r.Active,
        MaxCanSell = r.MaxCanSell,
        SoldCount = r.Sold,
        InCartCount = r.InCart,
        PickedUpCount = r.PickedUp,
        UnSoldCount = r.MaxCanSell - r.Sold,
        AvailableCount = r.MaxCanSell - r.Sold - r.InCart,
        SkuLabel = BuildSkuLabel(r.ItemName, r.SizeName, r.ColorName)
    };

    /// <summary>
    /// Legacy SkuLabel: "Item:Size:Color", collapsing "::" and trimming stray colons so a SKU
    /// missing a dimension reads "Item:Large" rather than "Item:Large:".
    /// </summary>
    private static string BuildSkuLabel(string itemName, string? sizeName, string? colorName) =>
        $"{itemName}:{sizeName}:{colorName}".Replace("::", ":").Trim(':');

    public async Task<StoreItemSkus?> GetSkuByIdAsync(
        int storeSkuId, CancellationToken cancellationToken = default)
    {
        // StoreItem is included because availability depends on the PARENT item's Active flag,
        // not just the SKU's - legacy StoreItemSkuMaxCanSell returns
        //   (s.Active && s.StoreItem.Active) ? s.MaxCanSell : 0
        // so deactivating an item zeroes the stock of every SKU under it.
        return await _context.StoreItemSkus
            .Include(s => s.StoreItem)
            .FirstOrDefaultAsync(s => s.StoreSkuId == storeSkuId, cancellationToken);
    }

    public async Task<int> GetSoldCountAsync(
        int storeSkuId, CancellationToken cancellationToken = default)
    {
        return await _context.StoreCartBatchSkus
            .Where(cbs => cbs.StoreSkuId == storeSkuId
                && cbs.Active
                && cbs.StoreCartBatch.StoreCartBatchAccounting.Any())
            .SumAsync(cbs => cbs.Quantity, cancellationToken);
    }

    public async Task<int> GetInCartCountAsync(
        int storeSkuId, CancellationToken cancellationToken = default)
    {
        return await _context.StoreCartBatchSkus
            .Where(cbs => cbs.StoreSkuId == storeSkuId
                && cbs.Active
                && !cbs.StoreCartBatch.StoreCartBatchAccounting.Any())
            .SumAsync(cbs => cbs.Quantity, cancellationToken);
    }

    // ── Deletion ──

    public async Task<List<StoreItemSkus>> GetSkusForItemAsync(
        int storeItemId, CancellationToken cancellationToken = default)
    {
        return await _context.StoreItemSkus
            .Where(sku => sku.StoreItemId == storeItemId)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> IsSkuReferencedAsync(
        int storeSkuId, CancellationToken cancellationToken = default)
    {
        return await _context.StoreCartBatchSkus
            .AnyAsync(cbs => cbs.StoreSkuId == storeSkuId, cancellationToken);
    }

    public async Task<bool> IsItemReferencedAsync(
        int storeItemId, CancellationToken cancellationToken = default)
    {
        return await _context.StoreCartBatchSkus
            .AnyAsync(cbs => cbs.StoreSku.StoreItemId == storeItemId, cancellationToken);
    }

    public void RemoveSku(StoreItemSkus sku)
    {
        _context.StoreItemSkus.Remove(sku);
    }

    public void RemoveSkus(IEnumerable<StoreItemSkus> skus)
    {
        _context.StoreItemSkus.RemoveRange(skus);
    }

    public void RemoveItem(StoreItems item)
    {
        _context.StoreItems.Remove(item);
    }
}
