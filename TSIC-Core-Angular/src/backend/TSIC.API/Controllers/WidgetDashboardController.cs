using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using TSIC.API.Extensions;
using TSIC.API.Services.Shared.Jobs;
using TSIC.Contracts.Dtos.Widgets;
using TSIC.Contracts.Services;

namespace TSIC.API.Controllers;

[ApiController]
[Route("api/widget-dashboard")]
[Authorize]
public class WidgetDashboardController : ControllerBase
{
    private readonly IWidgetDashboardService _dashboardService;
    private readonly IJobLookupService _jobLookupService;

    public WidgetDashboardController(
        IWidgetDashboardService dashboardService,
        IJobLookupService jobLookupService)
    {
        _dashboardService = dashboardService;
        _jobLookupService = jobLookupService;
    }

    /// <summary>
    /// Get the merged widget dashboard for the current user's job and role.
    /// Returns widgets grouped by section (health/action/insight) and category.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<WidgetDashboardResponse>> GetDashboard(CancellationToken ct)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null)
            return BadRequest(new { message = "Job context required" });

        var roleName = User.FindFirstValue(ClaimTypes.Role);
        if (string.IsNullOrEmpty(roleName))
            return Unauthorized();

        var result = await _dashboardService.GetDashboardAsync(jobId.Value, roleName, ct);
        return Ok(result);
    }

    /// <summary>
    /// Get the public widget dashboard for anonymous visitors.
    /// Returns widgets configured for the Anonymous role, grouped by section and category.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("public/{jobPath}")]
    public async Task<ActionResult<WidgetDashboardResponse>> GetPublicDashboard(
        string jobPath, CancellationToken ct)
    {
        var jobId = await _jobLookupService.GetJobIdByPathAsync(jobPath);
        if (jobId == null)
            return NotFound(new { message = "Job not found" });

        var result = await _dashboardService.GetDashboardAsync(jobId.Value, "Anonymous", ct);
        return Ok(result);
    }
}
