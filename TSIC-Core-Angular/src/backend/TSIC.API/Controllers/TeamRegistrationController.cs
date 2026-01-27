using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.Transactions;
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
using TSIC.API.Services.Shared.Jobs;
using TSIC.API.Services.Shared.VerticalInsure;
using TSIC.API.Services.Auth;
using TSIC.API.Services.Email;
using TSIC.API.Services.Shared.UsLax;

namespace TSIC.API.Controllers;

[ApiController]
[Route("api/team-registration")]
[Authorize]
public class TeamRegistrationController : ControllerBase
{
    private const string UserNotAuthenticatedMessage = "User not authenticated";
    private const string UnknownTeamName = "Unknown";

    private readonly ITeamRegistrationService _teamRegistrationService;
    private readonly ILogger<TeamRegistrationController> _logger;
    private readonly IJobLookupService _jobLookupService;
    private readonly IJobDiscountCodeRepository _discountCodeRepo;
    private readonly ITeamRepository _teamRepository;
    private readonly IRegistrationRepository _registrationRepository;
    private readonly IRegistrationFeeAdjustmentService _feeAdjustment;

    public TeamRegistrationController(
        ITeamRegistrationService teamRegistrationService,
        ILogger<TeamRegistrationController> logger,
        IJobLookupService jobLookupService,
        IJobDiscountCodeRepository discountCodeRepo,
        ITeamRepository teamRepository,
        IRegistrationRepository registrationRepository,
        IRegistrationFeeAdjustmentService feeAdjustment)
    {
        _teamRegistrationService = teamRegistrationService;
        _logger = logger;
        _jobLookupService = jobLookupService;
        _discountCodeRepo = discountCodeRepo;
        _teamRepository = teamRepository;
        _registrationRepository = registrationRepository;
        _feeAdjustment = feeAdjustment;
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
        {
            return Unauthorized(new { Message = UserNotAuthenticatedMessage });
        }

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
        {
            return Unauthorized(new { Message = UserNotAuthenticatedMessage });
        }

        try
        {
            var response = await _teamRegistrationService.InitializeRegistrationAsync(userId, request.ClubName, request.JobPath);
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
        // Extract regId from token
        var regIdClaim = User.FindFirst("regId")?.Value;
        if (string.IsNullOrEmpty(regIdClaim) || !Guid.TryParse(regIdClaim, out var regId))
        {
            return Unauthorized(new { Message = "Registration ID not found in token. Please select a club first." });
        }

        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized(new { Message = UserNotAuthenticatedMessage });
        }

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
        // Extract regId from token
        var regIdClaim = User.FindFirst("regId")?.Value;
        if (string.IsNullOrEmpty(regIdClaim) || !Guid.TryParse(regIdClaim, out var regId))
        {
            return Unauthorized(new { Message = "Registration ID not found in token. Please select a club first." });
        }

        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized(new { Message = UserNotAuthenticatedMessage });
        }

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
        try
        {
            await _teamRegistrationService.UnregisterTeamFromEventAsync(teamId);
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
        catch (UnauthorizedAccessException ex)
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
            await _teamRegistrationService.SendConfirmationEmailAsync(request.RegistrationId, userId, request.ForceResend);
            return Ok(new { Message = "Confirmation email sent successfully" });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { Message = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
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

        var now = DateTime.UtcNow;
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

        var (bAsPercent, codeAmount) = discountCodeRecord.Value;
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

        var response = await ProcessTeamDiscountsAsync(request.TeamIds, bAsPercent ?? false, amount, 0, jobId.Value, userId);

        _logger.LogInformation("ApplyTeamDiscount completed: success={Success} processed={Processed} succeeded={Succeeded} failed={Failed}",
            response.Success, response.TotalTeamsProcessed, response.SuccessCount, response.FailureCount);

        return Ok(response);
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

        var discountAmount = CalculateDiscountAmount(bAsPercent, amount, team.FeeBase ?? 0m);

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

        team.FeeTotal = (team.FeeBase ?? 0m) - (team.FeeDiscount ?? 0m) + (team.FeeProcessing ?? 0m) + (team.FeeDonation ?? 0m) + (team.FeeLatefee ?? 0m);
        team.OwedTotal = Math.Max(0m, (team.FeeTotal ?? 0m) - (team.PaidTotal ?? 0m));
        team.Modified = DateTime.UtcNow;
        team.LebUserId = userId;

        return new TeamDiscountResult
        {
            TeamId = teamId,
            TeamName = team.TeamName ?? UnknownTeamName,
            Success = true,
            Message = $"Discount applied: {discountAmount:C}",
            DiscountCodeId = discountCodeId
        };
    }

    private static decimal CalculateDiscountAmount(bool bAsPercent, decimal amount, decimal feeBase)
    {
        if (bAsPercent)
        {
            var pct = amount / 100m;
            return Math.Round(feeBase * pct, 2, MidpointRounding.AwayFromZero);
        }

        return amount;
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
}