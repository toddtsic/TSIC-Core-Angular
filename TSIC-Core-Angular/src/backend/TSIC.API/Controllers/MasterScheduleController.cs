using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using TSIC.API.Extensions;
using TSIC.API.Services.Shared.Jobs;
using TSIC.Contracts.Dtos.Scheduling;
using TSIC.Contracts.Services;

namespace TSIC.API.Controllers;

/// <summary>
/// Master Schedule — read-only date×field pivot grid with Excel export.
/// Loads ALL games for the job (no filtering). Directors print day sheets.
/// </summary>
[ApiController]
[Route("api/master-schedule")]
[Authorize]
public class MasterScheduleController : ControllerBase
{
    private readonly IMasterScheduleService _service;
    private readonly IJobLookupService _jobLookupService;

    public MasterScheduleController(
        IMasterScheduleService service,
        IJobLookupService jobLookupService)
    {
        _service = service;
        _jobLookupService = jobLookupService;
    }

    /// <summary>GET /api/master-schedule — Full pivot grid (all days).</summary>
    [HttpGet]
    public async Task<ActionResult<MasterScheduleResponse>> GetMasterSchedule(CancellationToken ct)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null)
            return BadRequest(new { message = "Job context required" });

        var isAdmin = IsAdmin();
        var result = await _service.GetMasterScheduleAsync(jobId.Value, isAdmin, ct);
        return Ok(result);
    }

    /// <summary>POST /api/master-schedule/export — Excel download (.xlsx).</summary>
    [HttpPost("export")]
    public async Task<IActionResult> ExportExcel(
        [FromBody] MasterScheduleExportRequest request, CancellationToken ct)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null)
            return BadRequest(new { message = "Job context required" });

        // Server enforces admin-only referee inclusion
        var includeReferees = request.IncludeReferees && IsAdmin();

        var bytes = await _service.ExportExcelAsync(jobId.Value, includeReferees, request.DayIndex, ct);

        var fileName = request.DayIndex.HasValue
            ? $"MasterSchedule-Day{request.DayIndex.Value + 1}.xlsx"
            : "MasterSchedule-Full.xlsx";

        return File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            fileName);
    }

    private bool IsAdmin()
    {
        var roleName = User.FindFirstValue(ClaimTypes.Role)
            ?? User.FindFirstValue("role");
        return roleName is "Superuser" or "Director" or "SuperDirector" or "Scorer";
    }
}
