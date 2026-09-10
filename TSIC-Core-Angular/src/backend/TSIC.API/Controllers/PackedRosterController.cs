using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TSIC.API.Extensions;
using TSIC.API.Services.Reporting;
using TSIC.API.Services.Shared.Jobs;
using TSIC.Contracts.Dtos;

namespace TSIC.API.Controllers;

/// <summary>
/// PackedRoster Designer — a director-built, configurable replacement for the canned
/// "Tournament Roster Packed" Bold RDLs. Picks/orders fields + toggles card chrome, then
/// renders the PDF in-process via Syncfusion.Pdf (no RDL, no Bold). jobId is derived from
/// JWT claims, never from client params.
/// </summary>
[ApiController]
[Route("api/packed-roster")]
[Authorize(Policy = "AdminOnly")]
public class PackedRosterController : ControllerBase
{
    private readonly IPackedRosterPdfService _packedRosterService;
    private readonly IJobLookupService _jobLookupService;
    private readonly IReportingService _reportingService;

    public PackedRosterController(
        IPackedRosterPdfService packedRosterService,
        IJobLookupService jobLookupService,
        IReportingService reportingService)
    {
        _packedRosterService = packedRosterService;
        _jobLookupService = jobLookupService;
        _reportingService = reportingService;
    }

    /// <summary>
    /// The player-row columns the Designer can place, with default width/align. Drives the
    /// frontend field picker so the available pool is never hard-coded client-side.
    /// </summary>
    [HttpGet("fields")]
    public ActionResult<IReadOnlyList<PackedRosterFieldDto>> GetFields()
        => Ok(_packedRosterService.GetAvailableFields());

    /// <summary>
    /// Renders the packed-roster PDF for the caller's current job from the given config.
    /// </summary>
    [HttpPost("generate")]
    public async Task<ActionResult> Generate(
        [FromBody] PackedRosterRequestDto request,
        CancellationToken cancellationToken)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null)
        {
            return BadRequest(new { message = "Registration context required" });
        }

        var result = await _packedRosterService.GenerateAsync(request, jobId.Value, cancellationToken);
        // Every report run leaves a who/which/when row (Jobs.JobReportExportHistory); the key is
        // the library ReportKey for this designer.
        await _reportingService.RecordExportHistoryAsync(
            User.GetRegistrationId(), null, "reporting/packed-roster-designer", cancellationToken);
        return File(result.FileBytes, result.ContentType, result.FileName);
    }

    /// <summary>
    /// Renders the recruiter report (player-as-card) PDF for the caller's current job —
    /// reproduces the legacy LFTC Recruiters report off the same EF roster query.
    /// </summary>
    [HttpGet("recruiter")]
    public async Task<ActionResult> GenerateRecruiter(CancellationToken cancellationToken)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null)
        {
            return BadRequest(new { message = "Registration context required" });
        }

        var result = await _packedRosterService.GenerateRecruiterAsync(jobId.Value, cancellationToken);
        await _reportingService.RecordExportHistoryAsync(
            User.GetRegistrationId(), null, "reporting/packed-roster-designer/recruiter", cancellationToken);
        return File(result.FileBytes, result.ContentType, result.FileName);
    }
}
