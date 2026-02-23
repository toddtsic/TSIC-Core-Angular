using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Dtos.Store;
using TSIC.Contracts.Repositories;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Data.SqlDbContext;

namespace TSIC.Infrastructure.Repositories;

/// <summary>
/// Repository for store analytics queries and admin operations (refunds, restocks, pickup).
/// </summary>
public class StoreAnalyticsRepository : IStoreAnalyticsRepository
{
    private readonly SqlDbContext _context;

    public StoreAnalyticsRepository(SqlDbContext context)
    {
        _context = context;
    }

    // ── Sales Analytics ──

    public async Task<List<StoreSalesPivotDto>> GetSalesPivotAsync(
        int storeId, CancellationToken cancellationToken = default)
    {
        return await (
            from cbs in _context.StoreCartBatchSkus
            join sku in _context.StoreItemSkus on cbs.StoreSkuId equals sku.StoreSkuId
            join item in _context.StoreItems on sku.StoreItemId equals item.StoreItemId
            where item.StoreId == storeId
                && cbs.Active
                && cbs.PaidTotal > 0
            group cbs by new
            {
                item.StoreItemName,
                cbs.CreateDate.Month,
                cbs.CreateDate.Year
            } into g
            orderby g.Key.Year descending, g.Key.Month descending, g.Key.StoreItemName
            select new StoreSalesPivotDto
            {
                ItemName = g.Key.StoreItemName,
                Month = g.Key.Month,
                Year = g.Key.Year,
                UnitsSold = g.Sum(x => x.Quantity),
                Revenue = g.Sum(x => x.PaidTotal)
            }
        ).AsNoTracking().ToListAsync(cancellationToken);
    }

    public async Task<List<StoreSalesByItemDto>> GetSalesByItemAsync(
        int storeId, CancellationToken cancellationToken = default)
    {
        return await (
            from cbs in _context.StoreCartBatchSkus
            join sku in _context.StoreItemSkus on cbs.StoreSkuId equals sku.StoreSkuId
            join item in _context.StoreItems on sku.StoreItemId equals item.StoreItemId
            where item.StoreId == storeId
                && cbs.Active
                && cbs.PaidTotal > 0
            group cbs by item.StoreItemName into g
            orderby g.Sum(x => x.PaidTotal) descending
            select new StoreSalesByItemDto
            {
                ItemName = g.Key,
                TotalUnitsSold = g.Sum(x => x.Quantity),
                TotalRevenue = g.Sum(x => x.PaidTotal)
            }
        ).AsNoTracking().ToListAsync(cancellationToken);
    }

