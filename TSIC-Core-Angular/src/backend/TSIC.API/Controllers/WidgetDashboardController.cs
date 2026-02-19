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
    private readonly IUserWidgetService _userWidgetService;
    private readonly IJobLookupService _jobLookupService;

    public WidgetDashboardController(
        IWidgetDashboardService dashboardService,
        IUserWidgetService userWidgetService,
        IJobLookupService jobLookupService)
    {
        _dashboardService = dashboardService;
        _userWidgetService = userWidgetService;
        _jobLookupService = jobLookupService;
    }

    /// <summary>
    /// Get the merged widget dashboard for the current user's job and role.
    /// Applies three merge layers: platform defaults, job overrides, user customizations.
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

        var regId = User.GetRegistrationId();

        var result = await _dashboardService.GetDashboardAsync(jobId.Value, roleName, regId, ct);
        return Ok(result);
    }

    /// <summary>
    /// Get live aggregate metrics (registrations, financials, scheduling) for the dashboard hero.
    /// </summary>
    [HttpGet("metrics")]
    public async Task<ActionResult<DashboardMetricsDto>> GetMetrics(CancellationToken ct)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null)
            return BadRequest(new { message = "Job context required" });

        var result = await _dashboardService.GetMetricsAsync(jobId.Value, ct);
        return Ok(result);
    }

    /// <summary>
    /// Get daily registration time-series data for the dashboard trend chart.
    /// Returns daily counts, revenue, and cumulative totals.
    /// </summary>
    [HttpGet("registration-trend")]
    public async Task<ActionResult<RegistrationTimeSeriesDto>> GetRegistrationTrend(CancellationToken ct)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null)
            return BadRequest(new { message = "Job context required" });

        var result = await _dashboardService.GetRegistrationTimeSeriesAsync(jobId.Value, ct);
        return Ok(result);
    }

    /// <summary>
    /// Get daily player registration time-series (Player role only).
    /// </summary>
    [HttpGet("player-trend")]
    public async Task<ActionResult<RegistrationTimeSeriesDto>> GetPlayerTrend(CancellationToken ct)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null)
            return BadRequest(new { message = "Job context required" });

        var result = await _dashboardService.GetPlayerTimeSeriesAsync(jobId.Value, ct);
        return Ok(result);
    }

    /// <summary>
    /// Get daily team registration time-series (ClubRep-paid teams).
    /// </summary>
    [HttpGet("team-trend")]
    public async Task<ActionResult<RegistrationTimeSeriesDto>> GetTeamTrend(CancellationToken ct)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null)
            return BadRequest(new { message = "Job context required" });

        var result = await _dashboardService.GetTeamTimeSeriesAsync(jobId.Value, ct);
        return Ok(result);
    }

    /// <summary>
    /// Get player and team counts per age group.
    /// </summary>
    [HttpGet("agegroup-distribution")]
    public async Task<ActionResult<AgegroupDistributionDto>> GetAgegroupDistribution(CancellationToken ct)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null)
            return BadRequest(new { message = "Job context required" });

        var result = await _dashboardService.GetAgegroupDistributionAsync(jobId.Value, ct);
        return Ok(result);
    }

    /// <summary>
    /// Get the primary event contact for the current job.
    /// Returns the earliest-registered administrator's name and email.
    /// </summary>
    [HttpGet("event-contact")]
    public async Task<ActionResult<EventContactDto>> GetEventContact(CancellationToken ct)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null)
            return BadRequest(new { message = "Job context required" });

        var result = await _dashboardService.GetEventContactAsync(jobId.Value, ct);
        if (result == null)
            return NotFound();

        return Ok(result);
    }

    /// <summary>
    /// Get year-over-year registration pace comparison across sibling jobs.
    /// </summary>
    [HttpGet("year-over-year")]
    public async Task<ActionResult<YearOverYearComparisonDto>> GetYearOverYear(CancellationToken ct)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null)
            return BadRequest(new { message = "Job context required" });

        var result = await _dashboardService.GetYearOverYearAsync(jobId.Value, ct);
        return Ok(result);
    }

    // ── User Widget Customization Endpoints ──

    /// <summary>
    /// Get the current user's widget customization delta.
    /// Returns an empty list if the user has no customizations.
    /// </summary>
    [HttpGet("my-widgets")]
    public async Task<ActionResult<List<UserWidgetEntryDto>>> GetMyWidgets(CancellationToken ct)
    {
        var regId = User.GetRegistrationId();
        if (regId == null)
            return BadRequest(new { message = "Registration context required" });

        var result = await _userWidgetService.GetUserWidgetsAsync(regId.Value, ct);
        return Ok(result);
    }

    /// <summary>
    /// Save the current user's widget customizations (replace-all pattern).
    /// Replaces existing delta with the provided entries.
    /// </summary>
    [HttpPut("my-widgets")]
    public async Task<IActionResult> SaveMyWidgets(
        [FromBody] SaveUserWidgetsRequest request, CancellationToken ct)
    {
        var regId = User.GetRegistrationId();
        if (regId == null)
            return BadRequest(new { message = "Registration context required" });

        await _userWidgetService.SaveUserWidgetsAsync(regId.Value, request, ct);
        return NoContent();
    }

    /// <summary>
    /// Reset the current user's widget customizations to platform defaults.
    /// Deletes all UserWidget entries for the current registration.
    /// </summary>
    [HttpDelete("my-widgets")]
    public async Task<IActionResult> ResetMyWidgets(CancellationToken ct)
    {
        var regId = User.GetRegistrationId();
        if (regId == null)
            return BadRequest(new { message = "Registration context required" });

        await _userWidgetService.ResetUserWidgetsAsync(regId.Value, ct);
        return NoContent();
    }

    /// <summary>
    /// Get all widgets available for the current user's role/jobType.
    /// Used by the widget library browser for customization UI.
    /// </summary>
    [HttpGet("available-widgets")]
    public async Task<ActionResult<List<AvailableWidgetDto>>> GetAvailableWidgets(CancellationToken ct)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null)
            return BadRequest(new { message = "Job context required" });

        var roleName = User.FindFirstValue(ClaimTypes.Role);
        if (string.IsNullOrEmpty(roleName))
            return Unauthorized();

        var regId = User.GetRegistrationId();
        if (regId == null)
            return BadRequest(new { message = "Registration context required" });

        var result = await _userWidgetService.GetAvailableWidgetsAsync(
            jobId.Value, roleName, regId.Value, ct);
        return Ok(result);
    }

    // ── Public (Anonymous) Endpoints ──

    /// <summary>
    /// Get the public widget dashboard for anonymous visitors.
    /// Returns widgets configured for the Anonymous role, grouped by workspace and category.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("public/{jobPath}")]
    public async Task<ActionResult<WidgetDashboardResponse>> GetPublicDashboard(
        string jobPath, CancellationToken ct)
    {
        var jobId = await _jobLookupService.GetJobIdByPathAsync(jobPath);
        if (jobId == null)
            return NotFound(new { message = "Job not found" });

        var result = await _dashboardService.GetDashboardAsync(jobId.Value, "Anonymous", ct: ct);
        return Ok(result);
    }

    /// <summary>
    /// Get the primary event contact for a public job (anonymous access).
    /// </summary>
    [AllowAnonymous]
    [HttpGet("public/{jobPath}/event-contact")]
    public async Task<ActionResult<EventContactDto>> GetPublicEventContact(
        string jobPath, CancellationToken ct)
    {
        var jobId = await _jobLookupService.GetJobIdByPathAsync(jobPath);
        if (jobId == null)
            return NotFound(new { message = "Job not found" });

        var result = await _dashboardService.GetEventContactAsync(jobId.Value, ct);
        if (result == null)
            return NotFound();

        return Ok(result);
    }
}
