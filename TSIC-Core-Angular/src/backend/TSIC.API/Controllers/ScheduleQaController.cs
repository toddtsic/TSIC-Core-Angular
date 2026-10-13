using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TSIC.API.Extensions;
using TSIC.API.Services.Scheduling;
using TSIC.API.Services.Shared.Jobs;
using TSIC.API.Utilities;
using TSIC.Contracts.Dtos.Scheduling;
using TSIC.Contracts.Services;

namespace TSIC.API.Controllers;

/// <summary>
/// Schedule QA validation — standalone endpoint for the QA Results view.
/// Shared logic with Auto-Build post-validation via IScheduleQaService.
/// </summary>
[ApiController]
[Route("api/schedule-qa")]
[Authorize(Policy = "AdminOnly")]
public class ScheduleQaController : ControllerBase
{
    private readonly IScheduleQaService _qaService;
    private readonly IJobLookupService _jobLookupService;

    public ScheduleQaController(
        IScheduleQaService qaService,
        IJobLookupService jobLookupService)
    {
        _qaService = qaService;
        _jobLookupService = jobLookupService;
    }

    /// <summary>
    /// GET /api/schedule-qa/validate — Run all QA checks on the current job's schedule.
    /// </summary>
    [HttpGet("validate")]
    public async Task<ActionResult<AutoBuildQaResult>> Validate(CancellationToken ct)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null)
            return BadRequest(new { message = "Scheduling context required" });

        var result = await _qaService.RunValidationAsync(jobId.Value, ct);
        return Ok(result);
    }

    /// <summary>
    /// GET /api/schedule-qa/export-excel — Run QA and return multi-sheet Excel workbook.
    /// Replaces the legacy [utility].[Schedule_QA_Tourny] sproc Excel export.
    /// </summary>
    [HttpGet("export-excel")]
    public async Task<IActionResult> ExportExcel(CancellationToken ct)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null)
            return BadRequest(new { message = "Scheduling context required" });

        var qa = await _qaService.RunValidationAsync(jobId.Value, ct);
        var bytes = ScheduleQaWorkbook.Build(qa);

        return File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"ScheduleQA_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");
    }
}
