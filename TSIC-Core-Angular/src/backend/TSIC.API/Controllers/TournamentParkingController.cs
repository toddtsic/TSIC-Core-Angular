using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TSIC.API.Extensions;
using TSIC.API.Services.Shared.Jobs;
using TSIC.Contracts.Dtos.Scheduling;
using TSIC.Contracts.Services;

namespace TSIC.API.Controllers;

/// <summary>
/// Tournament parking / teams-on-site reporting.
/// Shows estimated teams and cars at each field complex over time.
/// </summary>
[ApiController]
[Route("api/tournament-parking")]
[Authorize(Policy = "AdminOnly")]
public class TournamentParkingController : ControllerBase
{
    private readonly ITournamentParkingService _service;
    private readonly IJobLookupService _jobLookupService;

    public TournamentParkingController(
        ITournamentParkingService service,
        IJobLookupService jobLookupService)
    {
        _service = service;
        _jobLookupService = jobLookupService;
    }

    /// <summary>
    /// Generate a parking report for the current job.
    /// </summary>
    [HttpPost("report")]
    [ProducesResponseType<TournamentParkingResponse>(200)]
    public async Task<IActionResult> GetReport(
        [FromBody] TournamentParkingRequest request,
        CancellationToken ct)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null)
            return BadRequest(new { message = "Scheduling context required" });

        // Validate parameter ranges
        if (request.ArrivalBufferMinutes < 0 || request.ArrivalBufferMinutes > 60)
            return BadRequest(new { message = "Arrival buffer must be 0-60 minutes" });
        if (request.DepartureBufferMinutes < 0 || request.DepartureBufferMinutes > 60)
            return BadRequest(new { message = "Departure buffer must be 0-60 minutes" });
        if (request.CarMultiplier < 0 || request.CarMultiplier > 30)
            return BadRequest(new { message = "Car multiplier must be 0-30" });

        var result = await _service.GetParkingReportAsync(jobId.Value, request, ct);
        return Ok(result);
    }
}
