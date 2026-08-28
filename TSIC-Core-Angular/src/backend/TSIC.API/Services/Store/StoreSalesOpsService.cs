using TSIC.API.Services.Shared.Adn;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Dtos.Store;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Entities;

namespace TSIC.API.Services.Store;

/// <inheritdoc cref="IStoreSalesOpsService"/>
public sealed class StoreSalesOpsService : IStoreSalesOpsService
{
    private readonly IStoreRepository _storeRepo;
    private readonly IStoreAnalyticsRepository _analyticsRepo;
    private readonly IStoreItemRepository _itemRepo;
    private readonly IStoreRestockService _restockService;
    private readonly IAdnReversalService _adnReversal;
    private readonly ILogger<StoreSalesOpsService> _logger;

    // AccountingPaymentMethods, matching legacy appsettings PaymentMethods.
    private static readonly Guid CreditCardPaymentMethodId = new("30ECA575-A268-E111-9D56-F04DA202060D");
    private static readonly Guid CreditCardCreditMethodId = new("31ECA575-A268-E111-9D56-F04DA202060D");
    private static readonly Guid CreditCardVoidMethodId = new("E4F59983-A837-E511-8259-0026186D94AE");

    public StoreSalesOpsService(
        IStoreRepository storeRepo,
        IStoreAnalyticsRepository analyticsRepo,
        IStoreItemRepository itemRepo,
        IStoreRestockService restockService,
        IAdnReversalService adnReversal,
        ILogger<StoreSalesOpsService> logger)
    {
        _storeRepo = storeRepo;
        _analyticsRepo = analyticsRepo;
        _itemRepo = itemRepo;
        _restockService = restockService;
        _adnReversal = adnReversal;
        _logger = logger;
    }

    // ═══════════════════════════════════════════
    //  READ
    // ═══════════════════════════════════════════

    public async Task<List<StoreSaleLineDto>> GetSaleLinesAsync(
        Guid jobId, bool walkUpOnly, CancellationToken ct = default)
    {
        var storeId = await ResolveStoreIdAsync(jobId, ct);
        return await _analyticsRepo.GetSaleLinesAsync(storeId, walkUpOnly, ct);
    }

    public async Task<List<StoreSwapOptionDto>> GetSwapOptionsAsync(
        Guid jobId, int storeCartBatchSkuId, CancellationToken ct = default)
    {
        var (line, _) = await LoadLineInStoreAsync(jobId, storeCartBatchSkuId, ct);

        // LEGACY GetCartSkuOptions: same item, a DIFFERENT sku, both sku and item active, and
        // availability > 0. An exchange is for another size or colour of the thing they bought —
        // never a different product, which would be a refund plus a new sale.
        var siblings = await _itemRepo.GetSkusWithAvailabilityAsync(line.StoreSku.StoreItemId, ct);

        return siblings
            .Where(s => s.StoreSkuId != line.StoreSkuId && s.Active && s.AvailableCount > 0)
            .Select(s => new StoreSwapOptionDto
            {
                StoreSkuId = s.StoreSkuId,
                SkuLabel = s.SkuLabel,
                AvailableCount = s.AvailableCount
            })
            .ToList();
    }

    public async Task<StoreBatchSettledStatusDto> GetBatchSettledStatusAsync(
        Guid jobId, int storeCartBatchId, CancellationToken ct = default)
    {
        await AssertBatchInStoreAsync(jobId, storeCartBatchId, ct);

        var lines = await _analyticsRepo.GetTrackedBatchLinesAsync(storeCartBatchId, ct);
        var original = await FindOriginalCardChargeAsync(storeCartBatchId, ct);

        if (original == null)
        {
            return new StoreBatchSettledStatusDto
            {
                StoreCartBatchId = storeCartBatchId,
                IsSettled = false,
                HasCardPayment = false,
                BatchPaidTotal = lines.Sum(l => l.PaidTotal)
            };
        }

        // Read-only gateway lookup — nothing is charged or reversed. This is what decides whether
        // the UI may offer a partial refund at all (LEGACY GetCartBatchHasSettledStatus).
        var status = await _adnReversal.GetChargeStatusAsync(jobId, original.AdnTransactionId, ct);

        return new StoreBatchSettledStatusDto
        {
            StoreCartBatchId = storeCartBatchId,
            IsSettled = status == AdnChargeStatus.Settled,
            HasCardPayment = true,
            BatchPaidTotal = lines.Sum(l => l.PaidTotal)
        };
    }

