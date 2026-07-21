using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TSIC.Contracts.Dtos.EmailTroubleshooter;
using TSIC.Contracts.Services;

namespace TSIC.API.Controllers;

/// <summary>
/// Player-facing email deliverability self-service (companion to the admin
/// EmailTroubleshooterController). A logged-in family can, for their own emails only (mom/dad/each
/// player, across all jobs): check SES suppression status, self-unsuppress, send a real test
/// message, and review send history.
///
/// Security: the caller never supplies an address to act on. The family login (JWT subject)
/// resolves the sendable set server-side; unsuppress and test-send are refused for any address
/// outside it. Bare [Authorize] — the token is the boundary, matching AccountController/
/// FamilyController "self" endpoints. Suppression and history are account/cross-job wide, so no
/// job context is needed.
/// </summary>
[ApiController]
[Route("api/my-email-deliverability")]
[Authorize]
public class MyEmailDeliverabilityController : ControllerBase
{
    private readonly IMyEmailDeliverabilityService _service;

    public MyEmailDeliverabilityController(IMyEmailDeliverabilityService service)
    {
        _service = service;
    }

    [HttpGet("status")]
    [ProducesResponseType(typeof(IReadOnlyList<SuppressionEntryDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<SuppressionEntryDto>>> GetStatus(
        CancellationToken cancellationToken)
    {
        var familyUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(familyUserId))
        {
            return Unauthorized();
        }

        var results = await _service.GetStatusAsync(familyUserId, cancellationToken);
        return Ok(results);
    }

    [HttpPost("unsuppress")]
    [ProducesResponseType(typeof(SuppressionRemoveResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<SuppressionRemoveResultDto>> Unsuppress(
        [FromBody] MyEmailAddressRequest request,
        CancellationToken cancellationToken)
    {
        var familyUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(familyUserId))
        {
            return Unauthorized();
        }

        var result = await _service.UnsuppressAsync(familyUserId, request.Email, cancellationToken);
        if (result is null)
        {
            // Address is not one of the caller's own — never touched SES.
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        return Ok(result);
    }

    [HttpPost("test-send")]
    [ProducesResponseType(typeof(EmailInvestigateResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<EmailInvestigateResultDto>> TestSend(
        [FromBody] MyEmailAddressRequest request,
        CancellationToken cancellationToken)
    {
        var familyUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(familyUserId))
        {
            return Unauthorized();
        }

        var result = await _service.TestSendAsync(familyUserId, request.Email, cancellationToken);
        if (result is null)
        {
            // Address is not one of the caller's own — never sent.
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        return Ok(result);
    }

    [HttpGet("sent-history")]
    [ProducesResponseType(typeof(IReadOnlyList<PlayerSentEmailDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<PlayerSentEmailDto>>> GetSentHistory(
        CancellationToken cancellationToken)
    {
        var familyUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(familyUserId))
        {
            return Unauthorized();
        }

        var results = await _service.GetSentHistoryAsync(familyUserId, cancellationToken);
        return Ok(results);
    }
}
