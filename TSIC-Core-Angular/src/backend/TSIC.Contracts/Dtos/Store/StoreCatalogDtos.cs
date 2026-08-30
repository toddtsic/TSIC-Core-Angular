namespace TSIC.Contracts.Dtos.Store;

// ── Store ──

/// <summary>
/// Store identity for a job.
/// </summary>
public record StoreDto
{
    public required int StoreId { get; init; }
    public required Guid JobId { get; init; }
}

/// <summary>
/// The three pieces of shopper-facing copy the director writes for their store —
/// <c>Jobs.StorePickupDetails</c>, <c>Jobs.StoreRefundPolicy</c>, <c>Jobs.StoreContactEmail</c>.
///
/// <para>
/// Legacy surfaced these as a Pickup / Return Policy / Contact tab strip on EVERY item card in
/// the storefront, and again as three labelled lines on the checkout page. They are job-level,
/// not per-item, so the tab strip repeated identical text once per product.
/// </para>
///
/// <para>
/// All three are nullable and usually are: most of the 1,096 jobs never fill them in. A surface
/// showing this must render nothing at all when all three are empty rather than an empty panel
/// with three blank headings.
/// </para>
///
/// <para>
/// PLAIN TEXT, not HTML. The job config editor collects them in plain `textarea`s and legacy
/// rendered them through Razor's HTML-encoding interpolation. Interpolate; never bind
/// `[innerHTML]`. Line breaks are meaningful — the director typed them.
/// </para>
/// </summary>
public record StoreFrontInfoDto
{
    public string? PickupDetails { get; init; }
    public string? RefundPolicy { get; init; }
    public string? ContactEmail { get; init; }

    /// <summary>False when the director has filled none of the three in.</summary>
    public required bool HasAny { get; init; }
}

// ── Colors ──

/// <summary>
/// Store color lookup value.
/// </summary>
public record StoreColorDto
{
    public required int StoreColorId { get; init; }
    public required string StoreColorName { get; init; }
}

public record CreateStoreColorRequest
{
    public required string StoreColorName { get; init; }
}

public record UpdateStoreColorRequest
{
    public required string StoreColorName { get; init; }
}

// ── Sizes ──

/// <summary>
/// Store size lookup value.
/// </summary>
public record StoreSizeDto
{
    public required int StoreSizeId { get; init; }
    public required string StoreSizeName { get; init; }
}

public record CreateStoreSizeRequest
{
    public required string StoreSizeName { get; init; }
}

public record UpdateStoreSizeRequest
{
    public required string StoreSizeName { get; init; }
}

// ── Items ──

/// <summary>
/// Item summary for list views (no SKU details).
/// </summary>
public record StoreItemSummaryDto
{
    public required int StoreItemId { get; init; }
    public required int StoreId { get; init; }
    public required string StoreItemName { get; init; }
    public required decimal StoreItemPrice { get; init; }
    public required bool Active { get; init; }
    public required int SortOrder { get; init; }
    public required int SkuCount { get; init; }
    public required int ActiveSkuCount { get; init; }
    public required List<string> ImageUrls { get; init; }
    /// <summary>
    /// Set when ActiveSkuCount == 1, enabling quick-add without expanding the picker.
    /// </summary>
    public int? SingleSkuId { get; init; }

    /// <summary>
    /// Legacy `listSoldOutOrInactiveSkus` — the SKU labels a shopper cannot buy, either because
    /// availability has reached zero or because the SKU is inactive.
    ///
    /// <para>
    /// Legacy shows the item regardless and NAMES these variants, rather than hiding the product.
    /// Availability here is `MaxCanSell - Sold` on the legacy basis: in-cart units are NOT
    /// deducted (see GetSkuAvailableCountBySoldAndBuffer), so another family holding the last one
    /// in an unpaid cart does not make it read as sold out.
    /// </para>
    /// </summary>
    public required List<string> SoldOutOrInactiveSkuLabels { get; init; }

    /// <summary>
    /// Distinct colour names across the item's ACTIVE SKUs, so the card can show what the product
    /// comes in without opening it. "14 options" is a warehouse count; "Black · Blue" is what a
    /// shopper is deciding between.
    /// </summary>
    ///
    /// <para>
    /// Non-nullable by design: a nullable List&lt;T&gt; generates as <c>any[]</c> in the TypeScript
    /// client, which loses the element type at every call site. Empty list, never null.
    /// </para>
    public required List<string> ColorNames { get; init; }

    /// <summary>
    /// Distinct size names across the item's ACTIVE SKUs, in the order the SKUs come back, so the
    /// card can say "Youth S – Adult XL" rather than a count.
    /// </summary>
    public required List<string> SizeNames { get; init; }
}

/// <summary>
/// Full item detail with SKUs and image URLs.
/// </summary>
public record StoreItemDto
{
    public required int StoreItemId { get; init; }
    public required int StoreId { get; init; }
    public required string StoreItemName { get; init; }
    public string? StoreItemComments { get; init; }
    public required decimal StoreItemPrice { get; init; }
    public required bool Active { get; init; }
    public required int SortOrder { get; init; }
    public required DateTime Modified { get; init; }
    public required List<StoreSkuDto> Skus { get; init; }
    public required List<string> ImageUrls { get; init; }
}

