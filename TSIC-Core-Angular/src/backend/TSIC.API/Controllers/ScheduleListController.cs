using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TSIC.API.Extensions;
using TSIC.API.Services.Reporting;
using TSIC.API.Services.Shared.Jobs;
using TSIC.Contracts.Dtos;

namespace TSIC.API.Controllers;

/// <summary>
/// Schedule List Designer — a director-built, configurable replacement for the canned
/// Schedule_ExportExcel report family (ScheduleMaster, ScheduleByDay, FieldUtilization*,
/// Schedule_Export) plus the Score_Input blank-score sheet. Picks/orders columns, groups +
/// sorts the games, and chooses how scores render, then renders the PDF in-process via
/// Syncfusion.Pdf (no RDL, no Crystal). jobId is derived from JWT claims, never from client.
/// </summary>
[ApiController]
[Route("api/schedule-list")]
[Authorize(Policy = "AdminOnly")]
public class ScheduleListController : ControllerBase
{
    private readonly IScheduleListReportService _scheduleListService;
    private readonly IJobLookupService _jobLookupService;

    public ScheduleListController(
        IScheduleListReportService scheduleListService,
        IJobLookupService jobLookupService)
    {
        _scheduleListService = scheduleListService;
        _jobLookupService = jobLookupService;
    }

    /// <summary>
    /// The columns the Designer can place, with default width/align. Drives the frontend
    /// field picker so the available pool is never hard-coded client-side.
    /// </summary>
    [HttpGet("fields")]
    public ActionResult<IReadOnlyList<ScheduleListFieldDto>> GetFields()
        => Ok(_scheduleListService.GetAvailableFields());

    /// <summary>
    /// Renders the schedule-list PDF for the caller's current job from the given config.
    /// </summary>
    [HttpPost("generate")]
    public async Task<ActionResult> Generate(
        [FromBody] ScheduleListRequestDto request,
        CancellationToken cancellationToken)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null)
        {
            return BadRequest(new { message = "Registration context required" });
        }

        var result = await _scheduleListService.GenerateAsync(request, jobId.Value, cancellationToken);
        return File(result.FileBytes, result.ContentType, result.FileName);
    }
}
