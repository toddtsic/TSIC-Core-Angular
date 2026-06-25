using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TSIC.API.Extensions;
using TSIC.Contracts.Dtos.MyRoster;
using TSIC.Contracts.Services;

namespace TSIC.API.Controllers;

[ApiController]
[Route("api/my-roster")]
[Authorize]
public class MyRosterController : ControllerBase
{
    private readonly IMyRosterService _myRosterService;
    private readonly IEmailBatchJobRegistry _batchJobs;

    public MyRosterController(IMyRosterService myRosterService, IEmailBatchJobRegistry batchJobs)
    {
        _myRosterService = myRosterService;
        _batchJobs = batchJobs;
    }

    [HttpGet]
    public async Task<ActionResult<MyRosterResponseDto>> Get(CancellationToken ct)
    {
        var regId = User.GetRegistrationId();
        if (regId == null)
            return BadRequest(new { message = "Registration context required." });

        var result = await _myRosterService.GetMyRosterAsync(regId.Value, ct);
        return Ok(result);
    }

    [HttpPost("email")]
    public async Task<ActionResult<EmailBatchHandle>> SendEmail(
        [FromBody] MyRosterBatchEmailRequest request, CancellationToken ct)
    {
        var regId = User.GetRegistrationId();
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (regId == null || string.IsNullOrEmpty(userId))
            return BadRequest(new { message = "Registration context required." });

        try
        {
            var handle = await _myRosterService.StartBatchEmailAsync(regId.Value, userId, request, ct);
            return Ok(handle);
        }
        catch (UnauthorizedAccessException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>Progress / final summary for a background roster email batch (404 if unknown/expired).</summary>
    [HttpGet("email/{batchJobId:guid}/status")]
    public ActionResult<EmailBatchJobStatus> GetEmailStatus(Guid batchJobId)
    {
        var status = _batchJobs.Get(batchJobId);
        return status is null ? NotFound() : Ok(status);
    }
}
