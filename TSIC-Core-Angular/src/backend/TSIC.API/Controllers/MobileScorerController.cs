using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using TSIC.API.Extensions;
using TSIC.API.Services.Shared.Jobs;
using TSIC.Contracts.Dtos.Scoring;
using TSIC.Contracts.Services;

namespace TSIC.API.Controllers;

[ApiController]
[Route("api/mobile-scorers")]
[Authorize(Policy = "AdminOnly")]
public class MobileScorerController : ControllerBase
{
    private readonly IMobileScorerService _service;
    private readonly IJobLookupService _jobLookupService;

    public MobileScorerController(
        IMobileScorerService service,
        IJobLookupService jobLookupService)
    {
        _service = service;
        _jobLookupService = jobLookupService;
    }

    /// <summary>
    /// List all scorers for the authenticated user's job.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(List<MobileScorerDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<List<MobileScorerDto>>> GetScorers(
        CancellationToken cancellationToken)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null)
            return BadRequest(new { message = "Registration context required" });

        var scorers = await _service.GetScorersAsync(jobId.Value, cancellationToken);
        return Ok(scorers);
    }

    /// <summary>
    /// Create a new scorer (user account + registration).
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(MobileScorerDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<MobileScorerDto>> CreateScorer(
        [FromBody] CreateMobileScorerRequest request,
        CancellationToken cancellationToken)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null)
            return BadRequest(new { message = "Registration context required" });

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
            return Unauthorized();

        try
        {
            var scorer = await _service.CreateScorerAsync(jobId.Value, request, userId, cancellationToken);
            return CreatedAtAction(nameof(GetScorers), new { }, scorer);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Update a scorer's active status, email, and cellphone.
    /// </summary>
    [HttpPut("{registrationId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> UpdateScorer(
        Guid registrationId,
        [FromBody] UpdateMobileScorerRequest request,
        CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
            return Unauthorized();

        try
        {
            await _service.UpdateScorerAsync(registrationId, request, userId, cancellationToken);
            return NoContent();
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Delete a scorer registration (and the user if orphaned).
    /// </summary>
    [HttpDelete("{registrationId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> DeleteScorer(
        Guid registrationId,
        CancellationToken cancellationToken)
    {
        try
        {
            await _service.DeleteScorerAsync(registrationId, cancellationToken);
            return NoContent();
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }
}
