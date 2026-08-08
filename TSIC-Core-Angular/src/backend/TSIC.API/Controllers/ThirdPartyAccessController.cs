using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TSIC.API.Extensions;
using TSIC.API.Services.Admin;
using TSIC.API.Services.Shared.Jobs;
using TSIC.Contracts.Dtos.ThirdPartyAccess;

namespace TSIC.API.Controllers;

/// <summary>
/// "3rd Party Data Access" console: a customer's own administration of which vendor
/// login (ApiAuthorized) may pull its rosters/schedule export. CanCrossCustomerJobs
/// (SU + SuperDirector) — a cross-job screen belongs to cross-job roles; per-job
/// Directors are deliberately excluded. All context derives from JWT claims; the
/// service enforces the same-customer wall on every target job.
/// </summary>
[ApiController]
[Route("api/third-party-access")]
[Authorize(Policy = "CanCrossCustomerJobs")]
public class ThirdPartyAccessController : ControllerBase
{
    private readonly IThirdPartyAccessService _thirdPartyAccessService;
    private readonly IJobLookupService _jobLookupService;

    public ThirdPartyAccessController(
        IThirdPartyAccessService thirdPartyAccessService,
        IJobLookupService jobLookupService)
    {
        _thirdPartyAccessService = thirdPartyAccessService;
        _jobLookupService = jobLookupService;
    }

    /// <summary>Customer overview: vendor history + open-window jobs with their assignments.</summary>
    [HttpGet("overview")]
    public async Task<ActionResult<ThirdPartyAccessOverviewDto>> GetOverview(CancellationToken ct)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null)
        {
            return BadRequest(new { message = "Registration context required" });
        }

        var overview = await _thirdPartyAccessService.GetOverviewAsync(jobId.Value, ct);
        return Ok(overview);
    }

    /// <summary>Grant (create-or-reactivate) the vendor login on a job. Returns the refreshed overview.</summary>
    [HttpPost("jobs/{jobId:guid}/grant")]
    public async Task<ActionResult<ThirdPartyAccessOverviewDto>> Grant(
        Guid jobId, [FromBody] GrantThirdPartyAccessRequest request, CancellationToken ct)
    {
        var callerJobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (callerJobId == null)
        {
            return BadRequest(new { message = "Registration context required" });
        }

        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

        try
        {
            var overview = await _thirdPartyAccessService.GrantAsync(
                callerJobId.Value, jobId, request.UserId, currentUserId, ct);
            return Ok(overview);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    /// <summary>Disable the job's vendor login (bActive = 0). Returns the refreshed overview.</summary>
    [HttpPost("jobs/{jobId:guid}/disable")]
    public async Task<ActionResult<ThirdPartyAccessOverviewDto>> Disable(Guid jobId, CancellationToken ct)
    {
        var callerJobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (callerJobId == null)
        {
            return BadRequest(new { message = "Registration context required" });
        }

        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

        try
        {
            var overview = await _thirdPartyAccessService.DisableAsync(callerJobId.Value, jobId, currentUserId, ct);
            return Ok(overview);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }
}
