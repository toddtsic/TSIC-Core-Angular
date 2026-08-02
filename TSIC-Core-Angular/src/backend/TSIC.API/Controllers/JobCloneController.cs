using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TSIC.API.Extensions;
using TSIC.Contracts.Dtos.JobClone;
using TSIC.Contracts.Services;

namespace TSIC.API.Controllers;

/// <summary>
/// SuperUser-only endpoint for cloning a job (tournament/event) to create a new season,
/// plus the verify-then-release flow on the cloned job.
/// </summary>
[ApiController]
[Route("api/job-clone")]
[Authorize(Policy = "SuperUserOnly")]
public class JobCloneController : ControllerBase
{
    private readonly IJobCloneService _cloneService;
    private readonly IHostEnvironment _env;

    public JobCloneController(IJobCloneService cloneService, IHostEnvironment env)
    {
        _cloneService = cloneService;
        _env = env;
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
    /// Returns 409 with the FRESH plan when the data-moved guard trips (source data
    /// changed between preview and submit).
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
            var result = await _cloneService.CloneJobAsync(request, superUserId, authorCustomerId: null, ct);
            // Location: the release page's data source for the freshly-cloned job.
            return CreatedAtAction(nameof(GetVerifyChecklist), new { jobId = result.NewJobId }, result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (ClonePlanChangedException ex)
        {
            // Data-moved guard: body carries the fresh plan so the workbench re-renders it.
            return Conflict(new { message = ex.Message, freshPlan = ex.FreshPlan });
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
    /// Build the clone plan (no writes) — per-step counts, warnings, eligibility breakdown,
    /// fingerprint. The workbench's live plan pane refetches this on debounced input change.
    /// </summary>
    [HttpPost("preview")]
    public async Task<ActionResult<ClonePlanDto>> PreviewClone(
        [FromBody] JobCloneRequest request,
        CancellationToken ct)
    {
        var actorUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new UnauthorizedAccessException("User ID not found in token.");

        try
        {
            var result = await _cloneService.PreviewCloneAsync(request, actorUserId, authorCustomerId: null, ct);
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
        catch (ArgumentException ex)
        {
            // Same input → same status as submit (bad choice strings are 400 here too).
            return BadRequest(new { message = ex.Message });
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
            var result = await _cloneService.CreateBlankJobAsync(request, authorUserId, authorCustomerId: null, ct);
            return CreatedAtAction(nameof(GetVerifyChecklist), new { jobId = result.NewJobId }, result);
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
    /// Inline identity uniqueness check — flags whether the proposed jobPath and/or
    /// jobName already exist on another job. Returns { pathExists, nameExists }.
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
    /// Verify-then-release checklist (release panel 1): the cloned job's live settings
    /// grouped by section, ordered by job-type relevance, with configure deep-links.
    /// </summary>
    [HttpGet("{jobId:guid}/verify")]
    public async Task<ActionResult<JobVerifyChecklistDto>> GetVerifyChecklist(
        Guid jobId, CancellationToken ct)
    {
        try
        {
            var result = await _cloneService.GetVerifyChecklistAsync(jobId, ct);
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Flip Jobs.BSuspendPublic = false (release site to public). Release panel 2.
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
    /// Release panel 3.
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

    /// <summary>
    /// Open registration for the chosen personas (release panel 4). All five
    /// BRegistrationAllow* flags start FALSE on every clone; this flips the chosen ones ON.
    /// </summary>
    [HttpPost("{jobId:guid}/open-registration")]
    public async Task<ActionResult<RegistrationFlagsDto>> OpenRegistration(
        Guid jobId,
        [FromBody] OpenRegistrationRequest request,
        CancellationToken ct)
    {
        var actorUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new UnauthorizedAccessException("User ID not found in token.");

        try
        {
            var result = await _cloneService.OpenRegistrationAsync(
                jobId, request, actorUserId, authorCustomerId: null, ct);
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

    // ── Sandbox undo (cascade delete a freshly-cloned job) ──
    // Gated by IsSandbox() (Development + Staging) on top of SuperUserOnly. In prod, both
    // endpoints 404 so a cascade delete is never reachable there.

    /// <summary>
    /// Sandbox only: returns whether a freshly-cloned job can be cascade-deleted, with row counts
    /// for the confirm modal. CanUndo=true requires only admin Registrations, zero
    /// RegistrationAccounting, and zero rows in any ancillary job-scoped table.
    /// </summary>
    [HttpGet("{jobId:guid}/dev-undo-status")]
    public async Task<ActionResult<DevUndoStatusResponse>> GetDevUndoStatus(
        Guid jobId, CancellationToken ct)
    {
        if (!_env.IsSandbox())
            return NotFound();

        var result = await _cloneService.GetDevUndoStatusAsync(jobId, ct);
        return Ok(result);
    }

    /// <summary>
    /// Sandbox only: cascade-delete a freshly-cloned job. Re-runs predicate checks inside the
    /// delete transaction (TOCTOU defense). Returns 409 if predicates fail at delete time.
    /// </summary>
    [HttpDelete("{jobId:guid}/dev-undo")]
    public async Task<IActionResult> DeleteClonedJob(Guid jobId, CancellationToken ct)
    {
        if (!_env.IsSandbox())
            return NotFound();

        try
        {
            await _cloneService.DeleteClonedJobAsync(jobId, ct);
            return NoContent();
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }
}
