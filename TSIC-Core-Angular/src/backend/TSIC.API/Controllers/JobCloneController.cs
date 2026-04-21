using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TSIC.Contracts.Dtos.JobClone;
using TSIC.Contracts.Services;

namespace TSIC.API.Controllers;

/// <summary>
/// SuperUser-only endpoint for cloning a job (tournament/event) to create a new season.
/// </summary>
[ApiController]
[Route("api/job-clone")]
[Authorize(Policy = "SuperUserOnly")]
public class JobCloneController : ControllerBase
{
    private readonly IJobCloneService _cloneService;

    public JobCloneController(IJobCloneService cloneService)
    {
        _cloneService = cloneService;
    }

    /// <summary>
    /// List all jobs available as clone sources.
    /// </summary>
    [HttpGet("sources")]
    public async Task<ActionResult<List<JobCloneSourceDto>>> GetSources(CancellationToken ct)
    {
        var result = await _cloneService.GetCloneableJobsAsync(ct);
        return Ok(result);
    }

    /// <summary>
    /// Clone a source job into a new job with the given parameters.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<JobCloneResponse>> CloneJob(
        [FromBody] JobCloneRequest request,
        CancellationToken ct)
    {
        var superUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new UnauthorizedAccessException("User ID not found in token.");

        try
        {
            // authorCustomerId=null → skip same-customer guard (current endpoint is SuperUser-only
            // and target always inherits source.CustomerId, so cross-customer is impossible by construction).
            // Phase D will pass the author's customerId when non-SuperUser roles can hit this endpoint.
            var result = await _cloneService.CloneJobAsync(request, superUserId, authorCustomerId: null, ct);
            return CreatedAtAction(nameof(GetSources), null, result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Preview the transforms a clone will perform — year-delta shifts, name inference,
    /// admin deactivation counts — without committing. Used by the wizard's preview pane.
    /// </summary>
    [HttpPost("preview")]
    public async Task<ActionResult<JobClonePreviewResponse>> PreviewClone(
        [FromBody] JobCloneRequest request,
        CancellationToken ct)
    {
        try
        {
            // SuperUser-only → skip same-customer guard. Phase D passes the author's customerId.
            var result = await _cloneService.PreviewCloneAsync(request, authorCustomerId: null, ct);
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
        }
    }

    /// <summary>
    /// Create a brand-new empty job (no source) for new-customer onboarding.
    /// Lands with the same safe-by-default state as a clone. Author's admin reg is active.
    /// </summary>
    [HttpPost("blank")]
    public async Task<ActionResult<BlankJobResponse>> CreateBlank(
        [FromBody] BlankJobRequest request,
        CancellationToken ct)
    {
        var authorUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new UnauthorizedAccessException("User ID not found in token.");

        try
        {
            // SuperUser-only endpoint → no same-customer guard. Phase D opens up to in-customer admins.
            var result = await _cloneService.CreateBlankJobAsync(request, authorUserId, authorCustomerId: null, ct);
            return CreatedAtAction(nameof(GetSources), null, result);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Step 2→3 uniqueness check — flags whether the proposed jobPath and/or jobName
    /// already exist on another job. Returns { pathExists, nameExists }.
    /// </summary>
    [HttpGet("identity-exists")]
    public async Task<ActionResult<IdentityExistsResponse>> IdentityExists(
        [FromQuery] string path, [FromQuery] string name, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(name))
            return BadRequest(new { message = "path and name are both required." });

        var pathExists = await _cloneService.JobPathExistsAsync(path, ct);
        var nameExists = await _cloneService.JobNameExistsAsync(name, ct);
        return Ok(new IdentityExistsResponse { PathExists = pathExists, NameExists = nameExists });
    }

    /// <summary>
    /// List suspended (unreleased) jobs for the Landing screen.
    /// </summary>
    [HttpGet("suspended")]
    public async Task<ActionResult<List<SuspendedJobDto>>> GetSuspended(CancellationToken ct)
    {
        // SuperUser-only endpoint → null customerId returns all suspended jobs. Phase D filters.
        var result = await _cloneService.GetSuspendedJobsAsync(authorCustomerId: null, ct);
        return Ok(result);
    }

    /// <summary>
    /// List admin registrations on a suspended job — used to populate the Release screen's
    /// activation panel.
    /// </summary>
    [HttpGet("{jobId:guid}/admins")]
    public async Task<ActionResult<List<ReleasableAdminDto>>> GetAdmins(
        Guid jobId, CancellationToken ct)
    {
        var result = await _cloneService.GetReleasableAdminsAsync(jobId, ct);
        return Ok(result);
    }

    /// <summary>
    /// Flip Jobs.BSuspendPublic = false (release site to public).
    /// </summary>
    [HttpPost("{jobId:guid}/release-site")]
    public async Task<ActionResult<ReleaseResponse>> ReleaseSite(
        Guid jobId, CancellationToken ct)
    {
        var actorUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new UnauthorizedAccessException("User ID not found in token.");

        try
        {
            var result = await _cloneService.ReleaseSiteAsync(jobId, actorUserId, authorCustomerId: null, ct);
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
        }
    }

    /// <summary>
    /// Activate a set of admin registrations on a suspended job (flip BActive = true).
    /// </summary>
    [HttpPost("{jobId:guid}/release-admins")]
    public async Task<ActionResult<ReleaseResponse>> ReleaseAdmins(
        Guid jobId,
        [FromBody] ReleaseAdminsRequest request,
        CancellationToken ct)
    {
        var actorUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new UnauthorizedAccessException("User ID not found in token.");

        try
        {
            var result = await _cloneService.ReleaseAdminsAsync(
                jobId, request.RegistrationIds, actorUserId, authorCustomerId: null, ct);
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
        }
    }
}
