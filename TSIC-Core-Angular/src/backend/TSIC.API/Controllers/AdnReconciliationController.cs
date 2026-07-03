using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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

    private static (int Month, int Year) ResolveMonthYear(int? month, int? year)
    {
        if (month.HasValue && year.HasValue) return (month.Value, year.Value);

        var lastMonth = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1).AddMonths(-1);
        return (lastMonth.Month, lastMonth.Year);
    }
}
