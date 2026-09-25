using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TSIC.API.Extensions;
using TSIC.API.Services.Shared.Jobs;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Repositories;

namespace TSIC.API.Controllers;

/// <summary>
/// Backs the Scheduling Checklist's "Send Club Coaches Preview Email" tool.
///
/// The letter itself goes out through the existing batch-email endpoint — this controller
/// only answers "who gets it". That audience is a FIXED POPULATION, not something the
/// director assembles by hand in Search Registrations: active club reps on this event who
/// hold at least one team in the built schedule.
///
/// Read-only and scoped to the caller's own job (from the JWT, no cross-job picker), so it
/// cannot be used to enumerate another event's reps.
/// </summary>
[ApiController]
[Route("api/schedule-review")]
[Authorize(Policy = "AdminOnly")]
public class ScheduleReviewController : ControllerBase
{
    private readonly IRegistrationRepository _registrations;
    private readonly IJobLookupService _jobLookupService;

    public ScheduleReviewController(
        IRegistrationRepository registrations,
        IJobLookupService jobLookupService)
    {
        _registrations = registrations;
        _jobLookupService = jobLookupService;
    }

    /// <summary>
    /// Club reps who should review this event's schedule, with the club and scheduled-team
    /// count that explains why each one is on the list. Empty when the schedule has no games.
    /// </summary>
    [HttpGet("recipients")]
    [ProducesResponseType(typeof(IEnumerable<ScheduleReviewRecipientDto>), 200)]
    public async Task<ActionResult<List<ScheduleReviewRecipientDto>>> GetRecipients(CancellationToken ct)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId is null)
            return NotFound(new { message = "Job not found for current user." });

        return Ok(await _registrations.GetScheduleReviewRecipientsAsync(jobId.Value, ct));
    }
}
