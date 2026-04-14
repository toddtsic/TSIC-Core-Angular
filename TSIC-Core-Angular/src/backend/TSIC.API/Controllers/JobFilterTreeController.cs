using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TSIC.API.Extensions;
using TSIC.API.Services.Shared.Jobs;
using TSIC.Contracts.Dtos.Scheduling;
using TSIC.Contracts.Repositories;

namespace TSIC.API.Controllers;

/// <summary>
/// Single source of truth for the unified CADT/LADT filter tree consumed by
/// view-schedule, rescheduler, search-teams, search-registrations, and public-rosters.
/// One endpoint, one query, both trees with rich metadata flags. Per-surface filtering
/// (require scheduled, require club rep, exclude waitlist/dropped) happens client-side
/// in the shared filter component.
/// </summary>
[ApiController]
[Route("api/job-filter-tree")]
public class JobFilterTreeController : ControllerBase
{
    private readonly IJobFilterTreeRepository _repo;
    private readonly IJobLookupService _jobLookupService;

    public JobFilterTreeController(
        IJobFilterTreeRepository repo,
        IJobLookupService jobLookupService)
    {
        _repo = repo;
        _jobLookupService = jobLookupService;
    }

    /// <summary>
    /// Returns both CADT and LADT trees for the job, with team-level metadata
    /// (IsScheduled, HasClubRep, PlayerCount) and agegroup-level flags
    /// (IsWaitlist, IsDropped). Supports the same dual auth as view-schedule:
    /// authenticated user (regId claim) OR public access via jobPath query param.
    /// </summary>
    [AllowAnonymous]
    [HttpGet]
    public async Task<ActionResult<JobFilterTreeDto>> Get(
        [FromQuery] string? jobPath, CancellationToken ct)
    {
        Guid? jobId;

        var regId = User.GetRegistrationId();
        if (regId.HasValue)
        {
            jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
            if (jobId == null)
                return BadRequest(new { message = "Job context required" });
        }
        else if (!string.IsNullOrEmpty(jobPath))
        {
            jobId = await _jobLookupService.GetJobIdByPathAsync(jobPath);
            if (jobId == null)
                return NotFound(new { message = "Job not found" });
        }
        else
        {
            return Unauthorized(new { message = "Authentication or jobPath required" });
        }

        var tree = await _repo.GetForJobAsync(jobId.Value, ct);
        return Ok(tree);
    }
}
