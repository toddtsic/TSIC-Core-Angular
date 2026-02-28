using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TSIC.API.Extensions;
using TSIC.API.Services.Admin;
using TSIC.API.Services.Shared.Jobs;
using TSIC.Contracts.Dtos.CustomerJobRevenue;

namespace TSIC.API.Controllers;

[ApiController]
[Route("api/customer-job-revenue")]
[Authorize(Policy = "SuperUserOnly")]
public class CustomerJobRevenueController : ControllerBase
{
    private readonly ICustomerJobRevenueService _revenueService;
    private readonly IJobLookupService _jobLookupService;

    public CustomerJobRevenueController(
        ICustomerJobRevenueService revenueService,
        IJobLookupService jobLookupService)
    {
        _revenueService = revenueService;
        _jobLookupService = jobLookupService;
    }

    /// <summary>
    /// Returns all revenue data for the given date range: rollups, monthly counts, admin fees,
    /// CC records, check records, and available job names.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<JobRevenueDataDto>> GetRevenueData(
        [FromQuery] DateTime startDate,
        [FromQuery] DateTime endDate,
        [FromQuery] List<string> jobNames,
        CancellationToken ct)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null)
        {
            return BadRequest(new { message = "Registration context required" });
        }

        var data = await _revenueService.GetRevenueDataAsync(
            jobId.Value, startDate, endDate, jobNames ?? [], ct);

        return Ok(data);
    }

    /// <summary>
    /// Inline-edit a single MonthlyJobStats row (Player/Team Counts grid).
    /// </summary>
    [HttpPut("monthly-counts/{aid:int}")]
    public async Task<IActionResult> UpdateMonthlyCount(
        int aid,
        [FromBody] UpdateMonthlyCountRequest request,
        CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

        await _revenueService.UpdateMonthlyCountAsync(aid, request, userId, ct);

        return NoContent();
    }
}
