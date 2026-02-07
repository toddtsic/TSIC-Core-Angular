using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using TSIC.API.Extensions;
using TSIC.API.Services.Shared.Jobs;
using TSIC.Contracts.Dtos.DiscountCode;
using TSIC.Contracts.Services;

namespace TSIC.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "AdminOnly")]
public class DiscountCodesController : ControllerBase
{
    private readonly ILogger<DiscountCodesController> _logger;
    private readonly IDiscountCodeService _discountCodeService;
    private readonly IJobLookupService _jobLookupService;

    public DiscountCodesController(
        ILogger<DiscountCodesController> logger,
        IDiscountCodeService discountCodeService,
        IJobLookupService jobLookupService)
    {
        _logger = logger;
        _discountCodeService = discountCodeService;
        _jobLookupService = jobLookupService;
    }

    /// <summary>
    /// Get all discount codes for the authenticated user's job.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(List<DiscountCodeDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<List<DiscountCodeDto>>> GetDiscountCodes(
        CancellationToken cancellationToken)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null)
        {
            return BadRequest(new { message = "Registration context required" });
        }

        var codes = await _discountCodeService.GetDiscountCodesAsync(jobId.Value, cancellationToken);
        return Ok(codes);
    }

    /// <summary>
    /// Add a new discount code.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(DiscountCodeDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<DiscountCodeDto>> AddDiscountCode(
        [FromBody] AddDiscountCodeRequest request,
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
            var code = await _discountCodeService.AddDiscountCodeAsync(jobId.Value, userId, request, cancellationToken);
            return CreatedAtAction(nameof(GetDiscountCodes), new { }, code);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Bulk generate discount codes with sequential pattern.
    /// </summary>
    [HttpPost("bulk")]
    [ProducesResponseType(typeof(List<DiscountCodeDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<List<DiscountCodeDto>>> BulkAddDiscountCodes(
        [FromBody] BulkAddDiscountCodeRequest request,
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
            var codes = await _discountCodeService.BulkAddDiscountCodesAsync(jobId.Value, userId, request, cancellationToken);
            return CreatedAtAction(nameof(GetDiscountCodes), new { }, codes);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Update an existing discount code.
    /// </summary>
    [HttpPut("{ai:int}")]
    [ProducesResponseType(typeof(DiscountCodeDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DiscountCodeDto>> UpdateDiscountCode(
        int ai,
        [FromBody] UpdateDiscountCodeRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var code = await _discountCodeService.UpdateDiscountCodeAsync(ai, request, cancellationToken);
            return Ok(code);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Delete a discount code (only if not used).
    /// </summary>
    [HttpDelete("{ai:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> DeleteDiscountCode(
        int ai,
        CancellationToken cancellationToken)
    {
        try
        {
            var deleted = await _discountCodeService.DeleteDiscountCodeAsync(ai, cancellationToken);
            if (!deleted)
            {
                return NotFound(new { message = $"Discount code with ID {ai} not found" });
            }

            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Batch activate/deactivate discount codes.
    /// </summary>
    [HttpPost("batch-status")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult> BatchUpdateStatus(
        [FromBody] BatchUpdateStatusRequest request,
        CancellationToken cancellationToken)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null)
        {
            return BadRequest(new { message = "Registration context required" });
        }

        var count = await _discountCodeService.BatchUpdateStatusAsync(jobId.Value, request.CodeIds, request.IsActive, cancellationToken);
        return Ok(new { updatedCount = count });
    }

    /// <summary>
    /// Check if a discount code already exists for the job.
    /// </summary>
    [HttpGet("check-exists/{codeName}")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult> CheckCodeExists(
        string codeName,
        CancellationToken cancellationToken)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null)
        {
            return BadRequest(new { message = "Registration context required" });
        }

        var exists = await _discountCodeService.CheckCodeExistsAsync(jobId.Value, codeName, cancellationToken);
        return Ok(new { exists });
    }
}
