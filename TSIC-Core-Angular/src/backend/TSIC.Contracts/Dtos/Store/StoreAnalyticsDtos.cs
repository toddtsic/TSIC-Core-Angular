namespace TSIC.Contracts.Dtos.Store;

// ── Sales Analytics ──

/// <summary>
/// Sales pivot data — the single dataset behind all three of legacy's Store Dashboard pivots
/// (Sales Rollup table, Product Sales chart, Sales Rollup chart). Legacy's
/// <c>GetJobPurchasesPivotData</c> shipped one row per line item and let the pivot component
/// aggregate; this is grouped to the same grain the pivot rolls up to (item + sku + year + month),
/// which is arithmetically identical for Sum aggregates and a fraction of the payload.
/// </summary>
public record StoreSalesPivotDto
{
    public required string ItemName { get; init; }

    /// <summary>Legacy's <c>storeItemSku</c>: "Item:Size:Color" — the pivot's second row level.</summary>
    public required string SkuLabel { get; init; }

    public required int Month { get; init; }
    public required int Year { get; init; }

    /// <summary>Quantity NET of restocks — a restocked unit came back and was not sold.</summary>
    public required int UnitsSold { get; init; }

    /// <summary>Paid NET of refunds. A line that was never paid contributes zero, not a negative.</summary>
    public required decimal Revenue { get; init; }
}

/// <summary>
/// Sales totals by item (for pie chart).
/// </summary>
public record StoreSalesByItemDto
{
    public required string ItemName { get; init; }
    public required int TotalUnitsSold { get; init; }
    public required decimal TotalRevenue { get; init; }
}

/// <summary>
/// Payment record with customer details.
/// </summary>
public record StorePaymentDetailDto
{
    public required int StoreCartBatchAccountingId { get; init; }
    public required int StoreCartBatchId { get; init; }
    public required string FamilyUserId { get; init; }
    public required string FamilyUserName { get; init; }
    public required string PaymentMethodName { get; init; }
    public required decimal Paid { get; init; }
    public required DateTime CreateDate { get; init; }
    public string? Cclast4 { get; init; }
    public string? AdnInvoiceNo { get; init; }
    public string? AdnTransactionId { get; init; }
    public string? Comment { get; init; }
    public required bool IsWalkUp { get; init; }
}

/// <summary>
/// Family purchase history with all transactions.
/// </summary>
public record StoreFamilyPurchaseDto
{
    public required string FamilyUserId { get; init; }
    public required string FamilyUserName { get; init; }
    public required List<StoreFamilyTransactionDto> Transactions { get; init; }
    public required decimal TotalSpent { get; init; }
}

/// <summary>
/// A single family transaction (one checkout batch).
/// </summary>
public record StoreFamilyTransactionDto
{
    public required int StoreCartBatchId { get; init; }
    public required DateTime PurchaseDate { get; init; }
    public required decimal TotalPaid { get; init; }
    public required int ItemCount { get; init; }
    public required List<StoreCartLineItemDto> Items { get; init; }
}

// ── Refund & Restock ──

/// <summary>
/// A line item that has been partially or fully refunded.
/// </summary>
public record StoreRefundedItemDto
{
    public required int StoreCartBatchSkuId { get; init; }
    public required string ItemName { get; init; }
    public string? ColorName { get; init; }
    public string? SizeName { get; init; }

    /// <summary>Whether the purchased LINE is still active — legacy's "Active" column.</summary>
    public required bool Active { get; init; }

    public required int Quantity { get; init; }
    public required decimal FeeProduct { get; init; }
    public required decimal FeeProcessing { get; init; }
    public required decimal FeeTotal { get; init; }
    public required decimal PaidTotal { get; init; }
    public required decimal RefundedTotal { get; init; }

    /// <summary>
    /// What is still refundable on this line. Legacy computes <c>FeeTotal - RefundedTotal</c>, not
    /// <c>PaidTotal - RefundedTotal</c> as its own refund dialog caps at. The two agree on every
    /// line that was actually paid (654 lines, 178 differ, and in every one of those the line is
    /// unpaid so <c>PaidTotal</c> is 0 while <c>FeeTotal</c> holds what is owed) — a line that was
    /// never paid cannot be refunded, so the difference is unreachable. Legacy's formula kept.
    /// </summary>
    public required decimal SkuRefundable { get; init; }

    /// <summary>Units of this line already put back on the shelf.</summary>
    public required int Restocked { get; init; }

    public required string FamilyUserName { get; init; }
    public required DateTime ModifiedDate { get; init; }
}

/// <summary>
/// A restock history entry.
/// </summary>
public record StoreRestockedItemDto
{
    public required int StoreCartBatchSkuRestockId { get; init; }

    /// <summary>The purchase the restocked unit came off — legacy's Id-B / BatchId.</summary>
    public required int StoreCartBatchId { get; init; }

    /// <summary>The purchased LINE — legacy's CartSkuId, the id the restock hangs on.</summary>
    public required int StoreCartBatchSkuId { get; init; }

    public required string ItemName { get; init; }
    public string? ColorName { get; init; }
    public string? SizeName { get; init; }

    /// <summary>How many units the family bought on that line, against which this restock counts.</summary>
    public required int SkuQuantity { get; init; }

    public required int RestockCount { get; init; }
    public required decimal PaidTotal { get; init; }
    public required decimal RefundedTotal { get; init; }

    /// <summary>When the line was bought — legacy's "Purchased".</summary>
    public required DateTime PurchaseDate { get; init; }

    public required string FamilyUserName { get; init; }

    /// <summary>Who the merchandise was for, when the line was directed to a registrant.</summary>
    public string? DirectToPlayerName { get; init; }

    public required DateTime ModifiedDate { get; init; }
    public required string ModifiedBy { get; init; }
}

// ── Admin Requests ──

public record LogRestockRequest
{
    public required int StoreCartBatchSkuId { get; init; }
    public required int RestockCount { get; init; }
}

public record SignForPickupRequest
{
    public required int StoreCartBatchId { get; init; }
    public required string SignedForBy { get; init; }
}
