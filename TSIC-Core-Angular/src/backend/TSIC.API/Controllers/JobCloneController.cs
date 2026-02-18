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
            var result = await _cloneService.CloneJobAsync(request, superUserId, ct);
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
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
