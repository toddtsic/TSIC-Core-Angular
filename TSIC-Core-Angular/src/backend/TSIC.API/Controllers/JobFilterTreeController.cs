using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TSIC.API.Extensions;
using TSIC.API.Services.Shared.Jobs;
using TSIC.Contracts.Dtos.Scheduling;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;

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
    private readonly IViewScheduleService _viewScheduleService;

    public JobFilterTreeController(
        IJobFilterTreeRepository repo,
        IJobLookupService jobLookupService,
        IViewScheduleService viewScheduleService)
    {
        _repo = repo;
        _jobLookupService = jobLookupService;
        _viewScheduleService = viewScheduleService;
    }

    /// <summary>
    /// Returns both CADT and LADT trees for the job, with team-level metadata
    /// (IsScheduled, HasClubRep, PlayerCount) and agegroup-level flags
    /// (IsWaitlist, IsDropped). Supports the same dual auth as view-schedule:
    /// jobPath query param (wins when given) OR authenticated user (regId claim).
    /// IsScheduled is schedule data: it reads false for a caller who may not see the schedule.
    /// </summary>
    [AllowAnonymous]
    [HttpGet]
    public async Task<ActionResult<JobFilterTreeDto>> Get(
        [FromQuery] string? jobPath, CancellationToken ct)
    {
        Guid? jobId;

        // jobPath first, matching ViewScheduleController.ResolveContext: the tree must be for the
        // event the page shows, not whichever event the caller happens to be logged in to.
        if (!string.IsNullOrEmpty(jobPath))
        {
            jobId = await _jobLookupService.GetJobIdByPathAsync(jobPath);
            if (jobId == null)
                return NotFound(new { message = "Job not found" });
        }
        else if (User.GetRegistrationId().HasValue)
        {
            jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
            if (jobId == null)
                return BadRequest(new { message = "Job context required" });
        }
        else
        {
            return Unauthorized(new { message = "Authentication or jobPath required" });
        }

        var tree = await _repo.GetForJobAsync(jobId.Value, ct);
        if (!await HttpContext.CanViewScheduleAsync(jobId.Value, _jobLookupService, _viewScheduleService, ct))
            tree = WithoutScheduleFlags(tree);
        return Ok(tree);
    }

    /// <summary>The same tree with every team's IsScheduled cleared — reveals nothing about the schedule.</summary>
    internal static JobFilterTreeDto WithoutScheduleFlags(JobFilterTreeDto tree) => tree with
    {
        Cadt = [.. tree.Cadt.Select(club => club with
        {
            Agegroups = [.. club.Agegroups.Select(ag => ag with
            {
                Divisions = [.. ag.Divisions.Select(div => div with
                {
                    Teams = [.. div.Teams.Select(t => t with { IsScheduled = false })]
                })]
            })]
        })],
        Ladt = [.. tree.Ladt.Select(ag => ag with
        {
            Divisions = [.. ag.Divisions.Select(div => div with
            {
                Teams = [.. div.Teams.Select(t => t with { IsScheduled = false })]
            })]
        })]
    };
}
