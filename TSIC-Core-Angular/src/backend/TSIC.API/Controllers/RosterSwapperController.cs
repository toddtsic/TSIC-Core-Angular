using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using TSIC.API.Extensions;
using TSIC.API.Services.Shared.Jobs;
using TSIC.Contracts.Dtos.RosterSwapper;
using TSIC.Contracts.Services;

namespace TSIC.API.Controllers;

[ApiController]
[Route("api/roster-swapper")]
[Authorize(Policy = "AdminOnly")]
public class RosterSwapperController : ControllerBase
{
    private readonly ILogger<RosterSwapperController> _logger;
    private readonly IRosterSwapperService _swapperService;
    private readonly IJobLookupService _jobLookupService;

    public RosterSwapperController(
        ILogger<RosterSwapperController> logger,
        IRosterSwapperService swapperService,
        IJobLookupService jobLookupService)
    {
        _logger = logger;
        _swapperService = swapperService;
        _jobLookupService = jobLookupService;
    }

    [HttpGet("pools")]
    public async Task<ActionResult<List<SwapperPoolOptionDto>>> GetPools(CancellationToken ct)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null)
            return BadRequest(new { message = "Registration context required" });

        var pools = await _swapperService.GetPoolOptionsAsync(jobId.Value, ct);
        return Ok(pools);
    }

    [HttpGet("roster/{poolId:guid}")]
    public async Task<ActionResult<List<SwapperPlayerDto>>> GetRoster(Guid poolId, CancellationToken ct)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null)
            return BadRequest(new { message = "Registration context required" });

        try
        {
            var roster = await _swapperService.GetRosterAsync(poolId, jobId.Value, ct);
            return Ok(roster);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("preview")]
    public async Task<ActionResult<List<RosterTransferFeePreviewDto>>> PreviewTransfer(
        [FromBody] RosterTransferPreviewRequest request, CancellationToken ct)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null)
            return BadRequest(new { message = "Registration context required" });

        try
        {
            var preview = await _swapperService.PreviewTransferAsync(jobId.Value, request, ct);
            return Ok(preview);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("transfer")]
    public async Task<ActionResult<RosterTransferResultDto>> ExecuteTransfer(
        [FromBody] RosterTransferRequest request, CancellationToken ct)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null)
            return BadRequest(new { message = "Registration context required" });

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
            return Unauthorized();

        try
        {
            var result = await _swapperService.ExecuteTransferAsync(jobId.Value, userId, request, ct);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPut("players/{registrationId:guid}/active")]
    public async Task<ActionResult> TogglePlayerActive(
        Guid registrationId, [FromBody] UpdatePlayerActiveRequest request, CancellationToken ct)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null)
            return BadRequest(new { message = "Registration context required" });

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
            return Unauthorized();

        try
        {
            await _swapperService.TogglePlayerActiveAsync(registrationId, jobId.Value, request.BActive, userId, ct);
            return NoContent();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
