using TSIC.API.Services.Shared.Files;
using TSIC.Contracts.Dtos.Store;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;

namespace TSIC.API.Services.Store;

/// <summary>
/// Builds the store admin grids' .xlsx exports. Port of the EJ2 toolbar `ExcelExport` on legacy's
/// StoreItems, StoreSkus, StoreSales and StoreCartQuantityAdjustments views.
///
/// <para>
/// LEGACY FINDING: the Items and Skus toolbars declare an `ExcelExport` button but never set
/// `allowExcelExport="true"` on the grid, so in legacy BOTH buttons are inert — clicking them
/// does nothing. Only StoreSales, StoreRefunded and StoreCartQuantityAdjustments enable it. We
/// implement all four rather than replicating two dead buttons (same call as R-10): the column
/// sets are legacy's own grid definitions, so the exports are what legacy intended to produce.
/// </para>
///
/// <para>
/// Every read here is a read a screen already performs — no export-only query. That keeps the
/// numbers in the workbook identical to the numbers on the tab by construction, rather than by
/// two queries agreeing.
/// </para>
/// </summary>
public class StoreExportService : IStoreExportService
{
    private readonly IStoreCatalogService _catalogService;
    private readonly IStoreSalesOpsService _salesOpsService;
    private readonly IStoreAdminService _adminService;
    private readonly IStoreItemRepository _itemRepo;
    private readonly IStoreRepository _storeRepo;

    public StoreExportService(
        IStoreCatalogService catalogService,
        IStoreSalesOpsService salesOpsService,
        IStoreAdminService adminService,
        IStoreItemRepository itemRepo,
        IStoreRepository storeRepo)
    {
        _catalogService = catalogService;
        _salesOpsService = salesOpsService;
        _adminService = adminService;
        _itemRepo = itemRepo;
        _storeRepo = storeRepo;
    }

    // ── Items ──

    public async Task<StoreExportFile> ExportItemsAsync(Guid jobId, CancellationToken ct = default)
    {
        var items = await _catalogService.GetItemsAsync(jobId);

        var sheet = new ExcelSheet { Name = "Items" }
            .WithColumns("Active", "Item", "Sort Order");

        foreach (var item in items.OrderBy(i => i.SortOrder).ThenBy(i => i.StoreItemName))
        {
            sheet.Rows.Add([YesNo(item.Active), item.StoreItemName, item.SortOrder]);
        }

        return Workbook("Store-Items", sheet);
    }

    // ── Skus ──

    public async Task<StoreExportFile> ExportSkusAsync(Guid jobId, CancellationToken ct = default)
    {
        var store = await _storeRepo.GetByJobIdAsync(jobId, ct);

        // No store yet means no items and no SKUs — an empty workbook with headers, not a 404.
        // A director who has never added a product still gets a file back.
        var skus = store is null
            ? []
            : await _itemRepo.GetAllSkusWithAvailabilityAsync(store.StoreId, ct);

        var sheet = new ExcelSheet { Name = "Skus" }
            .WithColumns("Item", "Active", "Sku", "PickedUp", "Sold", "UnSold", "MaxCanSell", "Price");

        foreach (var sku in skus)
        {
            sheet.Rows.Add([
                sku.StoreItemName,
                YesNo(sku.Active),
                sku.SkuLabel,
                sku.PickedUpCount,
                sku.SoldCount,
                sku.UnSoldCount,
                sku.MaxCanSell,
                sku.Price
            ]);
        }

        return Workbook("Store-Skus", sheet);
    }

    // ── Sales ──

    public async Task<StoreExportFile> ExportSalesAsync(
        Guid jobId, bool walkUpOnly, CancellationToken ct = default)
    {
        var lines = await _salesOpsService.GetSaleLinesAsync(jobId, walkUpOnly, ct);

        // Legacy's 24 data columns in grid order, hidden ones included (includeHiddenColumn:true).
        // Two legacy columns are deliberately absent: NewSku and New Sku Quantity are the inline
        // editor's scratch fields for the swap command — they hold no data on any row and export
        // as 24 blank cells. Two more corrections, both from legacy's own markup:
        //   • legacy labels the Restocked column "Refunded" as well, giving the workbook two
        //     identically-named money columns that are not the same thing;
        //   • Restocked is a UNIT COUNT formatted as currency (format="c2"), so legacy's export
        //     renders "3 units restocked" as "$3.00".
        var sheet = new ExcelSheet { Name = walkUpOnly ? "Walk-Up Sales" : "Sales" }
            .WithColumns(
                "Batch", "CartSkuId", "Sku", "Quantity", "A",
                "Purchased", "Modified", "Item",
                "UPrice", "Fee-Prod", "Fee-Proc", "Fee-Tot", "Paid-Tot",
                "Refunded", "Restocked",
                "Family Username", "DeliverToFN", "DeliverToLN",
                "dt:Club", "dt:Age", "dt:Pool", "dt:Team", "dt:Email", "dt:Cellphone");

        foreach (var line in lines)
        {
            sheet.Rows.Add([
                line.StoreCartBatchId,
                line.StoreCartBatchSkuId,
                line.SkuLabel,
                line.Quantity,
                YesNo(line.Active),
                line.PurchaseDate,
                line.ModifiedDate,
                line.ItemName,
                line.UnitPrice,
                line.FeeProduct,
                line.FeeProcessing,
                line.FeeTotal,
                line.Paid,
                line.Refunded,
                line.Restocked,
                line.FamilyUserName,
                line.DirectToFirstName,
                line.DirectToLastName,
                line.DirectToClub,
                line.DirectToAgegroup,
                line.DirectToPool,
                line.DirectToTeam,
                line.DirectToEmail,
                line.DirectToCellphone
            ]);
        }

        return Workbook(walkUpOnly ? "Store-WalkUp-Sales" : "Store-Sales", sheet);
    }

    // ── Quantity adjustments ──

    public async Task<StoreExportFile> ExportQuantityAdjustmentsAsync(
        Guid jobId, CancellationToken ct = default)
    {
        var rows = await _adminService.GetQuantityAdjustmentsAsync(jobId, ct);

        var sheet = new ExcelSheet { Name = "Quantity Adjustments" }
            .WithColumns(
                "AdjQty", "Sku", "FromQty", "ToQty",
                "F-Username", "FNParent", "LNParent", "Email", "When");

        foreach (var row in rows)
        {
            sheet.Rows.Add([
                row.AdjQuantity,
                row.SkuLabel,
                row.FromQuantity,
                row.ToQuantity,
                row.FamilyUserName,
                row.ParentFirstName,
                row.ParentLastName,
                row.Email,
                row.WhenChanged
            ]);
        }

        return Workbook("Store-Quantity-Adjustments", sheet);
    }

    // ── Shared ──

    /// <summary>
    /// Booleans go out as Yes/No, not TRUE/FALSE. Legacy's grid rendered these as checkboxes
    /// (`displayAsCheckBox`), which has no equivalent in a cell — a word is what a reader of a
    /// spreadsheet can filter on.
    /// </summary>
    private static string YesNo(bool value) => value ? "Yes" : "No";

    private static StoreExportFile Workbook(string baseName, ExcelSheet sheet) => new()
    {
        FileName = $"{baseName}-{DateTime.Now:yyyy-MM-dd}.xlsx",
        Content = ExcelWorkbookWriter.Build([sheet])
    };
}
