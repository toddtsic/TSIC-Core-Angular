namespace TSIC.Contracts.Dtos.Store;

// ── Cart Batch (the current unpaid order) ──

/// <summary>
/// Current cart state with all line items and computed totals.
/// </summary>
public record StoreCartBatchDto
{
    public required int StoreCartBatchId { get; init; }
    public required List<StoreCartLineItemDto> LineItems { get; init; }
    public required decimal Subtotal { get; init; }
    public required decimal TotalFees { get; init; }
    public required decimal TotalTax { get; init; }
    public required decimal GrandTotal { get; init; }
}

/// <summary>
/// A single line item in the cart with item/color/size names and financial breakdown.
/// </summary>
public record StoreCartLineItemDto
{
    public required int StoreCartBatchSkuId { get; init; }
    public required int StoreSkuId { get; init; }
    public required string ItemName { get; init; }
    public string? ColorName { get; init; }
    public string? SizeName { get; init; }
    public required int Quantity { get; init; }
    public required decimal UnitPrice { get; init; }
    public required decimal FeeProduct { get; init; }
    public required decimal FeeProcessing { get; init; }
    public required decimal SalesTax { get; init; }
    public required decimal FeeTotal { get; init; }
    public required decimal LineTotal { get; init; }
    public Guid? DirectToRegId { get; init; }
    public required bool Active { get; init; }
}

// ── Cart Requests ──

/// <summary>
/// Add a SKU to the cart. DirectToRegId is optional (null for walk-up, set for reg-linked).
/// </summary>
public record AddToCartRequest
{
    public required int StoreSkuId { get; init; }
    public required int Quantity { get; init; }
    public Guid? DirectToRegId { get; init; }
}

public record UpdateCartQuantityRequest
{
    public required int Quantity { get; init; }
}

// ── Availability ──

/// <summary>
/// SKU availability with breakdown of sold, in-cart, and remaining counts.
/// </summary>
public record SkuAvailabilityDto
{
    public required int StoreSkuId { get; init; }
    public required int MaxCanSell { get; init; }
    public required int SoldCount { get; init; }
    public required int InCartCount { get; init; }
    public required int AvailableCount { get; init; }
}

// ── Checkout ──

/// <summary>
/// Checkout request with payment processor details.
/// The actual card charge happens client-side via ADN; this records the result.
/// </summary>
public record StoreCheckoutRequest
{
    public required Guid PaymentMethodId { get; init; }
    public string? Cclast4 { get; init; }
    public string? CcexpDate { get; init; }
    public string? AdnInvoiceNo { get; init; }
    public string? AdnTransactionId { get; init; }
    public string? Comment { get; init; }
    public int? DiscountCodeAi { get; init; }
}

/// <summary>
/// Checkout result returned after successful payment recording.
/// </summary>
public record StoreCheckoutResultDto
{
    public required int StoreCartBatchId { get; init; }
    public required decimal TotalPaid { get; init; }
    public string? InvoiceNo { get; init; }
}
