using TSIC.Contracts.Dtos;

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

    /// <summary>True iff this job's merchant account accepts AMEX (see IJobPaymentFeaturesService).
    /// Gates whether store checkout offers AMEX as a card type. Fail-closed false.</summary>
    public bool JobUsesAmex { get; init; }
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
    public string? DirectToPlayerName { get; init; }
    public required bool Active { get; init; }
}

// ── Family Players (for DirectTo dropdown) ──

/// <summary>
/// A registered player in the family, used for the DirectTo picker in the store.
/// </summary>
public record StoreFamilyPlayerDto
{
    public required Guid RegistrationId { get; init; }
    public required string FirstName { get; init; }
    public required string LastName { get; init; }
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
/// Checkout request. For credit card payments, CreditCard must be populated;
/// the backend charges via Authorize.Net. For Cash/Check, CreditCard is null.
/// </summary>
public record StoreCheckoutRequest
{
    public required Guid PaymentMethodId { get; init; }
    public CreditCardInfo? CreditCard { get; init; }
    public string? Comment { get; init; }
    public int? DiscountCodeAi { get; init; }
}

/// <summary>
/// Checkout result. Always check Success before displaying confirmation.
/// </summary>
public record StoreCheckoutResultDto
{
    public required bool Success { get; init; }
    public required int StoreCartBatchId { get; init; }
    public required decimal TotalPaid { get; init; }
    public string? TransactionId { get; init; }
    public string? InvoiceNo { get; init; }
    public string? Message { get; init; }
    public string? ErrorCode { get; init; }

    /// <summary>
    /// The buyer is the walk-up counter registration rather than a family with an account —
    /// legacy's <c>isWalkupRegistration</c> (<c>StoreFamilyController.CheckoutConfirmation</c>,
    /// where the test is the caller's team name being "Store Merch"). Drives the confirmation
    /// copy and the kiosk sign-out. See A-24.
    /// </summary>
    public bool IsWalkUp { get; init; }
}

// ── Checkout availability re-check (legacy GetAllSkusAvailableStatus) ──

/// <summary>
/// One line the checkout re-check reduced or removed.
/// </summary>
public record StoreCartTrimAdjustmentDto
{
    public required int StoreSkuId { get; init; }
    public required string SkuLabel { get; init; }
    public required int FromQuantity { get; init; }
    /// <summary>Zero when the line was removed outright.</summary>
    public required int ToQuantity { get; init; }
}

/// <summary>
/// The cart as it stands after the checkout page re-checks availability.
/// </summary>
/// <remarks>
/// Legacy trims the cart to what is actually still in stock when the shopper ENTERS checkout
/// (StoreFamilyController.Checkout GET) and again on submit, then redirects with
/// <c>bCartHasBeenAutoUpdated=true</c> so the banner shows before any money moves. This DTO is
/// that redirect flag, plus what actually changed — legacy's banner said only "your cart has
/// been updated" and left the shopper to find the difference themselves.
/// </remarks>
public record StoreCheckoutPrepareDto
{
    public required StoreCartBatchDto Cart { get; init; }
    public required bool WasAutoUpdated { get; init; }
    public required List<StoreCartTrimAdjustmentDto> Adjustments { get; init; }
}

// ── Receipts ──

/// <summary>
/// Who a completed purchase belongs to, and who its receipt goes to.
///
/// <para>
/// The <see cref="JobId"/> and <see cref="FamilyUserId"/> here are a SECURITY BOUNDARY, not
/// decoration. Every receipt read must check the batch against the caller's job — and, for a
/// shopper, against their own family — before a byte of the PDF is generated. A store receipt
/// carries the buyer's name, the registrants the goods were directed to, the amounts, and the
/// last four of the card.
/// </para>
///
/// <para>
/// LEGACY recipient rule (<c>StoreFamilyController.SendEmailReceipt</c>): Mom's email, then Dad's,
/// then the email of every registrant a line was directed to, each added only if not already
/// present. Blank addresses are skipped. Legacy performed NO ownership check on the batch id at
/// all — see D-11.
/// </para>
/// </summary>
public record StoreReceiptContextDto
{
    public required Guid JobId { get; init; }
    public required string JobName { get; init; }
    public string? DisplayName { get; init; }
    public required string FamilyUserId { get; init; }

    public string? MomEmail { get; init; }
    public string? DadEmail { get; init; }

    /// <summary>
    /// Emails of the registrants this purchase's lines were directed to. On the live data 87 of
    /// 651 directed lines carry an address that is neither parent's — a player address the family
    /// chose to enter, which is why legacy mails it.
    /// </summary>
    public required List<string> DirectedEmails { get; init; }

    /// <summary>The job's store contact address — becomes the receipt's Reply-To.</summary>
    public string? StoreContactEmail { get; init; }
}

/// <summary>
/// One row of the shopper's purchase history — legacy <c>StoreFamily/Invoices</c>
/// (<c>GetFamilyTxBatchHistory</c>).
///
/// <para>
/// The grain is the ACCOUNTING RECORD, not the batch. Legacy groups by
/// (batch, date, invoice, paid, method), so a purchase that was later refunded produces two rows —
/// the charge and the reversal. That reads oddly as "invoices" but it is the truthful thing for a
/// shopper looking for what happened to their money, and it is what legacy showed.
/// </para>
/// </summary>
public record StoreFamilyPurchaseHistoryRowDto
{
    public required int StoreCartBatchId { get; init; }
    public required DateTime PaymentDate { get; init; }

    /// <summary>The ADN invoice number. Null on a walk-up or cash sale, which is why legacy's
    /// toolbar refused to act on a row without one.</summary>
    public string? AdnInvoiceNo { get; init; }

    public required decimal PaidTotal { get; init; }
    public required string PaymentMethod { get; init; }
}
