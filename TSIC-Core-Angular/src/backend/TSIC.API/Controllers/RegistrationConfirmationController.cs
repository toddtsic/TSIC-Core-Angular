using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TSIC.API.Services;
using TSIC.API.Dtos;

namespace TSIC.API.Controllers;

[ApiController]
[Route("api/registration/confirmation")]
public sealed class RegistrationConfirmationController : ControllerBase
{
    private readonly IPlayerRegConfirmationService _service;
    private readonly ILogger<RegistrationConfirmationController> _logger;

    public RegistrationConfirmationController(IPlayerRegConfirmationService service, ILogger<RegistrationConfirmationController> logger)
    {
        _service = service;
        _logger = logger;
    }

    [HttpGet]
    [Authorize]
    [ProducesResponseType(typeof(PlayerRegConfirmationDto), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    public async Task<IActionResult> Get([FromQuery] Guid jobId, [FromQuery] Guid familyUserId, CancellationToken ct)
    {
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
}
