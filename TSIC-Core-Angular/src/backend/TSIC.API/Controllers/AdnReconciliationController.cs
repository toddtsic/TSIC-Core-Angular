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
    /// Runs the legacy "Get Reconciliation Records" flow:
    /// imports last month's settled ADN transactions into Txs, then returns the
    /// Excel reconciliation report. Defaults to last month if no params supplied.
    /// Import counts surface in response headers (X-Imported-Count, X-Skipped-Duplicates,
    /// X-Batches-Pulled, X-Transactions-Pulled) for UI feedback + log inspection.
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

        return File(
            result.Excel.FileBytes,
            result.Excel.ContentType,
            $"TSIC-AdnReconciliation-{year}-{month:D2}.xlsx");
    }

    private static (int Month, int Year) ResolveMonthYear(int? month, int? year)
    {
        if (month.HasValue && year.HasValue) return (month.Value, year.Value);

        var lastMonth = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1).AddMonths(-1);
        return (lastMonth.Month, lastMonth.Year);
    }
}
