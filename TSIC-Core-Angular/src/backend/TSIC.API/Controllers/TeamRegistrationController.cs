using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.Transactions;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Services;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Extensions;
using TSIC.API.Services.Players;
using TSIC.API.Services.Teams;
using TSIC.API.Services.Families;
using TSIC.API.Services.Clubs;
using TSIC.API.Services.Payments;
using TSIC.API.Services.Metadata;
using TSIC.API.Services.Shared;
using TSIC.API.Services.Shared.Jobs;
using TSIC.API.Services.Shared.VerticalInsure;
using TSIC.API.Services.Auth;
using TSIC.API.Services.Shared.UsLax;
using Microsoft.AspNetCore.Identity;
using TSIC.Infrastructure.Data.Identity;
using TSIC.Domain.Constants;
using TSIC.Application.Services.Shared.Discount;
using TSIC.API.Extensions;

namespace TSIC.API.Controllers;

[ApiController]
[Route("api/team-registration")]
[Authorize]
public class TeamRegistrationController : ControllerBase
{
    private const string UserNotAuthenticatedMessage = "User not authenticated";
    private const string NotClubRepMessage = "This endpoint is restricted to Club Rep accounts.";
    private const string UnknownTeamName = "Unknown";

    private bool IsClubRepRole()
    {
        var role = User.FindFirst(ClaimTypes.Role)?.Value
                ?? User.FindFirst("http://schemas.microsoft.com/ws/2008/06/identity/claims/role")?.Value;
        return string.Equals(role, "Club Rep", StringComparison.OrdinalIgnoreCase)
            || string.Equals(role, "ClubRep", StringComparison.OrdinalIgnoreCase);
    }

    private readonly ITeamRegistrationService _teamRegistrationService;
    private readonly ILogger<TeamRegistrationController> _logger;
    private readonly IJobLookupService _jobLookupService;
    private readonly IJobDiscountCodeRepository _discountCodeRepo;
    private readonly ITeamRepository _teamRepository;
    private readonly IRegistrationRepository _registrationRepository;
    private readonly IRegistrationFeeAdjustmentService _feeAdjustment;
    private readonly IPaymentStateService _paymentState;
    private readonly ITokenService _tokenService;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IJobRegistrationCapabilities _capabilities;

    public TeamRegistrationController(
        ITeamRegistrationService teamRegistrationService,
        ILogger<TeamRegistrationController> logger,
        IJobLookupService jobLookupService,
        IJobDiscountCodeRepository discountCodeRepo,
        ITeamRepository teamRepository,
        IRegistrationRepository registrationRepository,
        IRegistrationFeeAdjustmentService feeAdjustment,
        IPaymentStateService paymentState,
        ITokenService tokenService,
        UserManager<ApplicationUser> userManager,
        IJobRegistrationCapabilities capabilities)
    {
        _teamRegistrationService = teamRegistrationService;
        _logger = logger;
        _jobLookupService = jobLookupService;
        _discountCodeRepo = discountCodeRepo;
        _teamRepository = teamRepository;
        _registrationRepository = registrationRepository;
        _feeAdjustment = feeAdjustment;
        _paymentState = paymentState;
        _tokenService = tokenService;
        _userManager = userManager;
        _capabilities = capabilities;
    }

    /// <summary>
    /// Get clubs that the current user is a rep for, with usage status.
    /// </summary>
    [HttpGet("my-clubs")]
    [ProducesResponseType(typeof(List<ClubRepClubDto>), 200)]
    [ProducesResponseType(401)]
    public async Task<IActionResult> GetMyClubs()
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
            return Unauthorized(new { Message = UserNotAuthenticatedMessage });