    // ═══════════════════════════════════════════
    //  SWAP
    // ═══════════════════════════════════════════

    public async Task SwapCartSkuAsync(
        Guid jobId, string userId, StoreSwapRequest request, CancellationToken ct = default)
    {
        var (line, _) = await LoadLineInStoreAsync(jobId, request.StoreCartBatchSkuId, ct);

        if (request.NewStoreSkuId == line.StoreSkuId)
            throw new InvalidOperationException("Pick a different size or colour to exchange for.");

        if (request.Quantity < 1 || request.Quantity > line.Quantity)
            throw new InvalidOperationException(
                $"Exchange between 1 and {line.Quantity} of the {line.Quantity} purchased.");

        // Legacy trusts the dropdown to have offered only valid targets, and re-checks nothing on
        // the POST. Enforce it here instead: same item, active, and actually in stock.
        var target = (await _itemRepo.GetSkusWithAvailabilityAsync(line.StoreSku.StoreItemId, ct))
            .FirstOrDefault(s => s.StoreSkuId == request.NewStoreSkuId)
            ?? throw new InvalidOperationException(
                "That variant belongs to a different product — an exchange is for another size or "
                + "colour of the same item.");

        if (!target.Active)
            throw new InvalidOperationException("That variant is no longer available.");

        if (target.AvailableCount < request.Quantity)
            throw new InvalidOperationException(
                target.AvailableCount <= 0
                    ? $"{target.SkuLabel} is sold out."
                    : $"Only {target.AvailableCount} of {target.SkuLabel} left.");

        var previousQuantity = line.Quantity;

        if (request.Quantity == line.Quantity)
        {
            // Whole line exchanged: retarget it. Legacy always split, leaving a zero-quantity ghost
            // row behind in the sales grid; there is nothing to split here.
            line.StoreSkuId = request.NewStoreSkuId;
            line.Modified = DateTime.Now;
            line.LebUserId = userId;

            _analyticsRepo.AddSkuEdit(new StoreCartBatchSkuEdits
            {
                StoreCartBatchSkuId = line.StoreCartBatchSkuId,
                PreviousStoreCartBatchSkuId = line.StoreCartBatchSkuId,
                PreviousStoreCartBatchSkuQuantity = previousQuantity,
                Modified = DateTime.Now,
                LebUserId = userId
            });

            await _analyticsRepo.SaveChangesAsync(ct);
            _logger.LogInformation(
                "Store SKU swap (full): line {LineId} {From} -> {To}, qty {Qty}",
                line.StoreCartBatchSkuId, line.StoreSkuId, request.NewStoreSkuId, previousQuantity);
            return;
        }

        // Partial exchange: split the line in two.
        //
        // DIVERGENCE FROM LEGACY, DELIBERATE. Legacy recomputed the split-off line's fees from
        // TODAY's job rates and subtracted those from the original — so if the processing or tax
        // rate had changed since the purchase, the two halves no longer summed to what the
        // customer actually paid. An exchange moves no money, so the invariant is that the halves
        // sum EXACTLY to the original. Every money column is therefore split by quantity, with the
        // rounding remainder left on the original line rather than recomputed from a rate.
        var split = new StoreCartBatchSkus
        {
            StoreCartBatchId = line.StoreCartBatchId,
            StoreSkuId = request.NewStoreSkuId,
            DirectToRegId = line.DirectToRegId,
            Active = line.Active,
            Quantity = request.Quantity,
            UnitPrice = line.UnitPrice,
            FeeProduct = Apportion(line.FeeProduct, request.Quantity, previousQuantity),
            FeeProcessing = Apportion(line.FeeProcessing, request.Quantity, previousQuantity),
            SalesTax = Apportion(line.SalesTax, request.Quantity, previousQuantity),
            FeeTotal = Apportion(line.FeeTotal, request.Quantity, previousQuantity),
            PaidTotal = Apportion(line.PaidTotal, request.Quantity, previousQuantity),
            RefundedTotal = Apportion(line.RefundedTotal, request.Quantity, previousQuantity),
            // Restocks belong to the units that were returned, which are not the ones moving.
            Restocked = 0,
            CreateDate = DateTime.Now,
            Modified = DateTime.Now,
            LebUserId = userId
        };

        line.Quantity -= request.Quantity;
        line.FeeProduct -= split.FeeProduct;
        line.FeeProcessing -= split.FeeProcessing;
        line.SalesTax -= split.SalesTax;
        line.FeeTotal -= split.FeeTotal;
        line.PaidTotal -= split.PaidTotal;
        line.RefundedTotal -= split.RefundedTotal;
        line.Modified = DateTime.Now;
        line.LebUserId = userId;

        _analyticsRepo.AddLine(split);
        await _analyticsRepo.SaveChangesAsync(ct);

        // The edit row points at the NEW line and names the one it came from, so the split is
        // reconstructable. Written after the save because it needs the new line's identity.
        _analyticsRepo.AddSkuEdit(new StoreCartBatchSkuEdits
        {
            StoreCartBatchSkuId = split.StoreCartBatchSkuId,
            PreviousStoreCartBatchSkuId = line.StoreCartBatchSkuId,
            PreviousStoreCartBatchSkuQuantity = previousQuantity,
            Modified = DateTime.Now,
            LebUserId = userId
        });
        await _analyticsRepo.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Store SKU swap (partial): line {LineId} split {Qty} of {Prev} to sku {To}",
            line.StoreCartBatchSkuId, request.Quantity, previousQuantity, request.NewStoreSkuId);
    }

    /// <summary>
    /// The share of <paramref name="total"/> that <paramref name="part"/> of
    /// <paramref name="whole"/> units carries, rounded to cents. The caller subtracts this from
    /// the original, so the two always sum back to the original exactly.
    /// </summary>
    private static decimal Apportion(decimal total, int part, int whole) =>
        whole <= 0 ? 0m : Math.Round(total * part / whole, 2, MidpointRounding.AwayFromZero);

    // ═══════════════════════════════════════════
    //  REFUND / VOID
    // ═══════════════════════════════════════════

    public async Task<StoreRefundResponse> RefundAsync(
        Guid jobId, string userId, StoreRefundRequest request, CancellationToken ct = default)
    {
        var (line, _) = await LoadLineInStoreAsync(jobId, request.StoreCartBatchSkuId, ct);
        var batchId = line.StoreCartBatchId;

        var batchLines = await _analyticsRepo.GetTrackedBatchLinesAsync(batchId, ct);
        var original = await FindOriginalCardChargeAsync(batchId, ct);

        if (original == null)
            return Fail("This purchase has no card payment to reverse.");

        var batchPaid = batchLines.Sum(l => l.PaidTotal);
        var lineMaxRefund = line.PaidTotal - line.RefundedTotal;

        // Server-side cap. Legacy capped the amount in the dialog only, so the ceiling was
        // advisory — a stale tab or a hand-built request could refund more than was ever paid.
        var requestedAmount = request.VoidEntireBatch ? batchPaid : request.RefundAmount;

        if (requestedAmount <= 0)
            return Fail(request.VoidEntireBatch
                ? "There is nothing left paid on this purchase to reverse."
                : "Enter a refund amount greater than $0.00.");

        if (!request.VoidEntireBatch && requestedAmount > lineMaxRefund)
            return Fail($"This line has ${lineMaxRefund:F2} available to refund.");

        var reversal = await _adnReversal.ReverseAsync(new AdnReversalRequest
        {
            JobId = jobId,
            AdnTransactionId = original.AdnTransactionId,
            OriginalPaidAmount = original.Paid,
            RequestedAmount = requestedAmount,
            CardLast4 = original.Cclast4,
            CardExpiry = original.CcexpDate,
            InvoiceNumber = original.AdnInvoiceNo
        }, ct);

        if (!reversal.Success)
            return Fail(reversal.Message);

        // A VOID is always for the FULL charge — Authorize.Net offers no partial. So a line-level
        // refund against an unsettled batch reverses EVERY line's money, whatever was asked for.
        // Booking that as a line refund would leave the other lines marked paid with no money
        // behind them, which is the ledger lying. Treat the gateway's answer as authoritative and
        // book the whole batch.
        var isFullBatchReversal = request.VoidEntireBatch || reversal.Kind == AdnReversalKind.Void;

        var restocked = isFullBatchReversal
            ? await BookBatchReversalAsync(batchLines, userId, ct)
            : await BookLineRefundAsync(line, reversal.ReversedAmount, request.RestockCount, userId, ct);

        RecordAccountingEntry(original, reversal, request.Reason, userId);
        await _analyticsRepo.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Store {Action}: batch {BatchId}, line {LineId}, amount {Amount}, restocked {Restocked}, tx {TxId}",
            reversal.Kind, batchId, line.StoreCartBatchSkuId,
            reversal.ReversedAmount, restocked, reversal.TransactionId);

        return new StoreRefundResponse
        {
            Success = true,
            Message = BuildOutcomeMessage(reversal, request, isFullBatchReversal, restocked),
            RefundedAmount = reversal.ReversedAmount,
            RestockedCount = restocked,
            TransactionId = reversal.TransactionId
        };
    }

    /// <summary>
    /// The whole purchase is reversed: every unit goes back on the shelf and every line is marked
    /// unpaid, with what it had been paid recorded as refunded. Legacy's RefundProceedWithVoid.
    /// </summary>
    private async Task<int> BookBatchReversalAsync(
        List<StoreCartBatchSkus> batchLines, string userId, CancellationToken ct)
    {
        var restocked = 0;

        foreach (var batchLine in batchLines)
        {
            var toRestock = batchLine.Quantity - batchLine.Restocked;
            if (toRestock > 0)
            {
                await _restockService.StageRestockAsync(
                    batchLine.StoreCartBatchSkuId, toRestock, userId, ct);
                restocked += toRestock;
            }

            batchLine.RefundedTotal += batchLine.PaidTotal;
            batchLine.PaidTotal = 0;
            batchLine.Modified = DateTime.Now;
            batchLine.LebUserId = userId;
        }

        return restocked;
    }

    /// <summary>
    /// One line refunded. Money and stock are tracked separately on purpose: an item may be
    /// refunded without coming back (damaged), or come back without a refund (goodwill).
    ///
    /// <para>
    /// PaidTotal is deliberately NOT reduced — RefundedTotal carries the reversal, and the batch's
    /// accounting rows are the ledger. Legacy reached the same shape, by commenting the decrement
    /// out; here it is a decision rather than a leftover.
    /// </para>
    /// </summary>
    private async Task<int> BookLineRefundAsync(
        StoreCartBatchSkus line, decimal amount, int restockCount, string userId, CancellationToken ct)
    {
        if (restockCount > 0)
            await _restockService.StageRestockAsync(line.StoreCartBatchSkuId, restockCount, userId, ct);

        line.RefundedTotal += amount;
        line.Modified = DateTime.Now;
        line.LebUserId = userId;

        return restockCount;
    }

    /// <summary>
    /// Record the reversal against the batch's ledger, the same two shapes as the registration
    /// path: an unsettled charge is voided IN PLACE, a settled one gets a negative credit row.
    /// </summary>
    private void RecordAccountingEntry(
        StoreCartBatchAccounting original, AdnReversalResult reversal, string? reason, string userId)
    {
        if (reversal.Kind == AdnReversalKind.Void)
        {
            original.PaymentMethodId = CreditCardVoidMethodId;
            // MUST be zeroed: the transaction was voided, so this row never collected anything.
            original.Paid = 0;
            original.Comment = IAdnReversalService.AppendNote(
                original.Comment,
                IAdnReversalService.BuildVoidNote(
                    reversal.ReversedAmount, reversal.TransactionId ?? "", reason));
            original.Modified = DateTime.Now;
            original.LebUserId = userId;
            return;
        }

        _analyticsRepo.AddAccounting(new StoreCartBatchAccounting
        {
            StoreCartBatchId = original.StoreCartBatchId,
            PaymentMethodId = CreditCardCreditMethodId,
            Paid = -reversal.ReversedAmount,
            CreateDate = DateTime.Now,
            Cclast4 = original.Cclast4,
            CcexpDate = original.CcexpDate,
            // Same invoice number as the charge it reverses — that is what ties the credit to the
            // original in adn.MonthyQBPExport_Automated_Merch.
            AdnInvoiceNo = original.AdnInvoiceNo,
            AdnTransactionId = reversal.TransactionId,
            Comment = string.IsNullOrWhiteSpace(reason)
                ? $"Refund {DateTime.Now:g}. ADN tx {reversal.TransactionId}."
                : $"Refund {DateTime.Now:g}. ADN tx {reversal.TransactionId}. Reason: {reason}",
            Modified = DateTime.Now,
            LebUserId = userId
        });
    }

    private static string BuildOutcomeMessage(
        AdnReversalResult reversal, StoreRefundRequest request, bool isFullBatchReversal, int restocked)
    {
        var stock = restocked == 1 ? "1 unit returned to stock" : $"{restocked} units returned to stock";

        // The case a director must not misread: they asked to refund one line and the gateway
        // reversed the entire purchase, because the charge had not settled yet.
        if (reversal.Kind == AdnReversalKind.Void && !request.VoidEntireBatch)
            return $"This charge had not settled, so Authorize.Net could only VOID it in full. "
                + $"The entire purchase of ${reversal.ReversedAmount:F2} was reversed and {stock}.";

        if (isFullBatchReversal)
            return $"Purchase reversed: ${reversal.ReversedAmount:F2} returned and {stock}.";

        return $"Refunded ${reversal.ReversedAmount:F2}"
            + (restocked > 0 ? $" and {stock}." : ". No units were returned to stock.");
    }

    // ═══════════════════════════════════════════
    //  HELPERS
    // ═══════════════════════════════════════════

    /// <summary>
    /// The ORIGINAL card charge on a batch: the earliest credit-card row that carries a gateway
    /// transaction id. Later rows are the credits and voids already applied against it, and
    /// reversing one of those instead is how a refund ends up pointed at the wrong transaction.
    /// </summary>
    private async Task<StoreCartBatchAccounting?> FindOriginalCardChargeAsync(
        int storeCartBatchId, CancellationToken ct)
    {
        var rows = await _analyticsRepo.GetTrackedBatchAccountingAsync(storeCartBatchId, ct);

        return rows.FirstOrDefault(a =>
            (a.PaymentMethodId == CreditCardPaymentMethodId
                || a.PaymentMethodId == CreditCardVoidMethodId)
            && !string.IsNullOrWhiteSpace(a.AdnTransactionId));
    }

    private async Task<int> ResolveStoreIdAsync(Guid jobId, CancellationToken ct)
    {
        var store = await _storeRepo.GetByJobIdAsync(jobId, ct)
            ?? throw new InvalidOperationException("Store not found for this job.");
        return store.StoreId;
    }

    /// <summary>
    /// Load a purchased line and confirm it belongs to the caller's store. StoreCartBatchSkuId is
    /// a bare integer from the client and is not job-scoped, so every operation that moves money or
    /// stock must establish this first.
    /// </summary>
    private async Task<(StoreCartBatchSkus Line, int StoreId)> LoadLineInStoreAsync(
        Guid jobId, int storeCartBatchSkuId, CancellationToken ct)
    {
        var storeId = await ResolveStoreIdAsync(jobId, ct);

        var line = await _analyticsRepo.GetTrackedLineAsync(storeCartBatchSkuId, ct)
            ?? throw new InvalidOperationException("Purchase line not found.");

        if (line.StoreCartBatch.StoreCart.StoreId != storeId)
            throw new InvalidOperationException("Purchase line not found in this job's store.");

        return (line, storeId);
    }

    private async Task AssertBatchInStoreAsync(Guid jobId, int storeCartBatchId, CancellationToken ct)
    {
        var storeId = await ResolveStoreIdAsync(jobId, ct);

        var lines = await _analyticsRepo.GetTrackedBatchLinesAsync(storeCartBatchId, ct);
        if (lines.Count == 0)
            throw new InvalidOperationException("Purchase not found.");

        var line = await _analyticsRepo.GetTrackedLineAsync(lines[0].StoreCartBatchSkuId, ct);
        if (line == null || line.StoreCartBatch.StoreCart.StoreId != storeId)
            throw new InvalidOperationException("Purchase not found in this job's store.");
    }

    private static StoreRefundResponse Fail(string message) =>
        new() { Success = false, Message = message };
}
