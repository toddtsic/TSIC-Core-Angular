using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Services;

namespace TSIC.API.Controllers;

[ApiController]
[Route("api/adn-reconciliation")]
[Authorize(Roles = "Superuser")]
public class AdnReconciliationController : ControllerBase
{
    private readonly IAdnReconciliationService _service;

    public AdnReconciliationController(IAdnReconciliationService service)
    {
        _service = service;
    }

    /// <summary>
    /// POST /api/adn-reconciliation/run-monthly?settlementMonth=N&amp;settlementYear=Y
    /// Runs the month-end close: imports last month's settled ADN transactions (reg + merch) into
    /// Txs, then returns a .zip bundling two independent QuickBooks .iif files (registration + merch)
    /// and their backing .xlsx reports. Defaults to last month if no params supplied.
    /// Import + per-stack validation counts surface in response headers (X-Imported-Count,
    /// X-Skipped-Duplicates, X-Batches-Pulled, X-Transactions-Pulled, X-Iif-Reg-Trns-Source,
    /// X-Iif-Reg-Trns-Consolidated, X-Iif-Merch-Trns-Source, X-Iif-Merch-Trns-Consolidated).
    /// A source/consolidated TRNS mismatch on either stack means that .iif needs review.
    /// </summary>
    [HttpPost("run-monthly")]
    public async Task<IActionResult> RunMonthly(
        [FromQuery] int? settlementMonth,
        [FromQuery] int? settlementYear,
        CancellationToken cancellationToken)
    {
        var (month, year) = ResolveMonthYear(settlementMonth, settlementYear);

        var result = await _service.RunMonthlyAsync(month, year, cancellationToken);

        Response.Headers["X-Imported-Count"] = result.Imported.ToString();
        Response.Headers["X-Skipped-Duplicates"] = result.SkippedDuplicates.ToString();
        Response.Headers["X-Batches-Pulled"] = result.BatchesPulled.ToString();
        Response.Headers["X-Transactions-Pulled"] = result.TransactionsPulled.ToString();
        Response.Headers["X-Iif-Reg-Trns-Source"] = result.RegSourceTrnsCount.ToString();
        Response.Headers["X-Iif-Reg-Trns-Consolidated"] = result.RegConsolidatedTrnsCount.ToString();
        Response.Headers["X-Iif-Merch-Trns-Source"] = result.MerchSourceTrnsCount.ToString();
        Response.Headers["X-Iif-Merch-Trns-Consolidated"] = result.MerchConsolidatedTrnsCount.ToString();

        return File(
            result.Bundle.FileBytes,
            result.Bundle.ContentType,
            $"TSIC-AdnReconciliation-{year}-{month:D2}.zip");
    }

