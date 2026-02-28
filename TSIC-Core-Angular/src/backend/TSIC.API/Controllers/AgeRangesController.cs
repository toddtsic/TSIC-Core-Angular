using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TSIC.API.Extensions;
using TSIC.API.Services.Admin;
using TSIC.API.Services.Shared.Jobs;
using TSIC.Contracts.Dtos.AgeRange;

namespace TSIC.API.Controllers;

/// <summary>
/// Controller for managing DOB-based age ranges per job.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "AdminOnly")]
public class AgeRangesController : ControllerBase
{
    private readonly IAgeRangeService _ageRangeService;
    private readonly IJobLookupService _jobLookupService;

    public AgeRangesController(
        IAgeRangeService ageRangeService,
        IJobLookupService jobLookupService)
    {
        _ageRangeService = ageRangeService;
        _jobLookupService = jobLookupService;
    }

    /// <summary>
    /// Get all age ranges for the current job.
    /// </summary>
    [HttpGet("admin")]
    [ProducesResponseType(typeof(List<AgeRangeDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<List<AgeRangeDto>>> GetAll(
        CancellationToken cancellationToken)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null)
        {
            return BadRequest(new { message = "Registration context required" });
        }

        var ranges = await _ageRangeService.GetAllForJobAsync(jobId.Value, cancellationToken);
        return Ok(ranges);
    }

    /// <summary>
    /// Create a new age range.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(AgeRangeDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<AgeRangeDto>> Create(
        [FromBody] CreateAgeRangeRequest request,
        CancellationToken cancellationToken)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null)
        {
            return BadRequest(new { message = "Registration context required" });
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized();
        }

        try
        {
            var range = await _ageRangeService.CreateAsync(jobId.Value, userId, request, cancellationToken);
            return CreatedAtAction(nameof(GetAll), new { }, range);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Update an existing age range.
    /// </summary>
    [HttpPut("{ageRangeId:int}")]
    [ProducesResponseType(typeof(AgeRangeDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AgeRangeDto>> Update(
        int ageRangeId,
        [FromBody] UpdateAgeRangeRequest request,
        CancellationToken cancellationToken)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null)
        {
            return BadRequest(new { message = "Registration context required" });
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized();
        }

        try
        {
            var range = await _ageRangeService.UpdateAsync(ageRangeId, jobId.Value, userId, request, cancellationToken);
            return Ok(range);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Delete an age range.
    /// </summary>
    [HttpDelete("{ageRangeId:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> Delete(
        int ageRangeId,
        CancellationToken cancellationToken)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null)
        {
            return BadRequest(new { message = "Registration context required" });
        }

        try
        {
            var deleted = await _ageRangeService.DeleteAsync(ageRangeId, jobId.Value, cancellationToken);
            if (!deleted)
            {
                return NotFound(new { message = $"Age range with ID {ageRangeId} not found" });
            }

            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
