using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TSIC.Contracts.Dtos.Logs;
using TSIC.Contracts.Repositories;

namespace TSIC.API.Controllers;

[ApiController]
[Route("api/admin/logs")]
[Authorize(Policy = "SuperUserOnly")]
public class LogsController : ControllerBase
{
    private readonly ILogRepository _logRepo;

    public LogsController(ILogRepository logRepo) => _logRepo = logRepo;

    [HttpGet]
    public async Task<IActionResult> Query([FromQuery] LogQueryParams query, CancellationToken ct)
    {
        var (items, total) = await _logRepo.QueryAsync(query, ct);
        return Ok(new { items, totalCount = total, page = query.Page, pageSize = query.PageSize });
    }

    [HttpGet("stats")]
    public async Task<IActionResult> Stats(
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        CancellationToken ct)
    {
        var start = from ?? DateTimeOffset.UtcNow.AddDays(-1);
        var end = to ?? DateTimeOffset.UtcNow;
        return Ok(await _logRepo.GetStatsAsync(start, end, ct));
    }

    [HttpDelete("purge")]
    public async Task<IActionResult> Purge([FromQuery] int daysToKeep = 30, CancellationToken ct = default)
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-daysToKeep);
        var deleted = await _logRepo.PurgeBeforeAsync(cutoff, ct);
        return Ok(new { deletedCount = deleted, cutoffDate = cutoff });
    }
}
