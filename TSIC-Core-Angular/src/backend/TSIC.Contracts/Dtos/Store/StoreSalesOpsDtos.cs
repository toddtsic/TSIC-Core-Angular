namespace TSIC.Contracts.Dtos.Store;

// ═══════════════════════════════════════════
//  SALES LINE GRID
//  Legacy StoreSales/Index + StoreSalesWalkup/Index (IStoreService.GetJobStorePaymentData).
// ═══════════════════════════════════════════

/// <summary>
/// One PURCHASED LINE — a SKU on a batch that has been paid for. This is the grain of legacy's
/// sales grid and the row the Swap and Refund commands act on.
///
/// <para>
/// Distinct from <c>StorePaymentDetailDto</c>, which is one row per accounting record (a payment).
/// A single payment covers many lines; you refund a payment but you swap a line, so both grains
/// are needed and neither substitutes for the other.
/// </para>
/// </summary>
public record StoreSaleLineDto
{
    public required int StoreCartBatchSkuId { get; init; }
    public required int StoreCartBatchId { get; init; }
    public required int StoreSkuId { get; init; }
    public required bool Active { get; init; }

    /// <summary>The purchasing family's login.</summary>
    public required string FamilyUserName { get; init; }

    public required string ItemName { get; init; }

    /// <summary>"Item:Size:Color", collapsed when a dimension is null.</summary>
    public required string SkuLabel { get; init; }

    /// <summary>
    /// Units still with the customer: purchased minus restocked. A fully restocked line reads 0
    /// and stays visible — the sale happened, and the record of it must not disappear.
    /// </summary>
    public required int Quantity { get; init; }

    public required decimal UnitPrice { get; init; }
    public required decimal FeeProduct { get; init; }
    public required decimal FeeProcessing { get; init; }
    public required decimal SalesTax { get; init; }
    public required decimal FeeTotal { get; init; }
    public required decimal Paid { get; init; }
    public required decimal Refunded { get; init; }

    /// <summary>
    /// Paid − Refunded. The ceiling on a further refund of this line, enforced server-side —
    /// legacy capped it in the dialog only, so the cap was advisory.
    /// </summary>
    public required decimal MaxCanRefund { get; init; }

    public required int Restocked { get; init; }

    /// <summary>Units still restockable: purchased minus already restocked.</summary>
    public required int MaxCanRestock { get; init; }

    /// <summary>Earliest accounting record on the batch — when the customer actually paid.</summary>
    public DateTime? PurchaseDate { get; init; }

    public required DateTime ModifiedDate { get; init; }

    /// <summary>True when this line was sold at the table rather than online.</summary>
    public required bool IsWalkUp { get; init; }

    // ── Who it is for ──
    // Legacy falls back to the purchasing family's Mom, then Dad, when a line is not directed to
    // a specific registrant, so the grid always names a human to hand the goods to.

    public string? DirectToFirstName { get; init; }
    public string? DirectToLastName { get; init; }
    public string? DirectToEmail { get; init; }
    public string? DirectToCellphone { get; init; }
    public string? DirectToClub { get; init; }
    public string? DirectToAgegroup { get; init; }
    public string? DirectToPool { get; init; }
    public string? DirectToTeam { get; init; }
}

// ═══════════════════════════════════════════
//  SWAP
//  Legacy GetCartItemSkuOptions + UpdateCartSku (non-refund branch).
// ═══════════════════════════════════════════

/// <summary>
/// A SKU this line could be exchanged for: same item, different size or colour, active, and with
/// stock available.
/// </summary>
public record StoreSwapOptionDto
{
    public required int StoreSkuId { get; init; }
    public required string SkuLabel { get; init; }

    /// <summary>Units available. Shown so a director does not pick a variant down to its last one.</summary>
    public required int AvailableCount { get; init; }
}

/// <summary>
/// Exchange some or all units of a purchased line for a different SKU of the SAME item.
///
/// <para>
/// The customer paid for the item, not the variant, so no money moves: the price is unchanged and
/// the paid and refunded amounts are split proportionally between the old line and the new one.
/// Swapping fewer units than were bought splits the line in two.
/// </para>
/// </summary>
public record StoreSwapRequest
{
    public required int StoreCartBatchSkuId { get; init; }
    public required int NewStoreSkuId { get; init; }
    public required int Quantity { get; init; }
}

// ═══════════════════════════════════════════
//  REFUND / VOID
//  Legacy UpdateCartSku (refund branch) + GetCartBatchHasSettledStatus.
// ═══════════════════════════════════════════

/// <summary>
/// Whether a batch's card charge has settled at Authorize.Net, which decides what the admin UI may
/// offer: an unsettled charge can only be VOIDED in full, so the partial-refund controls are
/// meaningless on it.
/// </summary>
public record StoreBatchSettledStatusDto
{
    public required int StoreCartBatchId { get; init; }

    /// <summary>True once Authorize.Net reports the charge settled.</summary>
    public required bool IsSettled { get; init; }

    /// <summary>False when the batch has no card payment to reverse at all (cash, check).</summary>
    public required bool HasCardPayment { get; init; }

    /// <summary>Total paid across the batch — what a void would reverse.</summary>
    public required decimal BatchPaidTotal { get; init; }
}

/// <summary>
/// Refund a line, or void the whole batch behind it.
///
/// <para>
/// These are genuinely different operations and the caller must say which: a refund returns money
/// against ONE line and restocks the units the director chose, a void reverses the ENTIRE batch
/// and restocks every unit on it. Legacy routed both through one flag on a shared payload, which
/// is why its void path had to re-read the batch to work out what it was really doing.
/// </para>
/// </summary>
public record StoreRefundRequest
{
    public required int StoreCartBatchSkuId { get; init; }

    /// <summary>
    /// Reverse the entire batch: refund everything paid on it, restock every unit, and mark every
    /// line unpaid. <see cref="RefundAmount"/> and <see cref="RestockCount"/> are ignored.
    /// </summary>
    public bool VoidEntireBatch { get; init; }

    /// <summary>How much to return. Capped server-side at the line's Paid − Refunded.</summary>
    public decimal RefundAmount { get; init; }

    /// <summary>
    /// How many units go back on the shelf. Independent of the amount on purpose: a customer may
    /// be refunded for a damaged item that is not resellable, or returned goods may be restocked
    /// as a goodwill gesture without money moving.
    /// </summary>
    public int RestockCount { get; init; }

    public string? Reason { get; init; }
}

public record StoreRefundResponse
{
    public required bool Success { get; init; }
    public required string Message { get; init; }

    /// <summary>What was actually reversed at the gateway — the full batch on a void.</summary>
    public decimal RefundedAmount { get; init; }

    public int RestockedCount { get; init; }
    public string? TransactionId { get; init; }
}
