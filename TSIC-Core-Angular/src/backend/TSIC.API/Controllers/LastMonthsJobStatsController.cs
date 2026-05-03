using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TSIC.Contracts.Dtos.LastMonthsJobStats;
using TSIC.Contracts.Services;

namespace TSIC.API.Controllers;

[ApiController]
[Route("api/last-months-job-stats")]
[Authorize(Roles = "Superuser")]
public class LastMonthsJobStatsController : ControllerBase
{
    private readonly ILastMonthsJobStatsService _service;

    public LastMonthsJobStatsController(ILastMonthsJobStatsService service)
    {
        _service = service;
    }

    /// <summary>
    /// Returns last calendar month's MonthlyJobStats rows for cross-customer SU review/edit.
    /// Mirrors legacy Home/LastMonthsJobStats_Get.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<LastMonthsJobStatRowDto>>> Get(CancellationToken cancellationToken)
    {
        var rows = await _service.GetLastMonthsAsync(cancellationToken);
        return Ok(rows);
    }

    /// <summary>
    /// Inline-edit save: updates the 6 count fields on a single MonthlyJobStats row by Aid.
    /// </summary>
    [HttpPut("{aid:int}")]
    public async Task<IActionResult> Update(
        int aid,
        [FromBody] UpdateLastMonthsJobStatRequest request,
        CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        var ok = await _service.UpdateCountsAsync(aid, request, userId, cancellationToken);
        if (!ok)
        {
            return NotFound(new { message = $"MonthlyJobStats row with aid {aid} not found." });
        }
        return NoContent();
    }
}
