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
    private readonly ITeamRegistrationService _teamRegistrationService;
    private readonly ILogger<TeamRegistrationController> _logger;

    public TeamRegistrationController(
        ITeamRegistrationService teamRegistrationService,
        ILogger<TeamRegistrationController> logger)
    {
        _teamRegistrationService = teamRegistrationService;
        _logger = logger;
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
            return Unauthorized(new { Message = "User not authenticated" });
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
            return Unauthorized(new { Message = "User not authenticated" });
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
    /// </summary>
    [HttpGet("metadata")]
    [ProducesResponseType(typeof(TeamsMetadataResponse), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    public async Task<IActionResult> GetMetadata(
        [FromQuery] string jobPath, 
        [FromQuery] string clubName, 
        [FromQuery] bool bPayBalanceDue = false)
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
            return Unauthorized(new { Message = "User not authenticated" });
        }

        try
        {
            var response = await _teamRegistrationService.GetTeamsMetadataAsync(jobPath, userId, clubName, bPayBalanceDue);
            return Ok(response);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Failed to get teams metadata for user {UserId} and job {JobPath}", userId, jobPath);
            return BadRequest(new { Message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting teams metadata for user {UserId} and job {JobPath}", userId, jobPath);
            return StatusCode(500, new { Message = "An error occurred while retrieving team data" });
        }
    }

    /// <summary>
    /// Register a ClubTeam for the current event.
    /// </summary>
    [HttpPost("register-team")]
    [ProducesResponseType(typeof(RegisterTeamResponse), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    public async Task<IActionResult> RegisterTeam([FromBody] RegisterTeamRequest request)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized(new { Message = "User not authenticated" });
        }

        // Extract clubId from JWT token (for ClubRep users)
        var clubIdClaim = User.FindFirst("clubId")?.Value;
        int? clubId = null;
        if (!string.IsNullOrEmpty(clubIdClaim) && int.TryParse(clubIdClaim, out var parsedClubId))
        {
            clubId = parsedClubId;
        }

        try
        {
            var response = await _teamRegistrationService.RegisterTeamForEventAsync(request, userId, clubId);
            if (!response.Success)
            {
                return BadRequest(response);
            }
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error registering team for user {UserId}", userId);
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
            return Unauthorized(new { Message = "User not authenticated" });
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
            return Unauthorized(new { Message = "User not authenticated" });
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
            return Unauthorized(new { Message = "User not authenticated" });
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
}
