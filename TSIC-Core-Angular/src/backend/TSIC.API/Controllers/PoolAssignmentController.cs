using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using TSIC.API.Extensions;
using TSIC.API.Services.Shared.Jobs;
using TSIC.Contracts.Dtos.PoolAssignment;
using TSIC.Contracts.Services;

namespace TSIC.API.Controllers;

[ApiController]
[Route("api/pool-assignment")]
[Authorize(Policy = "AdminOnly")]
public class PoolAssignmentController : ControllerBase
{
    private readonly ILogger<PoolAssignmentController> _logger;
    private readonly IPoolAssignmentService _poolService;
    private readonly IJobLookupService _jobLookupService;

    public PoolAssignmentController(
        ILogger<PoolAssignmentController> logger,
        IPoolAssignmentService poolService,
        IJobLookupService jobLookupService)
    {
        _logger = logger;
        _poolService = poolService;
        _jobLookupService = jobLookupService;
    }

    [HttpGet("divisions")]
    public async Task<ActionResult<List<PoolDivisionOptionDto>>> GetDivisions(CancellationToken ct)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null)
            return BadRequest(new { message = "Registration context required" });

        var divisions = await _poolService.GetDivisionOptionsAsync(jobId.Value, ct);
        return Ok(divisions);
    }

    [HttpGet("divisions/{divId:guid}/teams")]
    public async Task<ActionResult<List<PoolTeamDto>>> GetTeams(Guid divId, CancellationToken ct)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null)
            return BadRequest(new { message = "Registration context required" });

        try
        {
            var teams = await _poolService.GetTeamsAsync(divId, jobId.Value, ct);
            return Ok(teams);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("preview")]
    public async Task<ActionResult<PoolTransferPreviewResponse>> PreviewTransfer(
        [FromBody] PoolTransferPreviewRequest request, CancellationToken ct)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null)
            return BadRequest(new { message = "Registration context required" });

        try
        {
            var preview = await _poolService.PreviewTransferAsync(jobId.Value, request, ct);
            return Ok(preview);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("transfer")]
    public async Task<ActionResult<PoolTransferResultDto>> ExecuteTransfer(
        [FromBody] PoolTransferRequest request, CancellationToken ct)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null)
            return BadRequest(new { message = "Registration context required" });

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
            return Unauthorized();

        try
        {
            var result = await _poolService.ExecuteTransferAsync(jobId.Value, userId, request, ct);
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

    [HttpPut("teams/{teamId:guid}/active")]
    public async Task<ActionResult> ToggleTeamActive(
        Guid teamId, [FromBody] UpdateTeamActiveRequest request, CancellationToken ct)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null)
            return BadRequest(new { message = "Registration context required" });

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
            return Unauthorized();

        try
        {
            await _poolService.ToggleTeamActiveAsync(teamId, jobId.Value, request.Active, userId, ct);
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

    [HttpPut("teams/{teamId:guid}/divrank")]
    public async Task<ActionResult> UpdateTeamDivRank(
        Guid teamId, [FromBody] UpdateTeamDivRankRequest request, CancellationToken ct)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null)
            return BadRequest(new { message = "Registration context required" });

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
            return Unauthorized();

        try
        {
            await _poolService.UpdateTeamDivRankAsync(teamId, jobId.Value, request.DivRank, userId, ct);
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
