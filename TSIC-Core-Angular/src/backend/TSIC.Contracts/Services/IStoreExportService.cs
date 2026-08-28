namespace TSIC.Contracts.Services;

/// <summary>An .xlsx ready to stream: the bytes and the filename to hand the browser.</summary>
public record StoreExportFile
{
    public required string FileName { get; init; }
    public required byte[] Content { get; init; }
}

/// <summary>
/// The store admin grids' Excel exports — port of the EJ2 toolbar `ExcelExport` on legacy's
/// StoreItems, StoreSkus, StoreSales and StoreCartQuantityAdjustments views.
///
/// <para>
/// Legacy exported CLIENT-side from the grid's own column set, so an export always matched what
/// was on screen. We build the workbook server-side instead, which is why the column lists below
/// are pinned to legacy's grid definitions rather than to whatever our tabs happen to render —
/// the export is the full record, the screen is the working subset (see R-15).
/// </para>
/// </summary>
public interface IStoreExportService
{
    /// <summary>Items grid: Active · Item · Sort Order.</summary>
    Task<StoreExportFile> ExportItemsAsync(Guid jobId, CancellationToken ct = default);

    /// <summary>
    /// Skus grid, store-wide: Item · Active · Sku · PickedUp · Sold · UnSold · MaxCanSell · Price.
    /// </summary>
    Task<StoreExportFile> ExportSkusAsync(Guid jobId, CancellationToken ct = default);

    /// <summary>
    /// Sales grid with legacy's hidden columns included (legacy passed
    /// <c>includeHiddenColumn: true</c>) — the whole 24-column record of every purchased line.
    /// <paramref name="walkUpOnly"/> narrows to counter sales, legacy's StoreSalesWalkup screen.
    /// </summary>
    Task<StoreExportFile> ExportSalesAsync(Guid jobId, bool walkUpOnly, CancellationToken ct = default);

    /// <summary>Quantity Adjustments grid: every cart the checkout re-check had to cut back.</summary>
    Task<StoreExportFile> ExportQuantityAdjustmentsAsync(Guid jobId, CancellationToken ct = default);
}
