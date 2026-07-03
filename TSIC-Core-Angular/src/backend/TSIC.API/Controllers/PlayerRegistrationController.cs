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
    private readonly IJobRegistrationCapabilities _capabilities;
    private readonly IJobRepository _jobRepo;
    private readonly TSIC.API.Services.Invites.IInviteTokenService _inviteTokens;

    public PlayerRegistrationController(
        IJobLookupService jobLookupService,
        IPlayerRegistrationService registrationService,
        IFamilyRepository familyRepo,
        ITokenService tokenService,
        UserManager<ApplicationUser> userManager,
        IJobRegistrationCapabilities capabilities,
        IJobRepository jobRepo,
        TSIC.API.Services.Invites.IInviteTokenService inviteTokens)
    {
        _jobLookupService = jobLookupService;
        _registrationService = registrationService;
        _familyRepo = familyRepo;
        _tokenService = tokenService;
        _userManager = userManager;
        _capabilities = capabilities;
        _jobRepo = jobRepo;
        _inviteTokens = inviteTokens;
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

        // Determine role to mint — preserve Player role, default to Family
        var incomingRole = User.FindFirst(ClaimTypes.Role)?.Value;
        var mintRole = incomingRole == "Player" ? "Player" : "Family";

        if (mintRole != "Player")
        {
            // Family flow — verify family account exists
            var family = await _familyRepo.GetByFamilyUserIdAsync(userId);
            if (family == null)
            {
                return BadRequest(new { message = "Family account not found. Please create a family account first." });
            }
        }

        // Verify job exists
        var jobId = await _jobLookupService.GetJobIdByPathAsync(request.JobPath);
        if (jobId == null)
        {
            return BadRequest(new { message = $"Event not found: {request.JobPath}" });
        }

        // Invitation gate (write chokepoint). Family/Player is created downstream from THIS job-scoped
        // token, so a token-gated event must verify the signed invite here — only the invited user, for
        // this exact job, within the window, gets a wizard token. Authoritative; the guard is a pre-check.
        var regStatus = await _jobRepo.GetRegistrationStatusAsync(jobId.Value);
        if (regStatus?.BPlayerRegRequiresToken == true
            && !_inviteTokens.IsValidFor(request.InviteToken, jobId.Value, userId))
        {
            return BadRequest(new { message = "This event requires a valid invitation to register. Please use the invitation link from your email." });
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
        var token = _tokenService.GenerateJobScopedToken(user, request.JobPath, jobMetadata?.JobLogoPath, mintRole);

        return Ok(new AuthTokenResponse
        {
            AccessToken = token,
            RefreshToken = null,
            ExpiresIn = 3600
        });
    }

    /// <summary>
    /// Finalize at review-to-payment time. Creates registrations, applies form values,
    /// validates fields, reconciles seats (auto-switching any now-full team's player to its
    /// WAITLIST twin), mints twin-on-fill, and builds the insurance offer. Registrations are
    /// created HERE and nowhere else.
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

        // CREATE-AUTHORITY GATE — closes the long-standing hole: registrations are created HERE
        // and nowhere else, yet this path was ungated, so a returning parent (or a direct call)
        // could create a player registration for a CONCLUDED/superseded event whose generous
        // ExpiryUsers window was still open. CanRegisterPlayer = door(eventConcluded) AND
        // BRegistrationAllowPlayer AND a player fee row exists. Admins are exempt from the door.
        var caps = await _capabilities.ResolveAsync(jobId.Value, User.ToCapabilityActor());
        if (!caps.CanRegisterPlayer)
            return BadRequest(new { message = "This event is not accepting player registrations at this time." });

        // Delegate heavy lifting to service
        var response = await _registrationService.PreSubmitAsync(jobId.Value, familyUserId, request, familyUserId);
        return Ok(response);
    }

    /// <summary>
    /// Pay-by-check intake. Stamps PaymentMethodChosen=3 + BActive=true on the
    /// listed registrations so the roster spot is held while the check is in
    /// transit. No fee math. Idempotent. Strictly check-path: rejects rows
    /// already committed to a different payment method.
    /// </summary>
    [HttpPost("submit-by-check")]
    [Authorize]
    [ProducesResponseType(typeof(TSIC.Contracts.Dtos.SubmitByCheckResponseDto), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    public async Task<IActionResult> SubmitByCheck([FromBody] TSIC.Contracts.Dtos.SubmitByCheckRequestDto request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.JobPath))
            return BadRequest(new { message = "Invalid submit-by-check request" });

        var familyUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(familyUserId)) return Unauthorized();

        var jobId = await _jobLookupService.GetJobIdByPathAsync(request.JobPath);
        if (jobId is null)
            return NotFound(new { message = $"Job not found: {request.JobPath}" });

        var response = await _registrationService.SubmitByCheckAsync(jobId.Value, familyUserId, request, familyUserId);
        return Ok(response);
    }

    // (Validation and form-application logic moved into service)
}
