using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TSIC.API.Extensions;
using TSIC.API.Services.Shared.Jobs;
using TSIC.Contracts.Dtos.Stp;
using TSIC.Contracts.Services;

namespace TSIC.API.Controllers;

/// <summary>
/// Stay-to-Play admin. Replaces the legacy STP admin area (Controllers/STP/Admin),
/// of which only the club rep screen carried real function — the dashboard was a stub
/// and STPAdminAdd is covered by the Administrators page.
///
/// Job scope comes from the caller's registration, never from a route parameter: an
/// STPAdmin holds one registration per event, so the token already fixes which event's
/// club reps they can see.
/// </summary>
[ApiController]
[Route("api/stp")]
[Authorize(Policy = "CanViewStpClubReps")]
public class StpController : ControllerBase
{
    private readonly IStpService _service;
    private readonly IJobLookupService _jobLookupService;

    public StpController(IStpService service, IJobLookupService jobLookupService)
    {
        _service = service;
        _jobLookupService = jobLookupService;
    }

    /// <summary>
    /// GET /api/stp/club-reps — active club reps on the current job, with the team
    /// counts a housing vendor sizes room blocks from. Biggest travelling clubs first.
    /// </summary>
    [HttpGet("club-reps")]
    public async Task<ActionResult<List<StpClubRepDto>>> GetClubReps(CancellationToken ct)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId is null)
            return BadRequest(new { message = "Registration context required." });

        var clubReps = await _service.GetClubRepsAsync(jobId.Value, ct);

        // Null = the director has not consented to share this event's data (BEnableSTP off).
        // The message is deliberately neutral and says nothing about who decided or why:
        // an STPAdmin here is a third-party housing vendor holding a JWT minted before the
        // flag was flipped, and telling them a director switched them off hands them a
        // person to pressure over a data-sharing decision that is not theirs. The director's
        // own screen reads the flag off the job pulse and explains it properly.
        if (clubReps is null)
            return StatusCode(StatusCodes.Status403Forbidden,
                new { message = "Stay-to-Play is not enabled for this event." });

        return Ok(clubReps);
    }
}
