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

// ── SKUs ──

/// <summary>
/// SKU with color/size names and availability counts.
/// </summary>
public record StoreSkuDto
{
    public required int StoreSkuId { get; init; }
    public required int StoreItemId { get; init; }
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