    /// <summary>
    /// POST /api/adn-reconciliation/import?settlementMonth=N&amp;settlementYear=Y
    /// Step 1 (load): pull last month's settled ADN batches (reg + merch) into Txs and return the
    /// import counts. Writes Txs only — no files. Defaults to last month.
    /// </summary>
    [HttpPost("import")]
    public async Task<ActionResult<AdnImportResult>> Import(
        [FromQuery] int? settlementMonth,
        [FromQuery] int? settlementYear,
        CancellationToken cancellationToken)
    {
        var (month, year) = ResolveMonthYear(settlementMonth, settlementYear);
        var result = await _service.ImportSettlementsAsync(month, year, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// POST /api/adn-reconciliation/prepare?settlementMonth=N&amp;settlementYear=Y
    /// Eager build after the download: runs the reconciliation sprocs ONCE and persists the month's
    /// ledger + zip to disk so Step 2 (review) and Step 3 (files) open instantly. Returns build metadata
    /// (built-at + per-stack TRNS counts). Defaults to last month.
    /// </summary>
    [HttpPost("prepare")]
    public async Task<ActionResult<MonthEndArtifactsInfo>> Prepare(
        [FromQuery] int? settlementMonth,
        [FromQuery] int? settlementYear,
        CancellationToken cancellationToken)
    {
        var (month, year) = ResolveMonthYear(settlementMonth, settlementYear);
        var result = await _service.PrepareAsync(month, year, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// GET /api/adn-reconciliation/ledger?settlementMonth=N&amp;settlementYear=Y
    /// Step 2 (present): the human-readable month-end ledger — the export workbook's tabs on screen.
    /// Reads existing Txs. Defaults to last month.
    /// </summary>
    [HttpGet("ledger")]
    public async Task<ActionResult<MonthEndLedger>> Ledger(
        [FromQuery] int? settlementMonth,
        [FromQuery] int? settlementYear,
        CancellationToken cancellationToken)
    {
        var (month, year) = ResolveMonthYear(settlementMonth, settlementYear);
        var result = await _service.GetLedgerAsync(month, year, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// GET /api/adn-reconciliation/files?settlementMonth=N&amp;settlementYear=Y
    /// Step 3 (files): generate + download the .zip (two QuickBooks .iif + backing .xlsx) from the Txs
    /// already imported for the month. No ADN pull. Per-stack TRNS parity in response headers.
    /// </summary>
    [HttpGet("files")]
    public async Task<IActionResult> Files(
        [FromQuery] int? settlementMonth,
        [FromQuery] int? settlementYear,
        CancellationToken cancellationToken)
    {
        var (month, year) = ResolveMonthYear(settlementMonth, settlementYear);
        var bundle = await _service.GenerateBundleAsync(month, year, cancellationToken);

        Response.Headers["X-Iif-Reg-Trns-Source"] = bundle.RegSourceTrnsCount.ToString();
        Response.Headers["X-Iif-Reg-Trns-Consolidated"] = bundle.RegConsolidatedTrnsCount.ToString();
        Response.Headers["X-Iif-Merch-Trns-Source"] = bundle.MerchSourceTrnsCount.ToString();
        Response.Headers["X-Iif-Merch-Trns-Consolidated"] = bundle.MerchConsolidatedTrnsCount.ToString();

        return File(
            bundle.Zip.FileBytes,
            bundle.Zip.ContentType,
            $"TSIC-AdnReconciliation-{year}-{month:D2}.zip");
    }

    /// <summary>
    /// POST /api/adn-reconciliation/email-close?settlementMonth=N&amp;settlementYear=Y
    /// Manual trigger for the unattended month-end close the daily sweep runs on the 1st: pulls the
    /// month, builds the bundle, and EMAILS the .zip to support with the accounting-match verdict and
    /// IIF parity counts. Mirrors AdnSweepController.Run — the sweep's manual trigger — and is the only
    /// way to exercise the close off Production, since AdnSweepBackgroundService is IsLiveProduction()-
    /// gated and never fires on Staging. The send bypasses the sandbox gate (sendInDevelopment: true),
    /// so this really does transmit from Staging. Defaults to last month.
    /// </summary>
    [HttpPost("email-close")]
    public async Task<ActionResult<AdnReconciliationRunResult>> EmailClose(
        [FromQuery] int? settlementMonth,
        [FromQuery] int? settlementYear,
        CancellationToken cancellationToken)
    {
        var (month, year) = ResolveMonthYear(settlementMonth, settlementYear);
        var result = await _service.EmailMonthlyCloseAsync(month, year, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// GET /api/adn-reconciliation/reconcile?settlementMonth=N&amp;settlementYear=Y
    /// Custodial reconciliation for the month: does every ADN transaction have a matching accounting
    /// row (reg → Registration_Accounting, merch → StoreCartBatchAccounting)? Returns per-stack
    /// matched/unmatched counts, the unmatched list, and the paid/credit dollar totals staff compare
    /// against QuickBooks after importing the IIF. Reads Txs only — no ADN pull, no files. Defaults
    /// to last month if no params supplied.
    /// </summary>
    [HttpGet("reconcile")]
    public async Task<ActionResult<MonthEndReconciliationResult>> Reconcile(
        [FromQuery] int? settlementMonth,
        [FromQuery] int? settlementYear,
        CancellationToken cancellationToken)
    {
        var (month, year) = ResolveMonthYear(settlementMonth, settlementYear);
        var result = await _service.GetReconciliationAsync(month, year, cancellationToken);
        return Ok(result);
    }

    private static (int Month, int Year) ResolveMonthYear(int? month, int? year)
    {
        if (month.HasValue && year.HasValue) return (month.Value, year.Value);

        var lastMonth = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1).AddMonths(-1);
        return (lastMonth.Month, lastMonth.Year);
    }
}
