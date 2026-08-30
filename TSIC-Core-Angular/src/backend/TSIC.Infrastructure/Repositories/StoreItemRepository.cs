using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Dtos.Store;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Store;
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
        var rows = await _context.StoreItems
            .Where(i => i.StoreId == storeId)
            // LEGACY storefront order (IStoreService:360):
            //   .OrderBy(item => (item.sortOrder == 0) ? 10000 : item.sortOrder)
            //   .ThenBy(item => item.storeItemName)
            // SortOrder 0 means "unranked" and sorts LAST, not first. Plain OrderBy(SortOrder)
            // put every newly created item at the head of the catalog.
            .OrderBy(i => i.SortOrder == 0 ? 10000 : i.SortOrder)
            .ThenBy(i => i.StoreItemName)
            .Select(i => new SummaryRow(
                i.StoreItemId,
                i.StoreId,
                i.StoreItemName,
                i.StoreItemPrice,
                i.Active,
                i.SortOrder,
                i.StoreItemSkus.Count,
                i.StoreItemSkus.Count(s => s.Active),
                i.StoreItemImage
                    .OrderBy(img => img.DisplayOrder)
                    .Select(img => img.ImageUrl)
                    .ToList(),
                i.StoreItemSkus.Count(s => s.Active) == 1
                    ? i.StoreItemSkus.First(s => s.Active).StoreSkuId
                    : (int?)null,
                // Unbuyable variants: inactive, or availability exhausted. Legacy's availability
                // basis here is sold-only - in-cart units are NOT deducted.
                i.StoreItemSkus
                    .Where(s => !s.Active
                        || s.MaxCanSell - s.StoreCartBatchSkus
                            .Where(cbs => cbs.StoreCartBatch.StoreCartBatchAccounting.Any())
                            .Sum(cbs => cbs.Quantity - cbs.Restocked) < 1)
                    .Select(s => new UnbuyableSku(
                        s.StoreSize != null ? s.StoreSize.StoreSizeName : null,
                        s.StoreColor != null ? s.StoreColor.StoreColorName : null))
                    .ToList(),
                // What the item comes in, for the card. ACTIVE skus only — a shopper should not
                // be shown a colour swatch for a variant that was retired. Distinct is applied
                // in memory below rather than here: EF cannot translate Distinct() inside a
                // projected subquery on every provider, and these lists are a handful of rows.
                i.StoreItemSkus
                    .Where(s => s.Active && s.StoreColor != null)
                    .Select(s => s.StoreColor!.StoreColorName)
                    .ToList(),
                i.StoreItemSkus
                    .Where(s => s.Active && s.StoreSize != null)
                    .Select(s => s.StoreSize!.StoreSizeName)
                    .ToList()))
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return rows.Select(r => new StoreItemSummaryDto
        {
            StoreItemId = r.StoreItemId,
            StoreId = r.StoreId,
            StoreItemName = r.StoreItemName,
            StoreItemPrice = r.StoreItemPrice,
            Active = r.Active,
            SortOrder = r.SortOrder,
            SkuCount = r.SkuCount,
            ActiveSkuCount = r.ActiveSkuCount,
            ImageUrls = r.ImageUrls,
            SingleSkuId = r.SingleSkuId,
            SoldOutOrInactiveSkuLabels = r.Unbuyable
                .Select(u => StoreSkuLabel.Build(r.StoreItemName, u.SizeName, u.ColorName))
                .ToList(),
            // Distinct, first-seen order preserved. The SKU matrix is built SIZE outer / COLOUR
            // inner (B-07), so the sizes already arrive in the order the director entered them —
            // which is the order they want to read as a range.
            ColorNames = r.ColorNames.Distinct().ToList(),
            SizeNames = r.SizeNames.Distinct().ToList()
        }).ToList();
    }

    private sealed record UnbuyableSku(string? SizeName, string? ColorName);

    private sealed record SummaryRow(
        int StoreItemId, int StoreId, string StoreItemName, decimal StoreItemPrice,
        bool Active, int SortOrder, int SkuCount, int ActiveSkuCount,
        List<string> ImageUrls, int? SingleSkuId, List<UnbuyableSku> Unbuyable,
        List<string> ColorNames, List<string> SizeNames);

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
                        i.StoreItemPrice,
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
            Skus = SortRows(item.Skus).Select(ToSkuDto).ToList(),
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

    public Task<List<StoreSkuDto>> GetSkusWithAvailabilityAsync(
        int storeItemId, CancellationToken cancellationToken = default)
        => ProjectSkusAsync(
            _context.StoreItemSkus.Where(sku => sku.StoreItemId == storeItemId),
            cancellationToken);

    public Task<List<StoreSkuDto>> GetAllSkusWithAvailabilityAsync(
        int storeId, CancellationToken cancellationToken = default)
        => ProjectSkusAsync(
            _context.StoreItemSkus.Where(sku => sku.StoreItem.StoreId == storeId),
            cancellationToken);

    /// <summary>
    /// The one SKU projection, shared by the per-item and whole-store reads. The counts carry
    /// legacy's semantics (see <c>ToSkuDto</c>); duplicating them per caller is how two screens
    /// end up reporting different stock for the same SKU.
    /// </summary>
    private static async Task<List<StoreSkuDto>> ProjectSkusAsync(
        IQueryable<StoreItemSkus> query, CancellationToken cancellationToken)
    {
        var rows = await query
            .OrderBy(sku => sku.StoreItem.StoreItemName)
            .ThenBy(sku => sku.StoreSize!.StoreSizeName)
            .ThenBy(sku => sku.StoreColor!.StoreColorName)
            .Select(sku => new SkuCountsRow(
                sku.StoreSkuId,
                sku.StoreItemId,
                sku.StoreItem.StoreItemName,
                sku.StoreItem.StoreItemPrice,
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

        return SortRows(rows).Select(ToSkuDto).ToList();
    }

    /// <summary>
    /// Final SKU order. The database can only sort sizes alphabetically, which lists an Adult
    /// S/M/L/XL shirt as "Adult Large, Adult Medium, Adult Small, Adult XL" - see
    /// <see cref="StoreSizeOrder"/>. Both SKU reads land here, so the storefront and the admin
    /// grid cannot disagree about the order.
    /// </summary>
    private static IEnumerable<SkuCountsRow> SortRows(IEnumerable<SkuCountsRow> rows) =>
        rows.OrderBy(r => r.ItemName)
            .ThenBy(r => StoreSizeOrder.Key(r.SizeName))
            .ThenBy(r => r.ColorName);

    /// <summary>
    /// Raw per-SKU counts straight from the database, before the in-memory label build.
    /// Kept separate because the legacy label needs Trim(char), which EF cannot translate.
    /// </summary>
    private sealed record SkuCountsRow(
        int StoreSkuId, int StoreItemId, string ItemName, decimal Price,
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
        StoreItemName = r.ItemName,
        Price = r.Price,
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
        SkuLabel = StoreSkuLabel.Build(r.ItemName, r.SizeName, r.ColorName)
    };

    public async Task<StoreItemSkus?> GetSkuInStoreAsync(
        int storeSkuId, int storeId, CancellationToken cancellationToken = default)
    {
        // StoreItem is included because availability depends on the PARENT item's Active flag,
        // not just the SKU's - legacy StoreItemSkuMaxCanSell returns
        //   (s.Active && s.StoreItem.Active) ? s.MaxCanSell : 0
        // so deactivating an item zeroes the stock of every SKU under it.
        // The StoreId predicate IS the authorization check — see the interface doc.
        return await _context.StoreItemSkus
            .Include(s => s.StoreItem)
            .FirstOrDefaultAsync(
                s => s.StoreSkuId == storeSkuId && s.StoreItem.StoreId == storeId,
                cancellationToken);
    }

    public async Task<Dictionary<int, int>> GetEffectiveMaxCanSellAsync(
        List<int> storeSkuIds, CancellationToken cancellationToken = default)
    {
        if (storeSkuIds.Count == 0) return [];

        return await _context.StoreItemSkus
            .AsNoTracking()
            .Where(s => storeSkuIds.Contains(s.StoreSkuId))
            .Select(s => new
            {
                s.StoreSkuId,
                MaxCanSell = s.Active && s.StoreItem.Active ? s.MaxCanSell : 0
            })
            .ToDictionaryAsync(s => s.StoreSkuId, s => s.MaxCanSell, cancellationToken);
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

    // ── Images ──

    public async Task<List<StoreItemKeyDto>> GetItemKeysForStoreAsync(
        int storeId, CancellationToken cancellationToken = default)
    {
        return await _context.StoreItems
            .Where(i => i.StoreId == storeId)
            .OrderBy(i => i.StoreItemName)
            .Select(i => new StoreItemKeyDto
            {
                StoreItemId = i.StoreItemId,
                StoreItemName = i.StoreItemName
            })
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public async Task<List<StoreItemImage>> GetImageRowsForItemsAsync(
        IEnumerable<int> storeItemIds, CancellationToken cancellationToken = default)
    {
        var ids = storeItemIds.ToList();
        return await _context.StoreItemImage
            .Where(img => ids.Contains(img.StoreItemId))
            .ToListAsync(cancellationToken);
    }

    public void AddImageRows(IEnumerable<StoreItemImage> images)
    {
        _context.StoreItemImage.AddRange(images);
    }

    public void RemoveImageRows(IEnumerable<StoreItemImage> images)
    {
        _context.StoreItemImage.RemoveRange(images);
    }
}
