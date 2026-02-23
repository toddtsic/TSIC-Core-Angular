namespace TSIC.Contracts.Dtos.Store;

// ── Sales Analytics ──

/// <summary>
/// Sales pivot data: units and revenue by item by year-month.
/// </summary>
public record StoreSalesPivotDto
{
    public required string ItemName { get; init; }
    public required int Month { get; init; }
    public required int Year { get; init; }
    public required int UnitsSold { get; init; }
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
    public required int Quantity { get; init; }
    public required decimal PaidTotal { get; init; }
    public required decimal RefundedTotal { get; init; }
    public required string FamilyUserName { get; init; }
    public required DateTime ModifiedDate { get; init; }
}

/// <summary>
/// A restock history entry.
/// </summary>
public record StoreRestockedItemDto
{
    public required int StoreCartBatchSkuRestockId { get; init; }
    public required string ItemName { get; init; }
    public string? ColorName { get; init; }
    public string? SizeName { get; init; }
    public required int RestockCount { get; init; }
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
