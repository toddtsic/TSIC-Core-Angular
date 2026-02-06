using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Services;
using TSIC.Contracts.Repositories;
using TSIC.API.Services.Players;
using TSIC.API.Services.Teams;
using TSIC.API.Services.Families;
using TSIC.API.Services.Clubs;
using TSIC.API.Services.Payments;
using TSIC.API.Services.Metadata;
using TSIC.API.Services.Shared;
using TSIC.API.Services.Shared.VerticalInsure;
using TSIC.API.Services.Shared.Jobs;
using TSIC.API.Services.Auth;
using TSIC.API.Services.Shared.UsLax;
using Microsoft.AspNetCore.Identity;
using TSIC.Infrastructure.Data.Identity;

namespace TSIC.API.Controllers;

[ApiController]
[Route("api/player-registration")]
public class PlayerRegistrationController : ControllerBase
{
    private readonly IJobLookupService _jobLookupService;
    private readonly IPlayerRegistrationService _registrationService;
    private readonly IFamilyRepository _familyRepo;
    private readonly ITokenService _tokenService;
    private readonly UserManager<ApplicationUser> _userManager;

    public PlayerRegistrationController(
        IJobLookupService jobLookupService,
        IPlayerRegistrationService registrationService,
        IFamilyRepository familyRepo,
        ITokenService tokenService,
        UserManager<ApplicationUser> userManager)
    {
        _jobLookupService = jobLookupService;
        _registrationService = registrationService;
        _familyRepo = familyRepo;
        _tokenService = tokenService;
        _userManager = userManager;
    }

    /// <summary>
    /// Set wizard context after family login - upgrades Phase 1 token to job-scoped token.
    /// Adds jobPath claim to JWT without creating Registration record (no regId).
    /// </summary>
    [HttpPost("set-wizard-context")]
    [Authorize] // Requires Phase 1 token (username only)
    [ProducesResponseType(typeof(AuthTokenResponse), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    public async Task<IActionResult> SetWizardContext([FromBody] SetWizardContextRequest request)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized(new { message = "User not authenticated" });
        }

        // Verify family account exists
        var family = await _familyRepo.GetByFamilyUserIdAsync(userId);
        if (family == null)
        {
            return BadRequest(new { message = "Family account not found. Please create a family account first." });
        }

        // Verify job exists
        var jobId = await _jobLookupService.GetJobIdByPathAsync(request.JobPath);
        if (jobId == null)
        {
            return BadRequest(new { message = $"Event not found: {request.JobPath}" });
        }

        // Get job details for logo
        var jobMetadata = await _jobLookupService.GetJobMetadataAsync(request.JobPath);

        // Get user for token generation
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null)
        {
            return Unauthorized(new { message = "User not found" });
        }

        // Generate job-scoped token (jobPath + role, NO regId)
        var token = _tokenService.GenerateJobScopedToken(user, request.JobPath, jobMetadata?.JobLogoPath, "Family");

        return Ok(new AuthTokenResponse
        {
            AccessToken = token,
            RefreshToken = null,
            ExpiresIn = 3600
        });
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
        if (request == null || string.IsNullOrWhiteSpace(request.JobPath))
            return BadRequest(new { message = "Invalid preSubmit request" });

        var familyUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(familyUserId)) return Unauthorized();

        var jobId = await _jobLookupService.GetJobIdByPathAsync(request.JobPath);
        if (jobId is null)
            return NotFound(new { message = $"Job not found: {request.JobPath}" });

        // Delegate heavy lifting to service
        var response = await _registrationService.PreSubmitAsync(jobId.Value, familyUserId, request, familyUserId);
        return Ok(response);
    }

    // (Validation and form-application logic moved into service)
}
