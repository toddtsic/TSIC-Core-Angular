using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Dtos.Store;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Store;
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
                LineTotal = cbs.FeeTotal, // FeeTotal is the line grand total (legacy semantics)
                DirectToRegId = cbs.DirectToRegId,
                DirectToPlayerName = cbs.DirectToReg != null && cbs.DirectToReg.User != null
                    ? cbs.DirectToReg.User.FirstName!.Trim() + " " + cbs.DirectToReg.User.LastName!.Trim()
                    : null,
                Active = cbs.Active
            })
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    // Both predicates below ARE the authorization checks — see the interface docs. The line's
    // owner is reached through StoreCartBatch -> StoreCart, which carries both StoreId and
    // FamilyUserId, so neither boundary needs a second lookup.

    public async Task<StoreCartBatchSkus?> GetLineItemForFamilyAsync(
        int storeCartBatchSkuId, int storeId, string familyUserId,
        CancellationToken cancellationToken = default)
    {
        return await _context.StoreCartBatchSkus
            .FirstOrDefaultAsync(
                cbs => cbs.StoreCartBatchSkuId == storeCartBatchSkuId
                    && cbs.StoreCartBatch.StoreCart.StoreId == storeId
                    && cbs.StoreCartBatch.StoreCart.FamilyUserId == familyUserId,
                cancellationToken);
    }

    public async Task<StoreCartBatchSkus?> GetLineItemInStoreAsync(
        int storeCartBatchSkuId, int storeId, CancellationToken cancellationToken = default)
    {
        return await _context.StoreCartBatchSkus
            .FirstOrDefaultAsync(
                cbs => cbs.StoreCartBatchSkuId == storeCartBatchSkuId
                    && cbs.StoreCartBatch.StoreCart.StoreId == storeId,
                cancellationToken);
    }

    public async Task<StoreCartBatchSkus?> GetLineItemBySkuAsync(
        int storeCartBatchId, int storeSkuId, Guid? directToRegId = null, CancellationToken cancellationToken = default)
    {
        return await _context.StoreCartBatchSkus
            .FirstOrDefaultAsync(cbs => cbs.StoreCartBatchId == storeCartBatchId
                && cbs.StoreSkuId == storeSkuId
                && cbs.DirectToRegId == directToRegId
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

    public async Task<StoreCartBatchAccounting?> GetBatchAccountingAsync(
        int storeCartBatchId, CancellationToken cancellationToken = default)
    {
        return await _context.StoreCartBatchAccounting
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.StoreCartBatchId == storeCartBatchId, cancellationToken);
    }

    public async Task<StoreReceiptContextDto?> GetReceiptContextAsync(
        int storeCartBatchId, CancellationToken cancellationToken = default)
    {
        var batch = await _context.StoreCartBatches
            .AsNoTracking()
            .Where(b => b.StoreCartBatchId == storeCartBatchId)
            .Select(b => new
            {
                b.StoreCart.Store.JobId,
                JobName = b.StoreCart.Store.Job.JobName ?? "",
                b.StoreCart.Store.Job.DisplayName,
                b.StoreCart.Store.Job.StoreContactEmail,
                FamilyUserId = b.StoreCart.FamilyUserId ?? ""
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (batch is null) return null;

        // Legacy reads Mom then Dad off the Families row keyed by the CART's family user.
        var family = await _context.Families
            .AsNoTracking()
            .Where(f => f.FamilyUserId == batch.FamilyUserId)
            .Select(f => new { f.MomEmail, f.DadEmail })
            .FirstOrDefaultAsync(cancellationToken);

        // Then every registrant a line was directed to. Distinct at the database; the caller
        // dedupes these against the parents, in legacy's order.
        var directed = await _context.StoreCartBatchSkus
            .AsNoTracking()
            .Where(s => s.StoreCartBatchId == storeCartBatchId
                && s.DirectToReg != null
                && s.DirectToReg!.User!.Email != null
                && s.DirectToReg!.User.Email != "")
            .Select(s => s.DirectToReg!.User!.Email!)
            .Distinct()
            .ToListAsync(cancellationToken);

        return new StoreReceiptContextDto
        {
            JobId = batch.JobId,
            JobName = batch.JobName,
            DisplayName = batch.DisplayName,
            StoreContactEmail = batch.StoreContactEmail,
            FamilyUserId = batch.FamilyUserId,
            MomEmail = family?.MomEmail,
            DadEmail = family?.DadEmail,
            DirectedEmails = directed
        };
    }

    public async Task<List<StoreFamilyPurchaseHistoryRowDto>> GetFamilyPurchaseHistoryAsync(
        Guid jobId, string familyUserId, CancellationToken cancellationToken = default)
    {
        // Legacy groups by the whole tuple, which makes this a DISTINCT rather than an aggregate —
        // a batch with a charge and a later reversal yields two rows. Preserved: it is the
        // truthful record of what happened to the shopper's money.
        return await _context.StoreCartBatchAccounting
            .AsNoTracking()
            .Where(a => a.StoreCartBatch.StoreCart.Store.JobId == jobId
                && a.StoreCartBatch.StoreCart.FamilyUserId == familyUserId)
            .GroupBy(a => new
            {
                a.StoreCartBatchId,
                a.CreateDate,
                a.AdnInvoiceNo,
                a.Paid,
                PaymentMethodName = a.PaymentMethod.PaymentMethod ?? ""
            })
            .Select(g => new StoreFamilyPurchaseHistoryRowDto
            {
                StoreCartBatchId = g.Key.StoreCartBatchId,
                PaymentDate = g.Key.CreateDate,
                AdnInvoiceNo = g.Key.AdnInvoiceNo,
                PaidTotal = g.Key.Paid,
                PaymentMethod = g.Key.PaymentMethodName
            })
            .OrderByDescending(r => r.PaymentDate)
            .ToListAsync(cancellationToken);
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

    public async Task<Dictionary<int, int>> GetSoldCountsForSkusAsync(
        List<int> storeSkuIds, CancellationToken cancellationToken = default)
    {
        return await _context.StoreCartBatchSkus
            .Where(cbs => storeSkuIds.Contains(cbs.StoreSkuId)
                && cbs.Active
                && cbs.StoreCartBatch.StoreCartBatchAccounting.Any())
            .GroupBy(cbs => cbs.StoreSkuId)
            .Select(g => new { SkuId = g.Key, Total = g.Sum(x => x.Quantity) })
            .AsNoTracking()
            .ToDictionaryAsync(x => x.SkuId, x => x.Total, cancellationToken);
    }

    public async Task<Dictionary<int, int>> GetInCartCountsForSkusAsync(
        List<int> storeSkuIds, CancellationToken cancellationToken = default)
    {
        return await _context.StoreCartBatchSkus
            .Where(cbs => storeSkuIds.Contains(cbs.StoreSkuId)
                && cbs.Active
                && !cbs.StoreCartBatch.StoreCartBatchAccounting.Any())
            .GroupBy(cbs => cbs.StoreSkuId)
            .Select(g => new { SkuId = g.Key, Total = g.Sum(x => x.Quantity) })
            .AsNoTracking()
            .ToDictionaryAsync(x => x.SkuId, x => x.Total, cancellationToken);
    }

    public async Task<List<StoreCartBatchSkus>> GetBatchLineItemEntitiesAsync(
        int storeCartBatchId, CancellationToken cancellationToken = default)
    {
        return await _context.StoreCartBatchSkus
            .Where(cbs => cbs.StoreCartBatchId == storeCartBatchId && cbs.Active)
            .ToListAsync(cancellationToken);
    }

    // ── Family players (for DirectTo dropdown) ──

    // The caller's OWN registration counts as a recipient, not just their Family_Members.
    // A walk-up buyer is minted with a registration but no Family_Members row, so the
    // Family_Members join alone returned an empty list: the catalog had nobody to
    // auto-select, add-to-cart posted a null DirectToRegId, and the purchase was recorded
    // with no recipient — no name on the cart or receipt, and invisible to the walk-up
    // analytics in StoreAnalyticsRepository.WalkUpLines, which key on that column.
    // Legacy has no such hole: GetListFamilyPlayers keys on Registrations.FamilyUserId and
    // never consults Family_Members (IStoreService.cs:322). Read that before touching this.
    //
    // Deliberately a UNION and not a swap to legacy's key. Measured on the dev DB: legacy's
    // keying reaches 475 active registrations this join misses, and this join reaches 394
    // legacy misses (Family_UserId null or pointing elsewhere). Neither is a superset, so
    // conforming outright would hide 394 recipients that work today. The self clause below
    // is the part that is unambiguous — you are always a valid recipient of your own
    // purchase — and it is all this fix claims. The wider keying question is Todd's.
    //
    // Ordering is legacy's: LastName then FirstName.
    public async Task<List<StoreFamilyPlayerDto>> GetFamilyPlayersForJobAsync(
        string familyUserId, Guid jobId, CancellationToken cancellationToken = default)
    {
        return await (
            from r in _context.Registrations
            join u in _context.AspNetUsers
                on r.UserId equals u.Id
            where r.JobId == jobId
                && r.BActive == true
                && (
                    r.UserId == familyUserId
                    || _context.FamilyMembers.Any(fm =>
                        fm.FamilyUserId == familyUserId
                        && fm.FamilyMemberUserId == r.UserId)
                )
            orderby u.LastName, u.FirstName
            select new StoreFamilyPlayerDto
            {
                RegistrationId = r.RegistrationId,
                FirstName = u.FirstName ?? "",
                LastName = u.LastName ?? ""
            }
        ).AsNoTracking().ToListAsync(cancellationToken);
    }

    public void AddQuantityAdjustment(StoreCartBatchSkuQuantityAdjustments adjustment)
    {
        _context.StoreCartBatchSkuQuantityAdjustments.Add(adjustment);
    }

    public async Task<Dictionary<int, string>> GetSkuLabelsAsync(
        List<int> storeSkuIds, CancellationToken cancellationToken = default)
    {
        var rows = await _context.StoreItemSkus
            .AsNoTracking()
            .Where(s => storeSkuIds.Contains(s.StoreSkuId))
            .Select(s => new
            {
                s.StoreSkuId,
                ItemName = s.StoreItem.StoreItemName,
                SizeName = s.StoreSize != null ? s.StoreSize.StoreSizeName : null,
                ColorName = s.StoreColor != null ? s.StoreColor.StoreColorName : null
            })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(
            r => r.StoreSkuId,
            r => StoreSkuLabel.Build(r.ItemName, r.SizeName, r.ColorName));
    }

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return await _context.SaveChangesAsync(cancellationToken);
    }
}
