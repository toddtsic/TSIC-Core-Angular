using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TSIC.API.Services;
using TSIC.API.Dtos;

namespace TSIC.API.Controllers;

[ApiController]
[Route("api/registration")] // base route; action supplies segment
public sealed class RegistrationConfirmationController : ControllerBase
{
    private readonly IPlayerRegConfirmationService _service;
    private readonly ILogger<RegistrationConfirmationController> _logger;

    public RegistrationConfirmationController(IPlayerRegConfirmationService service, ILogger<RegistrationConfirmationController> logger)
    {
        _service = service;
        _logger = logger;
    }

    [HttpGet("confirmation")]
    [Authorize]
    [ProducesResponseType(typeof(PlayerRegConfirmationDto), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    public async Task<IActionResult> Get([FromQuery] Guid jobId, [FromQuery] Guid familyUserId, CancellationToken ct)
    {
        _logger.LogInformation("[Confirmation] GET invoked jobId={JobId} familyUserId={FamilyUserId}", jobId, familyUserId);
        if (jobId == Guid.Empty || familyUserId == Guid.Empty)
        {
            return BadRequest(new { message = "Invalid parameters" });
        }

        // Authorization: ensure caller matches familyUserId.
        var claimId = User?.FindFirst("sub")?.Value ?? User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(claimId) || !claimId.Equals(familyUserId.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Confirmation access denied for familyUserId={FamilyUserId} caller={Caller}", familyUserId, claimId);
            return Forbid();
        }

        var dto = await _service.BuildAsync(jobId, familyUserId.ToString(), ct);
        return Ok(dto);
    }

    // HEAD endpoint (some clients/browsers may probe; avoids 405)
    [HttpHead("confirmation")]
    [Authorize]
    public IActionResult Head([FromQuery] Guid jobId, [FromQuery] Guid familyUserId)
    {
        if (jobId == Guid.Empty || familyUserId == Guid.Empty) return BadRequest();
        var claimId = User?.FindFirst("sub")?.Value ?? User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(claimId) || !claimId.Equals(familyUserId.ToString(), StringComparison.OrdinalIgnoreCase)) return Forbid();
        return Ok();
    }
}
