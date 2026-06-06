using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TSIC.API.Extensions;
using TSIC.API.Services.Reporting;
using TSIC.API.Services.Shared.Jobs;
using TSIC.Contracts.Dtos;

namespace TSIC.API.Controllers;

/// <summary>
/// Roster Table Designer — a director-built, configurable replacement for the wide-roster
/// Crystal family (Club Rosters, No-Medical, Coaches, WithClubRep, STEPS, Recruiting roster).
/// Picks/orders columns, groups + sorts, and renders a full-width table PDF in-process via
/// Syncfusion.Pdf (no RDL, no Crystal). jobId is derived from JWT claims, never from client.
/// </summary>
[ApiController]
[Route("api/roster-table")]
[Authorize(Policy = "AdminOnly")]
public class RosterTableController : ControllerBase
{
    private readonly IRosterTablePdfService _rosterTableService;
    private readonly IJobLookupService _jobLookupService;

    public RosterTableController(
        IRosterTablePdfService rosterTableService,
        IJobLookupService jobLookupService)
    {
        _rosterTableService = rosterTableService;
        _jobLookupService = jobLookupService;
    }

    /// <summary>
    /// The columns the Designer can place, with default width/align. Drives the frontend field
    /// picker so the available pool is never hard-coded client-side.
    /// </summary>
    [HttpGet("fields")]
    public ActionResult<IReadOnlyList<RosterTableFieldDto>> GetFields()
        => Ok(_rosterTableService.GetAvailableFields());

    /// <summary>
    /// Renders the roster-table PDF for the caller's current job from the given config.
    /// </summary>
    [HttpPost("generate")]
    public async Task<ActionResult> Generate(
        [FromBody] RosterTableRequestDto request,
        CancellationToken cancellationToken)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null)
        {
            return BadRequest(new { message = "Registration context required" });
        }

        var result = await _rosterTableService.GenerateAsync(request, jobId.Value, cancellationToken);
        return File(result.FileBytes, result.ContentType, result.FileName);
    }
}