    public async Task<List<StorePaymentDetailDto>> GetPaymentDetailsAsync(
        int storeId, bool walkUpOnly, CancellationToken cancellationToken = default)
    {
        var query =
            from acct in _context.StoreCartBatchAccounting
            join batch in _context.StoreCartBatches on acct.StoreCartBatchId equals batch.StoreCartBatchId
            join cart in _context.StoreCart on batch.StoreCartId equals cart.StoreCartId
            join familyUser in _context.AspNetUsers on cart.FamilyUserId equals familyUser.Id
            join pm in _context.AccountingPaymentMethods on acct.PaymentMethodId equals pm.PaymentMethodId
            where cart.StoreId == storeId
            select new
            {
                acct,
                batch,
                cart,
                FamilyUserName = familyUser.UserName ?? "",
                PaymentMethodName = pm.PaymentMethod ?? "",
                // Walk-up: line items have no DirectToRegId
                IsWalkUp = !batch.StoreCartBatchSkus.Any(cbs => cbs.DirectToRegId != null)
            };

        if (walkUpOnly)
        {
            query = query.Where(x => x.IsWalkUp);
        }

        return await query
            .OrderByDescending(x => x.acct.CreateDate)
            .Select(x => new StorePaymentDetailDto
            {
                StoreCartBatchAccountingId = x.acct.StoreCartBatchAccountingId,
                StoreCartBatchId = x.acct.StoreCartBatchId,
                FamilyUserId = x.cart.FamilyUserId,
                FamilyUserName = x.FamilyUserName,
                PaymentMethodName = x.PaymentMethodName,
                Paid = x.acct.Paid,
                CreateDate = x.acct.CreateDate,
                Cclast4 = x.acct.Cclast4,
                AdnInvoiceNo = x.acct.AdnInvoiceNo,
                AdnTransactionId = x.acct.AdnTransactionId,
                Comment = x.acct.Comment,
                IsWalkUp = x.IsWalkUp
            })
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public async Task<List<StoreFamilyPurchaseDto>> GetFamilyPurchasesAsync(
        int storeId, CancellationToken cancellationToken = default)
    {
        return await (
            from cart in _context.StoreCart
            join familyUser in _context.AspNetUsers on cart.FamilyUserId equals familyUser.Id
            where cart.StoreId == storeId
                && cart.StoreCartBatches.Any(b => b.StoreCartBatchAccounting.Any())
            select new StoreFamilyPurchaseDto
            {
                FamilyUserId = cart.FamilyUserId,
                FamilyUserName = familyUser.UserName ?? "",
                TotalSpent = cart.StoreCartBatches
                    .SelectMany(b => b.StoreCartBatchAccounting)
                    .Sum(a => a.Paid),
                Transactions = cart.StoreCartBatches
                    .Where(b => b.StoreCartBatchAccounting.Any())
                    .OrderByDescending(b => b.Modified)
                    .Select(b => new StoreFamilyTransactionDto
                    {
                        StoreCartBatchId = b.StoreCartBatchId,
                        PurchaseDate = b.Modified,
                        TotalPaid = b.StoreCartBatchAccounting.Sum(a => a.Paid),
                        ItemCount = b.StoreCartBatchSkus.Where(cbs => cbs.Active).Sum(cbs => cbs.Quantity),
                        Items = b.StoreCartBatchSkus
                            .Where(cbs => cbs.Active)
                            .Select(cbs => new StoreCartLineItemDto
                            {
                                StoreCartBatchSkuId = cbs.StoreCartBatchSkuId,
                                StoreSkuId = cbs.StoreSkuId,
                                ItemName = cbs.StoreSku.StoreItem.StoreItemName,
                                ColorName = cbs.StoreSku.StoreColor != null
                                    ? cbs.StoreSku.StoreColor.StoreColorName : null,
                                SizeName = cbs.StoreSku.StoreSize != null
                                    ? cbs.StoreSku.StoreSize.StoreSizeName : null,
                                Quantity = cbs.Quantity,
                                UnitPrice = cbs.UnitPrice,
                                FeeProduct = cbs.FeeProduct,
                                FeeProcessing = cbs.FeeProcessing,
                                SalesTax = cbs.SalesTax,
                                FeeTotal = cbs.FeeTotal,
                                LineTotal = cbs.UnitPrice * cbs.Quantity + cbs.FeeTotal,
                                DirectToRegId = cbs.DirectToRegId,
                                Active = cbs.Active
                            }).ToList()
                    }).ToList()
            }
        ).AsNoTracking().ToListAsync(cancellationToken);
    }

    public async Task<StoreFamilyPurchaseDto?> GetFamilyPurchaseHistoryAsync(
        int storeId, string familyUserId, CancellationToken cancellationToken = default)
    {
        return await (
            from cart in _context.StoreCart
            join familyUser in _context.AspNetUsers on cart.FamilyUserId equals familyUser.Id
            where cart.StoreId == storeId && cart.FamilyUserId == familyUserId
            select new StoreFamilyPurchaseDto
            {
                FamilyUserId = cart.FamilyUserId,
                FamilyUserName = familyUser.UserName ?? "",
                TotalSpent = cart.StoreCartBatches
                    .SelectMany(b => b.StoreCartBatchAccounting)
                    .Sum(a => a.Paid),
                Transactions = cart.StoreCartBatches
                    .Where(b => b.StoreCartBatchAccounting.Any())
                    .OrderByDescending(b => b.Modified)
                    .Select(b => new StoreFamilyTransactionDto
                    {
                        StoreCartBatchId = b.StoreCartBatchId,
                        PurchaseDate = b.Modified,
                        TotalPaid = b.StoreCartBatchAccounting.Sum(a => a.Paid),
                        ItemCount = b.StoreCartBatchSkus.Where(cbs => cbs.Active).Sum(cbs => cbs.Quantity),
                        Items = b.StoreCartBatchSkus
                            .Where(cbs => cbs.Active)
                            .Select(cbs => new StoreCartLineItemDto
                            {
                                StoreCartBatchSkuId = cbs.StoreCartBatchSkuId,
                                StoreSkuId = cbs.StoreSkuId,
                                ItemName = cbs.StoreSku.StoreItem.StoreItemName,
                                ColorName = cbs.StoreSku.StoreColor != null
                                    ? cbs.StoreSku.StoreColor.StoreColorName : null,
                                SizeName = cbs.StoreSku.StoreSize != null
                                    ? cbs.StoreSku.StoreSize.StoreSizeName : null,
                                Quantity = cbs.Quantity,
                                UnitPrice = cbs.UnitPrice,
                                FeeProduct = cbs.FeeProduct,
                                FeeProcessing = cbs.FeeProcessing,
                                SalesTax = cbs.SalesTax,
                                FeeTotal = cbs.FeeTotal,
                                LineTotal = cbs.UnitPrice * cbs.Quantity + cbs.FeeTotal,
                                DirectToRegId = cbs.DirectToRegId,
                                Active = cbs.Active
                            }).ToList()
                    }).ToList()
            }
        ).AsNoTracking().FirstOrDefaultAsync(cancellationToken);
    }

    // ── Refunds ──

    public async Task<List<StoreRefundedItemDto>> GetRefundedItemsAsync(
        int storeId, CancellationToken cancellationToken = default)
    {
        return await (
            from cbs in _context.StoreCartBatchSkus
            join sku in _context.StoreItemSkus on cbs.StoreSkuId equals sku.StoreSkuId
            join item in _context.StoreItems on sku.StoreItemId equals item.StoreItemId
            join batch in _context.StoreCartBatches on cbs.StoreCartBatchId equals batch.StoreCartBatchId
            join cart in _context.StoreCart on batch.StoreCartId equals cart.StoreCartId
            join familyUser in _context.AspNetUsers on cart.FamilyUserId equals familyUser.Id
            where item.StoreId == storeId && cbs.RefundedTotal > 0
            orderby cbs.Modified descending
            select new StoreRefundedItemDto
            {
                StoreCartBatchSkuId = cbs.StoreCartBatchSkuId,
                ItemName = item.StoreItemName,
                ColorName = sku.StoreColor != null ? sku.StoreColor.StoreColorName : null,
                SizeName = sku.StoreSize != null ? sku.StoreSize.StoreSizeName : null,
                Quantity = cbs.Quantity,
                PaidTotal = cbs.PaidTotal,
                RefundedTotal = cbs.RefundedTotal,
                FamilyUserName = familyUser.UserName ?? "",
                ModifiedDate = cbs.Modified
            }
        ).AsNoTracking().ToListAsync(cancellationToken);
    }

    // ── Restocks ──

    public async Task<List<StoreRestockedItemDto>> GetRestockedItemsAsync(
        int storeId, CancellationToken cancellationToken = default)
    {
        return await (
            from rs in _context.StoreCartBatchSkuRestocks
            join cbs in _context.StoreCartBatchSkus on rs.StoreCartBatchSkuId equals cbs.StoreCartBatchSkuId
            join sku in _context.StoreItemSkus on cbs.StoreSkuId equals sku.StoreSkuId
            join item in _context.StoreItems on sku.StoreItemId equals item.StoreItemId
            join user in _context.AspNetUsers on rs.LebUserId equals user.Id
            where item.StoreId == storeId
            orderby rs.Modified descending
            select new StoreRestockedItemDto
            {
                StoreCartBatchSkuRestockId = rs.StoreCartBatchSkuRestockId,
                ItemName = item.StoreItemName,
                ColorName = sku.StoreColor != null ? sku.StoreColor.StoreColorName : null,
                SizeName = sku.StoreSize != null ? sku.StoreSize.StoreSizeName : null,
                RestockCount = rs.RestockCount,
                ModifiedDate = rs.Modified,
                ModifiedBy = user.UserName ?? ""
            }
        ).AsNoTracking().ToListAsync(cancellationToken);
    }

    public void AddRestock(StoreCartBatchSkuRestocks restock)
    {
        _context.StoreCartBatchSkuRestocks.Add(restock);
    }

    public void AddQuantityAdjustment(StoreCartBatchSkuQuantityAdjustments adjustment)
    {
        _context.StoreCartBatchSkuQuantityAdjustments.Add(adjustment);
    }

    // ── Pickup ──

    public async Task<StoreCartBatches?> GetBatchByIdAsync(
        int storeCartBatchId, CancellationToken cancellationToken = default)
    {
        return await _context.StoreCartBatches
            .FirstOrDefaultAsync(b => b.StoreCartBatchId == storeCartBatchId, cancellationToken);
    }

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return await _context.SaveChangesAsync(cancellationToken);
    }
}
