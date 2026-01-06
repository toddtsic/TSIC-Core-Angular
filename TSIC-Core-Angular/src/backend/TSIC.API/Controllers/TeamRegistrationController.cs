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
    public async Task<IActionResult> GetMetadata([FromQuery] string jobPath, [FromQuery] string clubName)
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
            var response = await _teamRegistrationService.GetTeamsMetadataAsync(jobPath, userId, clubName);
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
        {
            return Unauthorized(new { Message = "User not authenticated" });
        }

        try
        {
            await _teamRegistrationService.UnregisterTeamFromEventAsync(teamId, userId);
            return Ok(new { Success = true, Message = "Team unregistered successfully" });
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Unauthorized attempt to unregister team {TeamId} by user {UserId}", teamId, userId);
            return Unauthorized(new { Message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Failed to unregister team {TeamId} for user {UserId}", teamId, userId);
            return BadRequest(new { Message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error unregistering team {TeamId} for user {UserId}", teamId, userId);
            return StatusCode(500, new { Message = "An error occurred while unregistering the team" });
        }
    }

    /// <summary>
    /// Add a new ClubTeam to the club.
    /// </summary>
    [HttpPost("add-club-team")]
    [ProducesResponseType(typeof(AddClubTeamResponse), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    public async Task<IActionResult> AddClubTeam([FromBody] AddClubTeamRequest request)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized(new { Message = "User not authenticated" });
        }

        try
        {
            var response = await _teamRegistrationService.AddNewClubTeamAsync(request, userId);
            if (!response.Success)
            {
                return BadRequest(response);
            }
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding club team for user {UserId}", userId);
            return StatusCode(500, new { Message = "An error occurred while adding the club team" });
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
    /// Get all club teams for all clubs the user is a rep for.
    /// </summary>
    [HttpGet("club-library-teams")]
    [ProducesResponseType(typeof(List<ClubTeamManagementDto>), 200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    public async Task<IActionResult> GetClubTeams()
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized(new { Message = "User not authenticated" });
        }

        try
        {
            var teams = await _teamRegistrationService.GetClubTeamsAsync(userId);
            return Ok(teams);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Unauthorized access attempt by user {UserId}", userId);
            return StatusCode(403, new { Message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Invalid operation while getting teams for user {UserId}", userId);
            return BadRequest(new { Message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting teams for user {UserId}", userId);
            return StatusCode(500, new { Message = "An error occurred while retrieving club teams" });
        }
    }

    /// <summary>
    /// Update a club team. Conditional logic based on registration history.
    /// </summary>
    [HttpPut("club-team/{clubTeamId}")]
    [ProducesResponseType(typeof(ClubTeamOperationResponse), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    public async Task<IActionResult> UpdateClubTeam(int clubTeamId, [FromBody] UpdateClubTeamRequest request)
    {
        if (request == null)
        {
            return BadRequest(new { Message = "Request body is required" });
        }

        if (request.ClubTeamId != clubTeamId)
        {
            return BadRequest(new { Message = "ClubTeamId in URL does not match request body" });
        }

        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized(new { Message = "User not authenticated" });
        }

        try
        {
            var result = await _teamRegistrationService.UpdateClubTeamAsync(request, userId);
            return Ok(result);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Unauthorized attempt to update team {ClubTeamId} by user {UserId}", clubTeamId, userId);
            return StatusCode(403, new { Message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Failed to update team {ClubTeamId} for user {UserId}", clubTeamId, userId);
            return BadRequest(new { Message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating team {ClubTeamId} for user {UserId}", clubTeamId, userId);
            return StatusCode(500, new { Message = "An error occurred while updating the team" });
        }
    }

    /// <summary>
    /// Activate a club team.
    /// </summary>
    [HttpPatch("club-team/{clubTeamId}/activate")]
    [ProducesResponseType(typeof(ClubTeamOperationResponse), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    public async Task<IActionResult> ActivateClubTeam(int clubTeamId)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized(new { Message = "User not authenticated" });
        }

        try
        {
            var result = await _teamRegistrationService.ActivateClubTeamAsync(clubTeamId, userId);
            return Ok(result);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Unauthorized attempt to activate team {ClubTeamId} by user {UserId}", clubTeamId, userId);
            return StatusCode(403, new { Message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Failed to activate team {ClubTeamId} for user {UserId}", clubTeamId, userId);
            return BadRequest(new { Message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error activating team {ClubTeamId} for user {UserId}", clubTeamId, userId);
            return StatusCode(500, new { Message = "An error occurred while activating the team" });
        }
    }

    /// <summary>
    /// Inactivate a club team.
    /// </summary>
    [HttpPatch("club-team/{clubTeamId}/inactivate")]
    [ProducesResponseType(typeof(ClubTeamOperationResponse), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    public async Task<IActionResult> InactivateClubTeam(int clubTeamId)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized(new { Message = "User not authenticated" });
        }

        try
        {
            var result = await _teamRegistrationService.InactivateClubTeamAsync(clubTeamId, userId);
            return Ok(result);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Unauthorized attempt to inactivate team {ClubTeamId} by user {UserId}", clubTeamId, userId);
            return StatusCode(403, new { Message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Failed to inactivate team {ClubTeamId} for user {UserId}", clubTeamId, userId);
            return BadRequest(new { Message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error inactivating team {ClubTeamId} for user {UserId}", clubTeamId, userId);
            return StatusCode(500, new { Message = "An error occurred while inactivating the team" });
        }
    }

    /// <summary>
    /// Delete a club team. Smart delete: soft delete if registered, hard delete if never used.
    /// </summary>
    [HttpDelete("club-team/{clubTeamId}")]
    [ProducesResponseType(typeof(ClubTeamOperationResponse), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    public async Task<IActionResult> DeleteClubTeam(int clubTeamId)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized(new { Message = "User not authenticated" });
        }

        try
        {
            var result = await _teamRegistrationService.DeleteClubTeamAsync(clubTeamId, userId);
            return Ok(result);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Unauthorized attempt to delete team {ClubTeamId} by user {UserId}", clubTeamId, userId);
            return StatusCode(403, new { Message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Failed to delete team {ClubTeamId} for user {UserId}", clubTeamId, userId);
            return BadRequest(new { Message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting team {ClubTeamId} for user {UserId}", clubTeamId, userId);
            return StatusCode(500, new { Message = "An error occurred while deleting the team" });
        }
    }
}
