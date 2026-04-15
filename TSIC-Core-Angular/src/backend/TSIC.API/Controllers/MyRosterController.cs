using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TSIC.API.Extensions;
using TSIC.Contracts.Dtos.MyRoster;
using TSIC.Contracts.Dtos.RegistrationSearch;
using TSIC.Contracts.Services;

namespace TSIC.API.Controllers;

[ApiController]
[Route("api/my-roster")]
[Authorize]
public class MyRosterController : ControllerBase
{
    private readonly IMyRosterService _myRosterService;

    public MyRosterController(IMyRosterService myRosterService)
    {
        _myRosterService = myRosterService;
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
    public async Task<ActionResult<BatchEmailResponse>> SendEmail(
        [FromBody] MyRosterBatchEmailRequest request, CancellationToken ct)
    {
        var regId = User.GetRegistrationId();
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (regId == null || string.IsNullOrEmpty(userId))
            return BadRequest(new { message = "Registration context required." });

        try
        {
            var result = await _myRosterService.SendBatchEmailAsync(regId.Value, userId, request, ct);
            return Ok(result);
        }
        catch (UnauthorizedAccessException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
