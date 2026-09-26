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
    private readonly IMasterScheduleService _masterScheduleService;
    private readonly IJobLookupService _jobLookupService;

    public TournamentParkingController(
        ITournamentParkingService service,
        IMasterScheduleService masterScheduleService,
        IJobLookupService jobLookupService)
    {
        _service = service;
        _masterScheduleService = masterScheduleService;
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

        var invalid = ValidateParameters(request);
        if (invalid != null)
            return BadRequest(new { message = invalid });

        var result = await _service.GetParkingReportAsync(jobId.Value, request, ct);
        return Ok(result);
    }

    /// <summary>
    /// Operations Master Schedule (.pdf) — the master grid plus CARS ON SITE per field complex,
    /// computed with the parameters currently set on the parking page.
    /// </summary>
    [HttpPost("operations-master-schedule-pdf")]
    public async Task<IActionResult> ExportOperationsMasterSchedulePdf(
        [FromBody] TournamentParkingRequest request,
        CancellationToken ct)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null)
            return BadRequest(new { message = "Scheduling context required" });

        var invalid = ValidateParameters(request);
        if (invalid != null)
            return BadRequest(new { message = invalid });

        var bytes = await _masterScheduleService.ExportOperationsPdfAsync(jobId.Value, request, ct);
        return File(bytes, "application/pdf", "Operations-Master-Schedule.pdf");
    }

    private static string? ValidateParameters(TournamentParkingRequest request)
    {
        if (request.ArrivalBufferMinutes < 0 || request.ArrivalBufferMinutes > 60)
            return "Arrival buffer must be 0-60 minutes";
        if (request.DepartureBufferMinutes < 0 || request.DepartureBufferMinutes > 60)
            return "Departure buffer must be 0-60 minutes";
        if (request.CarMultiplier < 0 || request.CarMultiplier > 30)
            return "Car multiplier must be 0-30";
        return null;
    }
}
