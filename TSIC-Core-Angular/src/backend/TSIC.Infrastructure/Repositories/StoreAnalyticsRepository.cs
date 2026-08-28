using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Dtos.Store;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Store;
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

    // ── Walk-up identification ──

    /// <summary>
    /// Team that anchors every walk-up registration, under an agegroup and division of the same
    /// name. StoreWalkUpService refuses to sell without it.
    /// </summary>
    private const string WalkUpTeamName = "Store Merch";
    private const string WalkUpBucketName = "Dropped Teams";

    /// <summary>
    /// Is this purchased line a walk-up? The ONE definition, shared by every query that reports or
    /// filters walk-up sales.
    ///
    /// <para>
    /// LEGACY (GetJobStorePaymentData, walkupsOnly): a walk-up is a line DIRECTED TO a registration
    /// sitting on the "Store Merch" team. It is NOT "a line with no DirectToRegId" — walk-ups have
    /// one, pointing at the counter registration StoreWalkUpService mints for the buyer. That
    /// mistaken definition was live here and found 2 batches on the dev data where this finds 36;
    /// only 3 of 654 lines have a null DirectToRegId at all.
    /// </para>
    /// </summary>
    private IQueryable<StoreCartBatchSkus> WalkUpLines() =>
        _context.StoreCartBatchSkus.Where(cbs =>
            cbs.DirectToReg != null
            && cbs.DirectToReg.AssignedTeam != null
            && cbs.DirectToReg.AssignedTeam.TeamName == WalkUpTeamName
            && cbs.DirectToReg.AssignedTeam.Agegroup.AgegroupName == WalkUpBucketName
            && cbs.DirectToReg.AssignedTeam.Div != null
            && cbs.DirectToReg.AssignedTeam.Div.DivName == WalkUpBucketName);

    // ── Sales Analytics ──

    /// <summary>
    /// Legacy <c>IStoreService.GetJobPurchasesPivotData</c>, restored exactly. Three earlier
    /// divergences were each overstating a director-facing number — measured across the live
    /// database, they inflated units 533 → 529 and revenue $11,755.21 → $11,662.05:
    ///
    /// <list type="bullet">
    /// <item>Units summed <c>Quantity</c>, not <c>Quantity − Restocked</c>: returned goods counted as sold.</item>
    /// <item>Revenue summed <c>PaidTotal</c> and ignored <c>RefundedTotal</c> entirely.</item>
    /// <item>The filter was <c>PaidTotal &gt; 0</c> rather than "the batch was paid for", so a line
    /// refunded down to zero vanished from the rollup instead of showing as zero revenue.</item>
    /// </list>
    ///
    /// The zero guard is legacy's and is per-ROW, applied before the sum: a line with no payment
    /// contributes 0, never a negative — a refund can only cancel money that was taken.
    /// </summary>
    public async Task<List<StoreSalesPivotDto>> GetSalesPivotAsync(
        int storeId, CancellationToken cancellationToken = default)
    {
        var rows = await (
            from cbs in _context.StoreCartBatchSkus
            where cbs.StoreCartBatch.StoreCart.StoreId == storeId
                && cbs.Active
                && cbs.StoreCartBatch.StoreCartBatchAccounting.Any()
            group cbs by new
            {
                ItemName = cbs.StoreSku.StoreItem.StoreItemName,
                SizeName = cbs.StoreSku.StoreSize != null ? cbs.StoreSku.StoreSize.StoreSizeName : null,
                ColorName = cbs.StoreSku.StoreColor != null ? cbs.StoreSku.StoreColor.StoreColorName : null,
                cbs.CreateDate.Month,
                cbs.CreateDate.Year
            } into g
            select new
            {
                g.Key.ItemName,
                g.Key.SizeName,
                g.Key.ColorName,
                g.Key.Month,
                g.Key.Year,
                UnitsSold = g.Sum(x => x.Quantity - x.Restocked),
                Revenue = g.Sum(x => x.PaidTotal == 0 ? 0 : x.PaidTotal - x.RefundedTotal)
            }
        ).AsNoTracking().ToListAsync(cancellationToken);

        return rows
            .Select(r => new StoreSalesPivotDto
            {
                ItemName = r.ItemName,
                SkuLabel = StoreSkuLabel.Build(r.ItemName, r.SizeName, r.ColorName),
                Month = r.Month,
                Year = r.Year,
                UnitsSold = r.UnitsSold,
                Revenue = r.Revenue
            })
            .OrderByDescending(r => r.Year)
            .ThenByDescending(r => r.Month)
            .ThenBy(r => r.ItemName)
            .ThenBy(r => r.SkuLabel)
            .ToList();
    }

    /// <summary>
    /// Sales by item. Same "sold" and "revenue" rule as <see cref="GetSalesPivotAsync"/> — units net
    /// of restocks, money net of refunds, scoped to batches that were actually paid for. It had the
    /// same three overstatements, and two readouts of the same money disagreeing is worse than
    /// either being wrong on its own.
    /// </summary>
    public async Task<List<StoreSalesByItemDto>> GetSalesByItemAsync(
        int storeId, CancellationToken cancellationToken = default)
    {
        return await (
            from cbs in _context.StoreCartBatchSkus
            where cbs.StoreCartBatch.StoreCart.StoreId == storeId
                && cbs.Active
                && cbs.StoreCartBatch.StoreCartBatchAccounting.Any()
            group cbs by cbs.StoreSku.StoreItem.StoreItemName into g
            orderby g.Sum(x => x.PaidTotal == 0 ? 0 : x.PaidTotal - x.RefundedTotal) descending
            select new StoreSalesByItemDto
            {
                ItemName = g.Key,
                TotalUnitsSold = g.Sum(x => x.Quantity - x.Restocked),
                TotalRevenue = g.Sum(x => x.PaidTotal == 0 ? 0 : x.PaidTotal - x.RefundedTotal)
            }
        ).AsNoTracking().ToListAsync(cancellationToken);
    }

    public async Task<List<StorePaymentDetailDto>> GetPaymentDetailsAsync(
        int storeId, bool walkUpOnly, CancellationToken cancellationToken = default)
    {
        var walkUpLines = WalkUpLines();

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
                // A batch is a walk-up when its lines are directed to the Store Merch counter
                // registration — see WalkUpLines for why "no DirectToRegId" is the wrong test.
                IsWalkUp = walkUpLines.Any(w => w.StoreCartBatchId == batch.StoreCartBatchId)
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
                                LineTotal = cbs.FeeTotal, // FeeTotal is the line grand total (legacy semantics)
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
                                LineTotal = cbs.FeeTotal, // FeeTotal is the line grand total (legacy semantics)
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
                ColorName = sku.StoreColor != null ? sku.StoreColor!.StoreColorName : null,
                SizeName = sku.StoreSize != null ? sku.StoreSize!.StoreSizeName : null,
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

    /// <summary>
    /// Legacy StoreCartQuantityAdjustmentsController.GetListReportData, with two corrections.
    /// The SKU label goes through <see cref="StoreSkuLabel"/> instead of an unconditional ':'
    /// concat, which in SQL nulls the whole label when a SKU has no size or colour. And the
    /// parent name comes from <c>Families</c> as legacy's did, while the address is the family
    /// LOGIN's — legacy named that column MomEmail but read <c>FamilyUser.Email</c>.
    /// </summary>
    public async Task<List<StoreQuantityAdjustmentDto>> GetQuantityAdjustmentsAsync(
        Guid jobId, CancellationToken cancellationToken = default)
    {
        var rows = await _context.StoreCartBatchSkuQuantityAdjustments
            .AsNoTracking()
            .Where(qa => qa.StoreCart.Store.JobId == jobId)
            .OrderByDescending(qa => qa.Modified)
            .Select(qa => new
            {
                qa.StoreCartBatchSkuQuantityAdjustmentsId,
                qa.FromQuantity,
                qa.ToQuantity,
                qa.Modified,
                FamilyUserName = qa.StoreCart.FamilyUser.UserName,
                Email = qa.StoreCart.FamilyUser.Email,
                ParentFirstName = qa.StoreCart.FamilyUser.FamiliesFamilyUser != null
                    ? qa.StoreCart.FamilyUser.FamiliesFamilyUser.MomFirstName : null,
                ParentLastName = qa.StoreCart.FamilyUser.FamiliesFamilyUser != null
                    ? qa.StoreCart.FamilyUser.FamiliesFamilyUser.MomLastName : null,
                ItemName = qa.StoreSku.StoreItem.StoreItemName,
                SizeName = qa.StoreSku.StoreSize != null ? qa.StoreSku.StoreSize.StoreSizeName : null,
                ColorName = qa.StoreSku.StoreColor != null ? qa.StoreSku.StoreColor.StoreColorName : null
            })
            .ToListAsync(cancellationToken);

        return rows.Select(r => new StoreQuantityAdjustmentDto
        {
            StoreCartBatchSkuQuantityAdjustmentsId = r.StoreCartBatchSkuQuantityAdjustmentsId,
            AdjQuantity = r.FromQuantity - r.ToQuantity,
            SkuLabel = StoreSkuLabel.Build(r.ItemName, r.SizeName, r.ColorName),
            FromQuantity = r.FromQuantity,
            ToQuantity = r.ToQuantity,
            FamilyUserName = r.FamilyUserName ?? "",
            ParentFirstName = r.ParentFirstName,
            ParentLastName = r.ParentLastName,
            Email = r.Email ?? "",
            WhenChanged = r.Modified
        }).ToList();
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

    // ── Sales operations ──

    public async Task<List<StoreSaleLineDto>> GetSaleLinesAsync(
        int storeId, bool walkUpOnly, CancellationToken cancellationToken = default)
    {
        var walkUpLines = WalkUpLines();

        // LEGACY (GetJobStorePaymentData): every line on a batch that HAS an accounting record —
        // i.e. money changed hands. Deliberately NOT filtered on Active or PaidTotal > 0: a fully
        // refunded or restocked line is still a sale that happened, and it must stay visible or a
        // director cannot see what they reversed.
        var query =
            from cbs in _context.StoreCartBatchSkus
            where cbs.StoreCartBatch.StoreCart.StoreId == storeId
                && cbs.StoreCartBatch.StoreCartBatchAccounting.Any()
            select new
            {
                Line = cbs,
                IsWalkUp = walkUpLines.Any(w => w.StoreCartBatchSkuId == cbs.StoreCartBatchSkuId)
            };

        if (walkUpOnly)
            query = query.Where(x => x.IsWalkUp);

        var rows = await query
            .OrderByDescending(x => x.Line.Modified)
            .ThenByDescending(x => x.Line.StoreCartBatchId)
            .ThenByDescending(x => x.Line.StoreCartBatchSkuId)
            .Select(x => new SaleLineRow(
                x.Line.StoreCartBatchSkuId,
                x.Line.StoreCartBatchId,
                x.Line.StoreSkuId,
                x.Line.Active,
                x.Line.StoreCartBatch.StoreCart.FamilyUser.UserName ?? "",
                x.Line.StoreSku.StoreItem.StoreItemName,
                x.Line.StoreSku.StoreSize != null ? x.Line.StoreSku.StoreSize.StoreSizeName : null,
                x.Line.StoreSku.StoreColor != null ? x.Line.StoreSku.StoreColor.StoreColorName : null,
                x.Line.Quantity,
                x.Line.Restocked,
                x.Line.UnitPrice,
                x.Line.FeeProduct,
                x.Line.FeeProcessing,
                x.Line.SalesTax,
                x.Line.FeeTotal,
                x.Line.PaidTotal,
                x.Line.RefundedTotal,
                // Earliest accounting row on the batch — when the customer actually paid, which
                // is not the line's CreateDate (that is when it entered the cart).
                x.Line.StoreCartBatch.StoreCartBatchAccounting
                    .Select(a => (DateTime?)a.CreateDate).Min(),
                x.Line.Modified,
                x.IsWalkUp,
                // LEGACY fallback chain: the directed registrant, else the purchasing family's
                // Mom, else Dad — so the grid always names someone to hand the goods to.
                x.Line.DirectToReg != null && x.Line.DirectToReg.User != null ? x.Line.DirectToReg.User.FirstName : null,
                x.Line.DirectToReg != null && x.Line.DirectToReg.User != null ? x.Line.DirectToReg.User.LastName : null,
                x.Line.DirectToReg != null && x.Line.DirectToReg.User != null ? x.Line.DirectToReg.User.Email : null,
                x.Line.DirectToReg != null && x.Line.DirectToReg.User != null ? x.Line.DirectToReg.User.Cellphone : null,
                x.Line.StoreCartBatch.StoreCart.FamilyUser.FamiliesFamilyUser!.MomFirstName,
                x.Line.StoreCartBatch.StoreCart.FamilyUser.FamiliesFamilyUser!.MomLastName,
                x.Line.StoreCartBatch.StoreCart.FamilyUser.FamiliesFamilyUser!.MomEmail,
                x.Line.StoreCartBatch.StoreCart.FamilyUser.FamiliesFamilyUser!.MomCellphone,
                x.Line.StoreCartBatch.StoreCart.FamilyUser.FamiliesFamilyUser!.DadFirstName,
                x.Line.StoreCartBatch.StoreCart.FamilyUser.FamiliesFamilyUser!.DadLastName,
                x.Line.StoreCartBatch.StoreCart.FamilyUser.FamiliesFamilyUser!.DadEmail,
                x.Line.StoreCartBatch.StoreCart.FamilyUser.FamiliesFamilyUser!.DadCellphone,
                x.Line.DirectToReg != null && x.Line.DirectToReg.AssignedTeam != null
                    ? x.Line.DirectToReg.AssignedTeam.ClubrepRegistration!.ClubName : null,
                x.Line.DirectToReg != null && x.Line.DirectToReg.AssignedTeam != null
                    ? x.Line.DirectToReg.AssignedTeam.Agegroup.AgegroupName : null,
                x.Line.DirectToReg != null && x.Line.DirectToReg.AssignedTeam != null
                    && x.Line.DirectToReg.AssignedTeam.Div != null
                    ? x.Line.DirectToReg.AssignedTeam.Div.DivName : null,
                x.Line.DirectToReg != null && x.Line.DirectToReg.AssignedTeam != null
                    ? x.Line.DirectToReg.AssignedTeam.TeamName : null))
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return rows.Select(ToSaleLineDto).ToList();
    }

    private static StoreSaleLineDto ToSaleLineDto(SaleLineRow r) => new()
    {
        StoreCartBatchSkuId = r.StoreCartBatchSkuId,
        StoreCartBatchId = r.StoreCartBatchId,
        StoreSkuId = r.StoreSkuId,
        Active = r.Active,
        FamilyUserName = r.FamilyUserName,
        ItemName = r.ItemName,
        SkuLabel = StoreSkuLabel.Build(r.ItemName, r.SizeName, r.ColorName),
        // Units still with the customer, matching legacy's SkuQuantity.
        Quantity = r.Quantity - r.Restocked,
        UnitPrice = r.UnitPrice,
        FeeProduct = r.FeeProduct,
        FeeProcessing = r.FeeProcessing,
        SalesTax = r.SalesTax,
        FeeTotal = r.FeeTotal,
        Paid = r.PaidTotal,
        Refunded = r.RefundedTotal,
        MaxCanRefund = r.PaidTotal - r.RefundedTotal,
        Restocked = r.Restocked,
        MaxCanRestock = r.Quantity - r.Restocked,
        PurchaseDate = r.PurchaseDate,
        ModifiedDate = r.Modified,
        IsWalkUp = r.IsWalkUp,
        DirectToFirstName = Coalesce(r.RegFirstName, r.MomFirstName, r.DadFirstName),
        DirectToLastName = Coalesce(r.RegLastName, r.MomLastName, r.DadLastName),
        DirectToEmail = Coalesce(r.RegEmail, r.MomEmail, r.DadEmail),
        DirectToCellphone = Coalesce(r.RegCellphone, r.MomCellphone, r.DadCellphone),
        DirectToClub = r.ClubName,
        DirectToAgegroup = r.AgegroupName,
        DirectToPool = r.DivName,
        DirectToTeam = r.TeamName
    };

    /// <summary>
    /// First value that is actually present. Legacy used ?? here, which treats an EMPTY string as
    /// a real answer and stops — so a registrant with a blank email hid the family's. Blank is not
    /// a contact detail.
    /// </summary>
    private static string? Coalesce(params string?[] candidates) =>
        candidates.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c));

    private sealed record SaleLineRow(
        int StoreCartBatchSkuId, int StoreCartBatchId, int StoreSkuId, bool Active,
        string FamilyUserName, string ItemName, string? SizeName, string? ColorName,
        int Quantity, int Restocked, decimal UnitPrice, decimal FeeProduct, decimal FeeProcessing,
        decimal SalesTax, decimal FeeTotal, decimal PaidTotal, decimal RefundedTotal,
        DateTime? PurchaseDate, DateTime Modified, bool IsWalkUp,
        string? RegFirstName, string? RegLastName, string? RegEmail, string? RegCellphone,
        string? MomFirstName, string? MomLastName, string? MomEmail, string? MomCellphone,
        string? DadFirstName, string? DadLastName, string? DadEmail, string? DadCellphone,
        string? ClubName, string? AgegroupName, string? DivName, string? TeamName);

    public async Task<StoreCartBatchSkus?> GetTrackedLineAsync(
        int storeCartBatchSkuId, CancellationToken cancellationToken = default)
    {
        return await _context.StoreCartBatchSkus
            .Include(cbs => cbs.StoreSku)
            .Include(cbs => cbs.StoreCartBatch)
                .ThenInclude(b => b.StoreCart)
            .FirstOrDefaultAsync(
                cbs => cbs.StoreCartBatchSkuId == storeCartBatchSkuId, cancellationToken);
    }

    public async Task<List<StoreCartBatchSkus>> GetTrackedBatchLinesAsync(
        int storeCartBatchId, CancellationToken cancellationToken = default)
    {
        return await _context.StoreCartBatchSkus
            .Where(cbs => cbs.StoreCartBatchId == storeCartBatchId)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<StoreCartBatchAccounting>> GetTrackedBatchAccountingAsync(
        int storeCartBatchId, CancellationToken cancellationToken = default)
    {
        return await _context.StoreCartBatchAccounting
            .Where(a => a.StoreCartBatchId == storeCartBatchId)
            // Oldest first: the ORIGINAL charge is the one a reversal refers to, and later rows
            // are the refunds and voids already applied against it.
            .OrderBy(a => a.CreateDate)
            .ThenBy(a => a.StoreCartBatchAccountingId)
            .ToListAsync(cancellationToken);
    }

    public void AddAccounting(StoreCartBatchAccounting accounting)
    {
        _context.StoreCartBatchAccounting.Add(accounting);
    }

    public void AddSkuEdit(StoreCartBatchSkuEdits edit)
    {
        _context.StoreCartBatchSkuEdits.Add(edit);
    }

    public void AddLine(StoreCartBatchSkus line)
    {
        _context.StoreCartBatchSkus.Add(line);
    }
}