        try
        {
            var clubs = await _teamRegistrationService.GetMyClubsAsync(userId);
            return Ok(clubs);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting clubs for user {UserId}", userId);
            return StatusCode(500, new { Message = "An error occurred while retrieving clubs" });
        }
    }

    /// <summary>
    /// Initialize registration after club selection.
    /// Finds or creates Registration record and returns Phase 2 token with regId.
    /// </summary>
    [HttpPost("initialize-registration")]
    [ProducesResponseType(typeof(AuthTokenResponse), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    public async Task<IActionResult> InitializeRegistration([FromBody] InitializeRegistrationRequest request)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
            return Unauthorized(new { Message = UserNotAuthenticatedMessage });

        try
        {
            var response = await _teamRegistrationService.InitializeRegistrationAsync(userId, request.ClubName, request.JobPath, request.InviteToken);
            return Ok(response);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Failed to initialize registration for user {UserId}, club {ClubName}, job {JobPath}",
                userId, request.ClubName, request.JobPath);
            return BadRequest(new { Message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initializing registration for user {UserId}, club {ClubName}, job {JobPath}",
                userId, request.ClubName, request.JobPath);
            return StatusCode(500, new { Message = "An error occurred while initializing registration" });
        }
    }

    /// <summary>
    /// Find-only context upgrade for the ClubRepVIUpdate (team-insurance second-chance) flow.
    /// Looks up an existing ClubRep registration for (userId, jobPath) and mints an enriched
    /// JWT carrying its regId. Returns 400 if no such registration exists — the existing
    /// in-wizard `initialize-registration` endpoint creates one; this one never does.
    /// </summary>
    [HttpPost("set-clubrep-context")]
    [ProducesResponseType(typeof(AuthTokenResponse), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    public async Task<IActionResult> SetClubRepContext([FromBody] SetWizardContextRequest request)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
            return Unauthorized(new { message = UserNotAuthenticatedMessage });

        var jobId = await _jobLookupService.GetJobIdByPathAsync(request.JobPath);
        if (jobId is null)
            return BadRequest(new { message = $"Event not found: {request.JobPath}" });

        var registration = await _registrationRepository.GetClubRepRegistrationAsync(userId, jobId.Value);
        if (registration is null)
            return BadRequest(new { message = "No team registration found for this event." });

        var user = await _userManager.FindByIdAsync(userId);
        if (user is null)
            return Unauthorized(new { message = UserNotAuthenticatedMessage });

        var jobMetadata = await _jobLookupService.GetJobMetadataAsync(request.JobPath);
        var token = _tokenService.GenerateEnrichedJwtToken(
            user,
            registration.RegistrationId.ToString(),
            request.JobPath,
            jobMetadata?.JobLogoPath,
            RoleConstants.Names.ClubRepName);

        return Ok(new AuthTokenResponse
        {
            AccessToken = token,
            RefreshToken = null,
            ExpiresIn = 3600
        });
    }

    /// <summary>
    /// Check if another club rep has already registered teams for this event+club.
    /// Returns conflict info to warn user before they attempt registration.
    /// </summary>
    [HttpGet("check-existing")]
    [ProducesResponseType(typeof(CheckExistingRegistrationsResponse), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    public async Task<IActionResult> CheckExistingRegistrations([FromQuery] string jobPath, [FromQuery] string clubName)
    {
        if (string.IsNullOrWhiteSpace(jobPath))
        {
            return BadRequest(new { Message = "jobPath is required" });
        }

        if (string.IsNullOrWhiteSpace(clubName))
        {
            return BadRequest(new { Message = "clubName is required" });
        }

        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized(new { Message = UserNotAuthenticatedMessage });
        }

        try
        {
            var response = await _teamRegistrationService.CheckExistingRegistrationsAsync(jobPath, clubName, userId);
            return Ok(response);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Failed to check existing registrations for user {UserId}, job {JobPath}, club {ClubName}", userId, jobPath, clubName);
            return BadRequest(new { Message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking existing registrations for user {UserId}, job {JobPath}, club {ClubName}", userId, jobPath, clubName);
            return StatusCode(500, new { Message = "An error occurred while checking existing registrations" });
        }
    }

    /// <summary>
    /// Get teams metadata for the current club and event.
    /// Context derived from regId token claim.
    /// </summary>
    [HttpGet("metadata")]
    [ProducesResponseType(typeof(TeamsMetadataResponse), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    public async Task<IActionResult> GetMetadata([FromQuery] bool bPayBalanceDue = false)
    {
        if (!IsClubRepRole())
            return StatusCode(403, new { Message = NotClubRepMessage });

        var regIdClaim = User.FindFirst("regId")?.Value;
        if (string.IsNullOrEmpty(regIdClaim) || !Guid.TryParse(regIdClaim, out var regId))
            return Unauthorized(new { Message = "Registration ID not found in token. Please select a club first." });

        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
            return Unauthorized(new { Message = UserNotAuthenticatedMessage });

        try
        {
            var response = await _teamRegistrationService.GetTeamsMetadataAsync(regId, userId, bPayBalanceDue);
            return Ok(response);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Failed to get teams metadata for user {UserId}, regId {RegId}", userId, regId);
            return BadRequest(new { Message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting teams metadata for user {UserId}, regId {RegId}", userId, regId);
            return StatusCode(500, new { Message = "An error occurred while retrieving team data" });
        }
    }

    /// <summary>
    /// Register a ClubTeam for the current event.
    /// Context derived from regId token claim.
    /// </summary>
    [HttpPost("register-team")]
    [ProducesResponseType(typeof(RegisterTeamResponse), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    public async Task<IActionResult> RegisterTeam([FromBody] RegisterTeamRequest request)
    {
        if (!IsClubRepRole())
            return StatusCode(403, new { Message = NotClubRepMessage });

        var regIdClaim = User.FindFirst("regId")?.Value;
        if (string.IsNullOrEmpty(regIdClaim) || !Guid.TryParse(regIdClaim, out var regId))
            return Unauthorized(new { Message = "Registration ID not found in token. Please select a club first." });

        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
            return Unauthorized(new { Message = UserNotAuthenticatedMessage });

        try
        {
            var response = await _teamRegistrationService.RegisterTeamForEventAsync(request, regId, userId);
            if (!response.Success)
            {
                return BadRequest(response);
            }
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error registering team for user {UserId}, regId {RegId}", userId, regId);
            return StatusCode(500, new { Message = "An error occurred while registering the team" });
        }
    }

    /// <summary>
    /// Accept the refund policy for club rep registration.
    /// </summary>
    [HttpPost("accept-refund-policy")]
    [ProducesResponseType(200)]
    [ProducesResponseType(401)]
    public async Task<IActionResult> AcceptRefundPolicy()
    {
        var regIdClaim = User.FindFirst("regId")?.Value;
        if (string.IsNullOrEmpty(regIdClaim) || !Guid.TryParse(regIdClaim, out var regId))
        {
            return Unauthorized(new { Message = "Registration ID not found in token" });
        }

        try
        {
            await _teamRegistrationService.AcceptRefundPolicyAsync(regId);
            return Ok(new { Message = "Refund policy acceptance recorded" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error accepting refund policy for registration {RegId}", regId);
            return StatusCode(500, new { Message = "An error occurred while recording acceptance" });
        }
    }

    /// <summary>
    /// Unregister a Team from the current event.
    /// </summary>
    [HttpDelete("unregister-team/{teamId}")]
    [ProducesResponseType(200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    public async Task<IActionResult> UnregisterTeam(Guid teamId)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
            return Unauthorized(new { Message = UserNotAuthenticatedMessage });

        try
        {
            await _teamRegistrationService.UnregisterTeamFromEventAsync(teamId, userId);
            return Ok(new { Success = true, Message = "Team unregistered successfully" });
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Unauthorized attempt to unregister team {TeamId}", teamId);
            return Unauthorized(new { Message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Failed to unregister team {TeamId}", teamId);
            return BadRequest(new { Message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error unregistering team {TeamId}", teamId);
            return StatusCode(500, new { Message = "An error occurred while unregistering the team" });
        }
    }

    /// <summary>
    /// Create a new ClubTeam in the caller's club library.
    /// </summary>
    [HttpPost("create-club-team")]
    [ProducesResponseType(typeof(ClubTeamDto), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    public async Task<IActionResult> CreateClubTeam([FromBody] CreateClubTeamRequest request)
    {
        if (!IsClubRepRole())
            return StatusCode(403, new { Message = NotClubRepMessage });
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId)) return Unauthorized(new { Message = UserNotAuthenticatedMessage });

        try
        {
            var result = await _teamRegistrationService.CreateClubTeamAsync(userId, request);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { Message = ex.Message });
        }
    }

    /// <summary>
    /// Update a ClubTeam in the caller's club library.
    /// Rejected with 400 if the team has ever appeared on a schedule.
    /// </summary>
    [HttpPut("club-team/{clubTeamId:int}")]
    [ProducesResponseType(typeof(ClubTeamDto), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    public async Task<IActionResult> UpdateClubTeam(int clubTeamId, [FromBody] UpdateClubTeamRequest request)
    {
        if (!IsClubRepRole())
            return StatusCode(403, new { Message = NotClubRepMessage });
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId)) return Unauthorized(new { Message = UserNotAuthenticatedMessage });

        // BClubRepAllowEdit gate. Editing a team in the wizard is governed by the director's
        // per-event "Allow Edit" toggle for the job the rep authenticated under (their jobPath
        // claim), composed through the one capability authority so the disabled pencil and the
        // refused write agree. The eventConcluded door is the higher-level gate inside CanEditTeam:
        // a concluded event removes editing regardless of the toggle (mirrors Add/Delete).
        var jobPath = User.GetJobPath();
        var jobId = string.IsNullOrEmpty(jobPath) ? null : await _jobLookupService.GetJobIdByPathAsync(jobPath);
        if (jobId is null)
            return StatusCode(403, new { Message = "Team editing is not available in this session." });
        var caps = await _capabilities.ResolveAsync(jobId.Value, User.ToCapabilityActor());
        if (!caps.CanEditTeam)
            return StatusCode(403, new { Message = "Team editing is not enabled for this event." });

        try
        {
            var result = await _teamRegistrationService.UpdateClubTeamAsync(userId, clubTeamId, request);
            return Ok(result);
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { Message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { Message = ex.Message });
        }
    }

    /// <summary>
    /// Delete a ClubTeam from the caller's club library.
    /// Rejected with 400 if the team has ever been scheduled or is still event-registered.
    /// </summary>
    [HttpDelete("club-team/{clubTeamId:int}")]
    [ProducesResponseType(200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    public async Task<IActionResult> DeleteClubTeam(int clubTeamId)
    {
        if (!IsClubRepRole())
            return StatusCode(403, new { Message = NotClubRepMessage });
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId)) return Unauthorized(new { Message = UserNotAuthenticatedMessage });

        try
        {
            await _teamRegistrationService.DeleteClubTeamAsync(userId, clubTeamId);
            return Ok(new { Success = true });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { Message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { Message = ex.Message });
        }
    }

    /// <summary>
    /// Archive a ClubTeam (retire from visible library, retain history).
    /// Rejected with 400 if the team has never been scheduled — those should be deleted instead.
    /// </summary>
    [HttpPost("club-team/{clubTeamId:int}/archive")]
    [ProducesResponseType(typeof(ClubTeamDto), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    public async Task<IActionResult> ArchiveClubTeam(int clubTeamId)
    {
        if (!IsClubRepRole())
            return StatusCode(403, new { Message = NotClubRepMessage });
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId)) return Unauthorized(new { Message = UserNotAuthenticatedMessage });

        try
        {
            var result = await _teamRegistrationService.ArchiveClubTeamAsync(userId, clubTeamId);
            return Ok(result);
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { Message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { Message = ex.Message });
        }
    }

    /// <summary>
    /// Restore an archived ClubTeam to the visible library.
    /// </summary>
    [HttpPost("club-team/{clubTeamId:int}/unarchive")]
    [ProducesResponseType(typeof(ClubTeamDto), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    public async Task<IActionResult> UnarchiveClubTeam(int clubTeamId)
    {
        if (!IsClubRepRole())
            return StatusCode(403, new { Message = NotClubRepMessage });
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId)) return Unauthorized(new { Message = UserNotAuthenticatedMessage });

        try
        {
            var result = await _teamRegistrationService.UnarchiveClubTeamAsync(userId, clubTeamId);
            return Ok(result);
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { Message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { Message = ex.Message });
        }
    }

    /// <summary>
    /// Add a club to the current user's rep account.
    /// </summary>
    [HttpPost("add-club")]
    [ProducesResponseType(typeof(AddClubToRepResponse), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    public async Task<IActionResult> AddClubToRep([FromBody] AddClubToRepRequest request)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized(new { Message = UserNotAuthenticatedMessage });
        }

        try
        {
            var response = await _teamRegistrationService.AddClubToRepAsync(userId, request.ClubName);
            return Ok(response);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Invalid operation while adding club for user {UserId}", userId);
            return BadRequest(new { Message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding club for user {UserId}", userId);
            return StatusCode(500, new { Message = "An error occurred while adding the club" });
        }
    }

    /// <summary>
    /// Remove a club from the current user's rep account.
    /// </summary>
    [HttpDelete("remove-club")]
    [ProducesResponseType(200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    public async Task<IActionResult> RemoveClubFromRep([FromQuery] string clubName)
    {
        if (string.IsNullOrWhiteSpace(clubName))
        {
            return BadRequest(new { Message = "clubName is required" });
        }

        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized(new { Message = UserNotAuthenticatedMessage });
        }

        try
        {
            await _teamRegistrationService.RemoveClubFromRepAsync(userId, clubName);
            return Ok(new { Success = true, Message = "Club removed successfully" });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Invalid operation while removing club for user {UserId}", userId);
            return BadRequest(new { Message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing club for user {UserId}", userId);
            return StatusCode(500, new { Message = "An error occurred while removing the club" });
        }
    }

    /// <summary>
    /// Update/rename a club name for the current user's rep account.
    /// </summary>
    [HttpPatch("update-club-name")]
    [ProducesResponseType(200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    public async Task<IActionResult> UpdateClubName([FromBody] UpdateClubNameRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.OldClubName) || string.IsNullOrWhiteSpace(request.NewClubName))
        {
            return BadRequest(new { Message = "Old and new club names are required" });
        }

        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized(new { Message = UserNotAuthenticatedMessage });
        }

        try
        {
            await _teamRegistrationService.UpdateClubNameAsync(userId, request.OldClubName, request.NewClubName);
            return Ok(new { Success = true, Message = "Club name updated successfully" });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Invalid operation while updating club name for user {UserId}", userId);
            return BadRequest(new { Message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating club name for user {UserId}", userId);
            return StatusCode(500, new { Message = "An error occurred while updating the club name" });
        }
    }

    /// <summary>
    /// Recalculate team fees for all teams in a job or a specific team.
    /// Triggered by director flag changes (BAddProcessingFees, BApplyProcessingFeesToTeamDeposit, BTeamsFullPaymentRequired)
    /// or after moving a team to a different age group.
    /// </summary>
    [HttpPost("recalculate-fees")]
    [Authorize(Policy = "AdminOnly")]
    [ProducesResponseType(typeof(RecalculateTeamFeesResponse), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> RecalculateTeamFees([FromBody] RecalculateTeamFeesRequest request)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized(new { Message = UserNotAuthenticatedMessage });
        }

        if (!request.JobId.HasValue && !request.TeamId.HasValue)
        {
            return BadRequest(new { Message = "Either JobId or TeamId must be provided" });
        }

        if (request.JobId.HasValue && request.TeamId.HasValue)
        {
            return BadRequest(new { Message = "Only one of JobId or TeamId can be provided" });
        }

        try
        {
            var response = await _teamRegistrationService.RecalculateTeamFeesAsync(request, userId);
            return Ok(response);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Invalid operation while recalculating team fees for user {UserId}", userId);
            return BadRequest(new { Message = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            _logger.LogWarning(ex, "Resource not found while recalculating team fees");
            return NotFound(new { Message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error recalculating team fees for user {UserId}", userId);
            return StatusCode(500, new { Message = "An error occurred while recalculating team fees" });
        }
    }

    /// <summary>
    /// Get confirmation text with substituted variables for on-screen display.
    /// Uses AdultRegConfirmationOnScreen template from the Job.
    /// </summary>
    [HttpPost("confirmation-text")]
    [Authorize]
    [ProducesResponseType(typeof(string), 200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetConfirmationText([FromBody] GetConfirmationTextRequest request)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized(new { Message = UserNotAuthenticatedMessage });
        }

        try
        {
            var confirmationHtml = await _teamRegistrationService.GetConfirmationTextAsync(request.RegistrationId, userId);
            return Ok(confirmationHtml);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { Message = ex.Message });
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting confirmation text for registration {RegistrationId}", request.RegistrationId);
            return StatusCode(500, new { Message = "An error occurred while retrieving confirmation text" });
        }
    }

    /// <summary>
    /// Send confirmation email to club rep with substituted template.
    /// Sets bClubrep_NotificationSent flag on Registration.
    /// Uses AdultRegConfirmationEmail template from the Job.
    /// </summary>
    [HttpPost("send-confirmation-email")]
    [Authorize]
    [ProducesResponseType(200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> SendConfirmationEmail([FromBody] SendConfirmationEmailRequest request)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized(new { Message = UserNotAuthenticatedMessage });
        }

        try
        {
            await _teamRegistrationService.SendConfirmationEmailAsync(
                request.RegistrationId, userId, request.ForceResend, request.IsEcheckPending);
            return Ok(new { Message = "Confirmation email sent successfully" });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { Message = ex.Message });
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { Message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending confirmation email for registration {RegistrationId}", request.RegistrationId);
            return StatusCode(500, new { Message = "An error occurred while sending confirmation email" });
        }
    }

    /// <summary>
    /// Apply discount code to one or more teams. Validates code, applies discount,
    /// reduces processing fees proportionally, and synchronizes club rep Registration financials.
    /// </summary>
    [HttpPost("apply-discount")]
    [ProducesResponseType(typeof(ApplyTeamDiscountResponseDto), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    public async Task<IActionResult> ApplyTeamDiscount([FromBody] ApplyTeamDiscountRequestDto request)
    {
        _logger.LogInformation("ApplyTeamDiscount invoked: jobPath={JobPath} code={Code} teams={TeamCount}",
            request?.JobPath, request?.Code, request?.TeamIds?.Count);

        if (request == null || string.IsNullOrWhiteSpace(request.Code) || request.TeamIds == null || !request.TeamIds.Any() || string.IsNullOrWhiteSpace(request.JobPath))
        {
            return BadRequest(new { message = "Invalid request" });
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return Unauthorized();

        var jobId = await _jobLookupService.GetJobIdByPathAsync(request.JobPath);
        if (jobId is null)
            return NotFound(new { message = $"Job not found: {request.JobPath}" });

        // Discount-code start/end dates are stored in local AZ server time, not UTC.
        var now = DateTime.Now;
        var codeLower = request.Code.Trim().ToLowerInvariant();
        var discountCodeRecord = await _discountCodeRepo.GetActiveCodeAsync(jobId.Value, codeLower, now);

        if (discountCodeRecord == null)
        {
            return Ok(new ApplyTeamDiscountResponseDto
            {
                Success = false,
                Message = "Invalid or expired discount code",
                TotalTeamsProcessed = 0,
                SuccessCount = 0,
                FailureCount = 0,
                Results = new List<TeamDiscountResult>()
            });
        }

        var (discountCodeAi, bAsPercent, codeAmount) = discountCodeRecord.Value;
        var amount = codeAmount ?? 0m;
        if (amount <= 0m)
        {
            return Ok(new ApplyTeamDiscountResponseDto
            {
                Success = false,
                Message = "Discount code has no discount amount",
                TotalTeamsProcessed = 0,
                SuccessCount = 0,
                FailureCount = 0,
                Results = new List<TeamDiscountResult>()
            });
        }

        // Discount-code use is gated PER TEAM only (team.DiscountCodeId, in ProcessSingleTeamDiscountAsync),
        // not once-per-club-rep: a rep may apply a code to each of their teams — even across separate
        // sessions — while each individual team is still discounted at most once. Per-team soft rejections
        // (already coded / not found / $0 discount) are non-fatal: they ride back as Success=false rows in
        // Results[] while the other teams still apply and commit.
        try
        {
            var response = await ProcessTeamDiscountsAsync(request.TeamIds, bAsPercent ?? false, amount, discountCodeAi, jobId.Value, userId);

            _logger.LogInformation("ApplyTeamDiscount completed: success={Success} processed={Processed} succeeded={Succeeded} failed={Failed}",
                response.Success, response.TotalTeamsProcessed, response.SuccessCount, response.FailureCount);

            return Ok(response);
        }
        catch (Exception ex)
        {
            // A hard failure mid-batch (e.g. proc-fee adjust, SaveChanges, or club-rep sync throws) escapes
            // the TransactionScope without Complete(), so the whole batch rolls back — NO team is left
            // half-discounted. Surface that as a clean, logged 500 instead of a bare framework error.
            _logger.LogError(ex, "ApplyTeamDiscount failed for code={Code} teams={TeamCount}; transaction rolled back, no discounts applied",
                request.Code, request.TeamIds.Count);
            return StatusCode(500, new { message = "An error occurred applying the discount. No changes were made." });
        }
    }

    private async Task<ApplyTeamDiscountResponseDto> ProcessTeamDiscountsAsync(
        List<Guid> teamIds,
        bool bAsPercent,
        decimal amount,
        int discountCodeId,
        Guid jobId,
        string userId)
    {
        var results = new List<TeamDiscountResult>();
        Guid? clubRepRegistrationId = null;

        using (var scope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled))
        {
            foreach (var teamId in teamIds)
            {
                var result = await ProcessSingleTeamDiscountAsync(teamId, bAsPercent, amount, discountCodeId, jobId, userId);
                if (result != null)
                {
                    results.Add(result);
                    if (result.Success && clubRepRegistrationId == null)
                    {
                        var team = await _teamRepository.GetTeamFromTeamId(teamId);
                        if (team?.ClubrepRegistrationid.HasValue ?? false)
                        {
                            clubRepRegistrationId = team.ClubrepRegistrationid.Value;
                        }
                    }
                }
            }

            await _teamRepository.SaveChangesAsync();

            if (clubRepRegistrationId.HasValue)
            {
                // Roll the per-team discounts up into the club-rep registration financials. The redeemed
                // code is recorded PER TEAM (team.DiscountCodeId, set in ProcessSingleTeamDiscountAsync);
                // it is deliberately NOT stamped on the club-rep reg — that stamp used to enforce the
                // retired one-code-per-club-rep rule, and gating is now purely per team.
                await _registrationRepository.SynchronizeClubRepFinancialsAsync(clubRepRegistrationId.Value, userId);
            }

            scope.Complete();
        }

        var successCount = results.Count(r => r.Success);
        var failureCount = results.Count(r => !r.Success);

        return new ApplyTeamDiscountResponseDto
        {
            Success = successCount > 0,
            Message = successCount > 0 ? $"Successfully applied discount to {successCount} team(s)" : "No discounts were applied",
            TotalTeamsProcessed = results.Count,
            SuccessCount = successCount,
            FailureCount = failureCount,
            Results = results
        };
    }

    private async Task<TeamDiscountResult?> ProcessSingleTeamDiscountAsync(
        Guid teamId,
        bool bAsPercent,
        decimal amount,
        int discountCodeId,
        Guid jobId,
        string userId)
    {
        var team = await _teamRepository.GetTeamFromTeamId(teamId);
        if (team == null)
        {
            return new TeamDiscountResult
            {
                TeamId = teamId,
                TeamName = UnknownTeamName,
                Success = false,
                Message = "Team not found",
                DiscountCodeId = null
            };
        }

        if (team.DiscountCodeId != null)
        {
            return new TeamDiscountResult
            {
                TeamId = teamId,
                TeamName = team.TeamName ?? UnknownTeamName,
                Success = false,
                Message = "Discount already applied to this team",
                DiscountCodeId = team.DiscountCodeId
            };
        }

        // The discount code is the LAST modifier, rated against what is owed AT CHECKOUT — not the
        // full team burden. "The bill" = base net of every discount ALREADY stamped (TotalDiscount() =
        // early-bird + any multi-player discount — the same total FeeMath subtracts), plus any late
        // fee, MINUS the principal already paid (e.g. a deposit settled in an earlier session). Rating
        // against the full FeeBase would discount the whole pre-adjustment price (the way VerticalInsure
        // rates against the total potential bill) — so a 50% code on a team that already paid its
        // deposit would take 50% of deposit+balance instead of 50% of the balance still due,
        // over-discounting the paid portion. Netting only FeeDiscount would likewise over-discount by a
        // share of the sibling discount. Voluntary donations (FeeDonation) are intentionally excluded:
        // a code never discounts a charitable add-on. The code's dollars stack onto FeeDiscount (never
        // FeeDiscountMp — provenance) below. PrincipalPaid (not gross PaidTotal) so proc already
        // collected on the deposit does not eat into the discountable principal.
        var state = await _paymentState.ForTeamAsync(teamId, jobId);
        var netBill = (team.FeeBase ?? 0m) - team.TotalDiscount() + (team.FeeLatefee ?? 0m);
        var owedBasis = Math.Max(0m, netBill - state.PrincipalPaid);
        var discountAmount = DiscountCalculator.Calculate(owedBasis, amount, bAsPercent);

        if (discountAmount <= 0m)
        {
            return new TeamDiscountResult
            {
                TeamId = teamId,
                TeamName = team.TeamName ?? UnknownTeamName,
                Success = false,
                Message = "No discount applicable",
                DiscountCodeId = null
            };
        }

        team.DiscountCodeId = discountCodeId;
        var currentDiscount = team.FeeDiscount ?? 0m;
        team.FeeDiscount = currentDiscount + discountAmount;

        await _feeAdjustment.ReduceTeamProcessingFeeProportionalAsync(team, discountAmount, jobId, userId);

        team.RecalcTotals();
        team.Modified = DateTime.Now;
        team.LebUserId = userId;

        // A discount is a fee modifier, not a payment: it reduces FeeTotal and is recorded on the team
        // (DiscountCodeId, set above) — it never writes a RegistrationAccounting row or PaidTotal. (A 100%
        // DC used to stamp a fake Correction Payamt + PaidTotal +=, double-booking the discount and —
        // under signed OwedTotal — surfacing a phantom -discount.)

        return new TeamDiscountResult
        {
            TeamId = teamId,
            TeamName = team.TeamName ?? UnknownTeamName,
            Success = true,
            Message = $"Discount applied: {discountAmount:C}",
            DiscountCodeId = discountCodeId
        };
    }
}

public class GetConfirmationTextRequest
{
    public Guid RegistrationId { get; set; }
}

public class SendConfirmationEmailRequest
{
    public Guid RegistrationId { get; set; }
    public bool ForceResend { get; set; } = false;
    /// <summary>
    /// When true, the email body is prefixed with a "paid by eCheck" banner that sets
    /// the 3–5 business day bank-draft expectation. Set this to true ONLY when the just-
    /// completed payment was an eCheck (ACH) submission.
    /// </summary>
    public bool IsEcheckPending { get; set; } = false;
}