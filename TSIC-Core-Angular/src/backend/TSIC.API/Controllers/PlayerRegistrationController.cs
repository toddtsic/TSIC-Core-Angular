using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Services;
using TSIC.API.Services.Players;
using TSIC.API.Services.Teams;
using TSIC.API.Services.Families;
using TSIC.API.Services.Clubs;
using TSIC.API.Services.Payments;
using TSIC.API.Services.Metadata;
using TSIC.API.Services.Shared;
using TSIC.API.Services.External;
using TSIC.API.Services.Auth;
using TSIC.API.Services.Email;
using TSIC.API.Services.Validation;
using TSIC.Infrastructure.Data.SqlDbContext; // (no direct use; kept if DI decoration needed later)

namespace TSIC.API.Controllers;

[ApiController]
[Route("api/player-registration")]
public class PlayerRegistrationController : ControllerBase
{
    private readonly IJobLookupService _jobLookupService;
    private readonly IPlayerRegistrationService _registrationService;

    public PlayerRegistrationController(
        IJobLookupService jobLookupService,
        IPlayerRegistrationService registrationService)
    {
        _jobLookupService = jobLookupService;
        _registrationService = registrationService;
    }

    /// <summary>
    /// Checks team roster capacity and creates pending registrations (BActive=false) for available teams before payment.
    /// Returns per-team results and next tab to show.
    /// </summary>
    [HttpPost("preSubmit")]
    [Authorize]
    [ProducesResponseType(typeof(TSIC.Contracts.Dtos.PreSubmitPlayerRegistrationResponseDto), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    public async Task<IActionResult> PreSubmitRegistration([FromBody] TSIC.Contracts.Dtos.PreSubmitPlayerRegistrationRequestDto request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.JobPath) || string.IsNullOrWhiteSpace(request.FamilyUserId))
            return BadRequest(new { message = "Invalid preSubmit request" });

        var callerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (callerId == null) return Unauthorized();
        if (!string.Equals(callerId, request.FamilyUserId, StringComparison.OrdinalIgnoreCase))
            return Forbid();

        var jobId = await _jobLookupService.GetJobIdByPathAsync(request.JobPath);
        if (jobId is null)
            return NotFound(new { message = $"Job not found: {request.JobPath}" });

        // Delegate heavy lifting to service
        var response = await _registrationService.PreSubmitAsync(jobId.Value, request.FamilyUserId, request, callerId);
        return Ok(response);
    }

    // (Validation and form-application logic moved into service)
}