/// <summary>
/// Create a new store item. Mirrors legacy CreateNewStoreItemDto
/// (StoreItemsController.CreateNewStoreItem).
///
/// Sizes and colours arrive as free text, semicolon-delimited, and are resolved by NAME against
/// the GLOBAL StoreSizes / StoreColors tables — those are a shared dictionary with no store or
/// job scoping. A name that does not exist yet is created.
///
/// SKU matrix: both dimensions → cross-product; one dimension → SKUs on that dimension only;
/// neither → a single default SKU with both null.
///
/// There is deliberately no MaxCanSell here — legacy has no stock field at creation; stock is
/// set afterwards on the Skus screen. StoreItemComments is accepted because legacy's DTO carries
/// it, but the create modal does not collect it, so in practice it arrives null.
/// </summary>
public record CreateStoreItemRequest
{
    public required string StoreItemName { get; init; }
    public string? StoreItemComments { get; init; }
    public required decimal StoreItemPrice { get; init; }

    /// <summary>Semicolon-delimited size names, e.g. "Adult Small;Adult Medium;Adult Large".</summary>
    public string? ItemSizes { get; init; }

    /// <summary>Semicolon-delimited colour names, e.g. "White;GrayS".</summary>
    public string? ItemColors { get; init; }
}

public record UpdateStoreItemRequest
{
    public required string StoreItemName { get; init; }
    public string? StoreItemComments { get; init; }
    public required decimal StoreItemPrice { get; init; }
    public required bool Active { get; init; }
    public required int SortOrder { get; init; }
}

// ── Images ──

/// <summary>Id + name of one store item, for surfaces that only need to label a row.</summary>
public record StoreItemKeyDto
{
    public required int StoreItemId { get; init; }
    public required string StoreItemName { get; init; }
}

/// <summary>
/// One row of the legacy store-images grid (StoreImagesController.Index /
/// IStoreService.GetJobItemsPictures): one image file belonging to one item.
///
/// <para>
/// Legacy lists EVERY item in the job. An item with no file on disk still gets a row, carrying
/// the missing-image.jpg placeholder — that is how a director sees at a glance which products
/// have no photo. Those rows have <see cref="IsPlaceholder"/> true and cannot be deleted.
/// </para>
/// </summary>
public record StoreItemImageDto
{
    public required int StoreItemId { get; init; }
    public required string StoreItemName { get; init; }

    /// <summary>
    /// The trailing number in the legacy filename {storeId}-{storeItemId}-{instance}.jpg.
    /// Instances are contiguous from 1 and are renumbered when one is deleted, so this is a
    /// position, not a stable id. Zero on a placeholder row.
    /// </summary>
    public required int Instance { get; init; }

    public required string FileName { get; init; }

    /// <summary>
    /// Absolute statics URL, with a cache-busting query so a replaced image is picked up
    /// immediately rather than served from the browser cache (legacy AddCacheBuster).
    /// </summary>
    public required string ImageUrl { get; init; }

    /// <summary>True for the missing-image.jpg stand-in shown when an item has no photo.</summary>
    public required bool IsPlaceholder { get; init; }
}

// ── SKUs ──

/// <summary>
/// SKU with color/size names and availability counts.
/// </summary>
public record StoreSkuDto
{
    public required int StoreSkuId { get; init; }
    public required int StoreItemId { get; init; }

    /// <summary>Legacy `Item` column — the parent item's name, carried so a store-wide SKU list
    /// can group without a second lookup.</summary>
    public required string StoreItemName { get; init; }

    /// <summary>
    /// Legacy `Price` — <c>StoreItem.StoreItemPrice</c>. Price lives on the ITEM, not the SKU;
    /// every SKU under an item carries the same figure.
    /// </summary>
    public required decimal Price { get; init; }

    public int? StoreColorId { get; init; }
    public string? StoreColorName { get; init; }
    public int? StoreSizeId { get; init; }
    public string? StoreSizeName { get; init; }
    public required bool Active { get; init; }
    public required int MaxCanSell { get; init; }
    public required int SoldCount { get; init; }
    public required int InCartCount { get; init; }
    public required int AvailableCount { get; init; }

    /// <summary>
    /// Legacy `SkuLabel` — "Item:Size:Color", with "::" collapsed and stray colons trimmed when a
    /// dimension is null. Built server-side so every surface shows the same string.
    /// </summary>
    public required string SkuLabel { get; init; }

    /// <summary>
    /// Legacy `PickedUp` (CartBatchSkuItemsSignedFor): paid units on a batch with a
    /// SignedForDate, net of restocks. What has physically left the table.
    /// </summary>
    public required int PickedUpCount { get; init; }

    /// <summary>
    /// Legacy `UnSold` = MaxCanSell − Sold. Deliberately does NOT deduct in-cart units, so it is
    /// stock-on-hand for a director, not the shopper-facing availability figure.
    /// </summary>
    public required int UnSoldCount { get; init; }
}

public record UpdateStoreSkuRequest
{
    public required bool Active { get; init; }
    public required int MaxCanSell { get; init; }
}
