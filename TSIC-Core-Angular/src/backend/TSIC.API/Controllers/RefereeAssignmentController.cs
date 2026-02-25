using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TSIC.API.Extensions;
using TSIC.Contracts.Dtos.Referees;
using TSIC.API.Services.Shared.Jobs;
using TSIC.Contracts.Services;

namespace TSIC.API.Controllers;

/// <summary>
/// Referee assignment management — assign refs to games, copy, import, and calendar.
/// </summary>
[ApiController]
[Route("api/referee-assignment")]
[Authorize(Policy = "RefAdmin")]
public class RefereeAssignmentController : ControllerBase
{
    private readonly IRefAssignmentService _service;
    private readonly IJobLookupService _jobLookupService;

    public RefereeAssignmentController(
        IRefAssignmentService service,
        IJobLookupService jobLookupService)
    {
        _service = service;
        _jobLookupService = jobLookupService;
    }

    // ── Queries ──

    /// <summary>Get all referees for the current job.</summary>
    [HttpGet("referees")]
    public async Task<ActionResult<List<RefereeSummaryDto>>> GetReferees(CancellationToken ct)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null) return BadRequest(new { message = "Unable to resolve job from token." });

        var result = await _service.GetRefereesAsync(jobId.Value, ct);
        return Ok(result);
    }

    /// <summary>Get filter options for the schedule search grid.</summary>
    [HttpGet("filter-options")]
    public async Task<ActionResult<RefScheduleFilterOptionsDto>> GetFilterOptions(CancellationToken ct)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null) return BadRequest(new { message = "Unable to resolve job from token." });

        var result = await _service.GetFilterOptionsAsync(jobId.Value, ct);
        return Ok(result);
    }

    /// <summary>Search games matching filter criteria with assigned ref IDs.</summary>
    [HttpPost("search")]
    public async Task<ActionResult<List<RefScheduleGameDto>>> SearchSchedule(
        [FromBody] RefScheduleSearchRequest request, CancellationToken ct)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null) return BadRequest(new { message = "Unable to resolve job from token." });

        var result = await _service.SearchScheduleAsync(jobId.Value, request, ct);
        return Ok(result);
    }

    /// <summary>Get all ref-to-game assignment pairs for the current job.</summary>
    [HttpGet("assignments")]
    public async Task<ActionResult<List<GameRefAssignmentDto>>> GetAllAssignments(CancellationToken ct)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null) return BadRequest(new { message = "Unable to resolve job from token." });

        var result = await _service.GetAllAssignmentsAsync(jobId.Value, ct);
        return Ok(result);
    }

    /// <summary>Get detailed ref assignments for a single game.</summary>
    [HttpGet("game-details/{gid:int}")]
    public async Task<ActionResult<List<RefGameDetailsDto>>> GetGameDetails(int gid, CancellationToken ct)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null) return BadRequest(new { message = "Unable to resolve job from token." });

        var result = await _service.GetGameRefDetailsAsync(gid, jobId.Value, ct);
        return Ok(result);
    }

    /// <summary>Get all referee calendar events for the current job.</summary>
    [HttpGet("calendar-events")]
    public async Task<ActionResult<List<RefereeCalendarEventDto>>> GetCalendarEvents(CancellationToken ct)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null) return BadRequest(new { message = "Unable to resolve job from token." });

        var result = await _service.GetCalendarEventsAsync(jobId.Value, ct);
        return Ok(result);
    }

    // ── Commands ──

    /// <summary>Replace all ref assignments for a game.</summary>
    [HttpPost("assign")]
    public async Task<IActionResult> AssignRefs([FromBody] AssignRefsRequest request, CancellationToken ct)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null) return BadRequest(new { message = "Unable to resolve job from token." });

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        await _service.AssignRefsToGameAsync(request, userId, ct);
        return Ok();
    }

    /// <summary>Copy ref assignments from one game to adjacent timeslots.</summary>
    [HttpPost("copy")]
    public async Task<ActionResult<List<int>>> CopyGameRefs([FromBody] CopyGameRefsRequest request, CancellationToken ct)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null) return BadRequest(new { message = "Unable to resolve job from token." });

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        var affectedGids = await _service.CopyGameRefsAsync(jobId.Value, request, userId, ct);
        return Ok(affectedGids);
    }

    /// <summary>Import referees from a CSV file.</summary>
    [HttpPost("import")]
    public async Task<ActionResult<ImportRefereesResult>> ImportReferees(IFormFile file, CancellationToken ct)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null) return BadRequest(new { message = "Unable to resolve job from token." });

        if (file == null || file.Length == 0)
            return BadRequest(new { message = "No file uploaded." });

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        await using var stream = file.OpenReadStream();
        var result = await _service.ImportRefereesAsync(jobId.Value, stream, userId, ct);
        return Ok(result);
    }

    /// <summary>Create N test referee registrations for development.</summary>
    [HttpPost("seed-test")]
    public async Task<ActionResult<List<RefereeSummaryDto>>> SeedTestReferees(
        [FromBody] SeedTestRefsRequest request, CancellationToken ct)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null) return BadRequest(new { message = "Unable to resolve job from token." });

        if (request.Count < 1 || request.Count > 50)
            return BadRequest(new { message = "Count must be between 1 and 50." });

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        var result = await _service.SeedTestRefereesAsync(jobId.Value, request.Count, userId, ct);
        return Ok(result);
    }

    /// <summary>Delete ALL referee assignments AND registrations for the current job.</summary>
    [HttpDelete("purge")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> PurgeAll(CancellationToken ct)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null) return BadRequest(new { message = "Unable to resolve job from token." });

        await _service.DeleteAllAsync(jobId.Value, ct);
        return Ok();
    }

}
