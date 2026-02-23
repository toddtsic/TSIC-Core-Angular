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
    public required string StoreItemName { get; init; }
    public required decimal StoreItemPrice { get; init; }
    public required bool Active { get; init; }
    public required int SortOrder { get; init; }
    public required int SkuCount { get; init; }
    public required int ActiveSkuCount { get; init; }
}

/// <summary>
/// Full item detail with SKUs and image URLs.
/// </summary>
public record StoreItemDto
{
    public required int StoreItemId { get; init; }
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
/// Create a new store item with optional size/color matrix.
/// If both ColorIds and SizeIds are provided, SKUs are generated as the cross-product.
/// If one list is empty, SKUs are generated per the other dimension.
/// If both are empty, a single default SKU is created.
/// </summary>
public record CreateStoreItemRequest
{
    public required string StoreItemName { get; init; }
    public string? StoreItemComments { get; init; }
    public required decimal StoreItemPrice { get; init; }
    public required List<int> ColorIds { get; init; }
    public required List<int> SizeIds { get; init; }
    public required int MaxCanSell { get; init; }
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
}

public record UpdateStoreSkuRequest
{
    public required bool Active { get; init; }
    public required int MaxCanSell { get; init; }
}
