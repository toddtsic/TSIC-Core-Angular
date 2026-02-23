using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Dtos.Store;
using TSIC.Contracts.Repositories;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Data.SqlDbContext;

namespace TSIC.Infrastructure.Repositories;

/// <summary>
/// Repository for StoreCart, StoreCartBatches, StoreCartBatchSkus, and StoreCartBatchAccounting.
/// </summary>
public class StoreCartRepository : IStoreCartRepository
{
    private readonly SqlDbContext _context;

    public StoreCartRepository(SqlDbContext context)
    {
        _context = context;
    }

    // ── Cart ──

    public async Task<StoreCart?> GetCartAsync(
        int storeId, string familyUserId, CancellationToken cancellationToken = default)
    {
        return await _context.StoreCart
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.StoreId == storeId && c.FamilyUserId == familyUserId, cancellationToken);
    }

    public void AddCart(StoreCart cart)
    {
        _context.StoreCart.Add(cart);
    }

    // ── Batches ──

    public async Task<StoreCartBatches?> GetCurrentBatchAsync(
        int storeCartId, CancellationToken cancellationToken = default)
    {
        // Current batch = most recent batch with no accounting records (unpaid)
        return await _context.StoreCartBatches
            .Where(b => b.StoreCartId == storeCartId
                && !b.StoreCartBatchAccounting.Any())
            .OrderByDescending(b => b.Modified)
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken);
    }

    public void AddBatch(StoreCartBatches batch)
    {
        _context.StoreCartBatches.Add(batch);
    }

    // ── Batch SKUs (line items) ──

    public async Task<List<StoreCartLineItemDto>> GetBatchLineItemsAsync(
        int storeCartBatchId, CancellationToken cancellationToken = default)
    {
        return await _context.StoreCartBatchSkus
            .Where(cbs => cbs.StoreCartBatchId == storeCartBatchId && cbs.Active)
            .Select(cbs => new StoreCartLineItemDto
            {
                StoreCartBatchSkuId = cbs.StoreCartBatchSkuId,
                StoreSkuId = cbs.StoreSkuId,
                ItemName = cbs.StoreSku.StoreItem.StoreItemName,
                ColorName = cbs.StoreSku.StoreColor != null ? cbs.StoreSku.StoreColor.StoreColorName : null,
                SizeName = cbs.StoreSku.StoreSize != null ? cbs.StoreSku.StoreSize.StoreSizeName : null,
                Quantity = cbs.Quantity,
                UnitPrice = cbs.UnitPrice,
                FeeProduct = cbs.FeeProduct,
                FeeProcessing = cbs.FeeProcessing,
                SalesTax = cbs.SalesTax,
                FeeTotal = cbs.FeeTotal,
                LineTotal = cbs.UnitPrice * cbs.Quantity + cbs.FeeTotal,
                DirectToRegId = cbs.DirectToRegId,
                Active = cbs.Active
            })
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public async Task<StoreCartBatchSkus?> GetLineItemByIdAsync(
        int storeCartBatchSkuId, CancellationToken cancellationToken = default)
    {
        return await _context.StoreCartBatchSkus
            .FirstOrDefaultAsync(cbs => cbs.StoreCartBatchSkuId == storeCartBatchSkuId, cancellationToken);
    }

    public async Task<StoreCartBatchSkus?> GetLineItemBySkuAsync(
        int storeCartBatchId, int storeSkuId, CancellationToken cancellationToken = default)
    {
        return await _context.StoreCartBatchSkus
            .FirstOrDefaultAsync(cbs => cbs.StoreCartBatchId == storeCartBatchId
                && cbs.StoreSkuId == storeSkuId
                && cbs.Active, cancellationToken);
    }

    public void AddLineItem(StoreCartBatchSkus lineItem)
    {
        _context.StoreCartBatchSkus.Add(lineItem);
    }

    public void RemoveLineItem(StoreCartBatchSkus lineItem)
    {
        _context.StoreCartBatchSkus.Remove(lineItem);
    }

    // ── Accounting ──

    public void AddAccounting(StoreCartBatchAccounting accounting)
    {
        _context.StoreCartBatchAccounting.Add(accounting);
    }

    public async Task<bool> BatchHasPaymentAsync(
        int storeCartBatchId, CancellationToken cancellationToken = default)
    {
        return await _context.StoreCartBatchAccounting
            .AsNoTracking()
            .AnyAsync(a => a.StoreCartBatchId == storeCartBatchId, cancellationToken);
    }

    // ── Availability queries ──

    public async Task<int> GetSoldCountForSkuAsync(
        int storeSkuId, CancellationToken cancellationToken = default)
    {
        return await _context.StoreCartBatchSkus
            .Where(cbs => cbs.StoreSkuId == storeSkuId
                && cbs.Active
                && cbs.StoreCartBatch.StoreCartBatchAccounting.Any())
            .SumAsync(cbs => cbs.Quantity, cancellationToken);
    }

    public async Task<int> GetInCartCountForSkuAsync(
        int storeSkuId, CancellationToken cancellationToken = default)
    {
        return await _context.StoreCartBatchSkus
            .Where(cbs => cbs.StoreSkuId == storeSkuId
                && cbs.Active
                && !cbs.StoreCartBatch.StoreCartBatchAccounting.Any())
            .SumAsync(cbs => cbs.Quantity, cancellationToken);
    }

    public async Task<List<int>> ValidateBatchAvailabilityAsync(
        int storeCartBatchId, CancellationToken cancellationToken = default)
    {
        // Get all active line items in this batch with their SKU's MaxCanSell
        var lineItems = await _context.StoreCartBatchSkus
            .Where(cbs => cbs.StoreCartBatchId == storeCartBatchId && cbs.Active)
            .Select(cbs => new
            {
                cbs.StoreSkuId,
                cbs.Quantity,
                cbs.StoreSku.MaxCanSell
            })
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var overCommitted = new List<int>();

        foreach (var skuGroup in lineItems.GroupBy(li => li.StoreSkuId))
        {
            var skuId = skuGroup.Key;
            var maxCanSell = skuGroup.First().MaxCanSell;

            // Count sold across ALL batches (not just this one)
            var soldCount = await _context.StoreCartBatchSkus
                .Where(cbs => cbs.StoreSkuId == skuId
                    && cbs.Active
                    && cbs.StoreCartBatch.StoreCartBatchAccounting.Any())
                .SumAsync(cbs => cbs.Quantity, cancellationToken);

            // Count in all OTHER unpaid carts (exclude current batch)
            var otherCartCount = await _context.StoreCartBatchSkus
                .Where(cbs => cbs.StoreSkuId == skuId
                    && cbs.Active
                    && cbs.StoreCartBatchId != storeCartBatchId
                    && !cbs.StoreCartBatch.StoreCartBatchAccounting.Any())
                .SumAsync(cbs => cbs.Quantity, cancellationToken);

            var requestedQty = skuGroup.Sum(li => li.Quantity);

            if (soldCount + otherCartCount + requestedQty > maxCanSell)
            {
                overCommitted.Add(skuId);
            }
        }

        return overCommitted;
    }

    public async Task<List<StoreCartBatchSkus>> GetBatchLineItemEntitiesAsync(
        int storeCartBatchId, CancellationToken cancellationToken = default)
    {
        return await _context.StoreCartBatchSkus
            .Where(cbs => cbs.StoreCartBatchId == storeCartBatchId && cbs.Active)
            .ToListAsync(cancellationToken);
    }

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return await _context.SaveChangesAsync(cancellationToken);
    }
}
