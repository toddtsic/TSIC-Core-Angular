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
            .OrderBy(i => i.SortOrder)
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
        return await _context.StoreItems
            .Where(i => i.StoreItemId == storeItemId && i.StoreId == storeId)
            .Select(i => new StoreItemDto
            {
                StoreItemId = i.StoreItemId,
                StoreId = i.StoreId,
                StoreItemName = i.StoreItemName,
                StoreItemComments = i.StoreItemComments,
                StoreItemPrice = i.StoreItemPrice,
                Active = i.Active,
                SortOrder = i.SortOrder,
                Modified = i.Modified,
                Skus = i.StoreItemSkus.Select(sku => new StoreSkuDto
                {
                    StoreSkuId = sku.StoreSkuId,
                    StoreItemId = sku.StoreItemId,
                    StoreColorId = sku.StoreColorId,
                    StoreColorName = sku.StoreColor != null ? sku.StoreColor.StoreColorName : null,
                    StoreSizeId = sku.StoreSizeId,
                    StoreSizeName = sku.StoreSize != null ? sku.StoreSize.StoreSizeName : null,
                    Active = sku.Active,
                    MaxCanSell = sku.MaxCanSell,
                    SoldCount = sku.StoreCartBatchSkus
                        .Where(cbs => cbs.Active && cbs.StoreCartBatch.StoreCartBatchAccounting.Any())
                        .Sum(cbs => cbs.Quantity),
                    InCartCount = sku.StoreCartBatchSkus
                        .Where(cbs => cbs.Active && !cbs.StoreCartBatch.StoreCartBatchAccounting.Any())
                        .Sum(cbs => cbs.Quantity),
                    AvailableCount = sku.MaxCanSell
                        - sku.StoreCartBatchSkus
                            .Where(cbs => cbs.Active && cbs.StoreCartBatch.StoreCartBatchAccounting.Any())
                            .Sum(cbs => cbs.Quantity)
                        - sku.StoreCartBatchSkus
                            .Where(cbs => cbs.Active && !cbs.StoreCartBatch.StoreCartBatchAccounting.Any())
                            .Sum(cbs => cbs.Quantity)
                }).ToList(),
                ImageUrls = i.StoreItemImage
                    .OrderBy(img => img.DisplayOrder)
                    .Select(img => img.ImageUrl)
                    .ToList()
            })
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<StoreItems?> GetItemByIdAsync(
        int storeItemId, CancellationToken cancellationToken = default)
    {
        return await _context.StoreItems
            .FirstOrDefaultAsync(i => i.StoreItemId == storeItemId, cancellationToken);
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
        return await _context.StoreItemSkus
            .Where(sku => sku.StoreItemId == storeItemId)
            .Select(sku => new StoreSkuDto
            {
                StoreSkuId = sku.StoreSkuId,
                StoreItemId = sku.StoreItemId,
                StoreColorId = sku.StoreColorId,
                StoreColorName = sku.StoreColor != null ? sku.StoreColor.StoreColorName : null,
                StoreSizeId = sku.StoreSizeId,
                StoreSizeName = sku.StoreSize != null ? sku.StoreSize.StoreSizeName : null,
                Active = sku.Active,
                MaxCanSell = sku.MaxCanSell,
                SoldCount = sku.StoreCartBatchSkus
                    .Where(cbs => cbs.Active && cbs.StoreCartBatch.StoreCartBatchAccounting.Any())
                    .Sum(cbs => cbs.Quantity),
                InCartCount = sku.StoreCartBatchSkus
                    .Where(cbs => cbs.Active && !cbs.StoreCartBatch.StoreCartBatchAccounting.Any())
                    .Sum(cbs => cbs.Quantity),
                AvailableCount = sku.MaxCanSell
                    - sku.StoreCartBatchSkus
                        .Where(cbs => cbs.Active && cbs.StoreCartBatch.StoreCartBatchAccounting.Any())
                        .Sum(cbs => cbs.Quantity)
                    - sku.StoreCartBatchSkus
                        .Where(cbs => cbs.Active && !cbs.StoreCartBatch.StoreCartBatchAccounting.Any())
                        .Sum(cbs => cbs.Quantity)
            })
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public async Task<StoreItemSkus?> GetSkuByIdAsync(
        int storeSkuId, CancellationToken cancellationToken = default)
    {
        return await _context.StoreItemSkus
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
}
