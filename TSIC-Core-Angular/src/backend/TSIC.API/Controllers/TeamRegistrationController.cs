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

        try
        {
            var response = await _teamRegistrationService.RegisterTeamForEventAsync(request, userId);
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

    // ============================================================
    // CLUB TEAM MANAGEMENT ENDPOINTS
    // ============================================================

    /// <summary>
    /// Get all club teams (active + inactive) for management.
    /// </summary>
    [HttpGet("clubs/{clubName}/management")]
    [Authorize(Policy = "ClubRepOnly")]
    [ProducesResponseType(typeof(List<ClubTeamManagementDto>), 200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(400)]
    public async Task<IActionResult> GetClubTeamsForManagement(string clubName)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized(new { Message = "User not authenticated" });
        }

        try
        {
            var teams = await _teamRegistrationService.GetClubTeamsForManagementAsync(userId, clubName);
            return Ok(teams);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Invalid operation getting teams for management. User: {UserId}, Club: {Club}", userId, clubName);
            return BadRequest(new { Message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting teams for management. User: {UserId}, Club: {Club}", userId, clubName);
            return StatusCode(500, new { Message = "An error occurred while retrieving teams" });
        }
    }

    /// <summary>
    /// Inactivate a club team (soft delete). Can be reactivated later for year rollover.
    /// </summary>
    [HttpPatch("teams/{clubTeamId}/inactivate")]
    [Authorize(Policy = "ClubRepOnly")]
    [ProducesResponseType(typeof(ClubTeamOperationResponse), 200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(400)]
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
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Invalid operation inactivating team {TeamId} for user {UserId}", clubTeamId, userId);
            return BadRequest(new { Message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error inactivating team {TeamId} for user {UserId}", clubTeamId, userId);
            return StatusCode(500, new { Message = "An error occurred while inactivating the team" });
        }
    }

    /// <summary>
    /// Activate a club team (restore from inactive). Used for year rollover.
    /// </summary>
    [HttpPatch("teams/{clubTeamId}/activate")]
    [Authorize(Policy = "ClubRepOnly")]
    [ProducesResponseType(typeof(ClubTeamOperationResponse), 200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(400)]
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
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Invalid operation activating team {TeamId} for user {UserId}", clubTeamId, userId);
            return BadRequest(new { Message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error activating team {TeamId} for user {UserId}", clubTeamId, userId);
            return StatusCode(500, new { Message = "An error occurred while activating the team" });
        }
    }

    /// <summary>
    /// Delete a club team permanently. Only allowed if team has never been used.
    /// </summary>
    [HttpDelete("teams/{clubTeamId}")]
    [Authorize(Policy = "ClubRepOnly")]
    [ProducesResponseType(typeof(ClubTeamOperationResponse), 200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(400)]
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
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Invalid operation deleting team {TeamId} for user {UserId}", clubTeamId, userId);
            return BadRequest(new { Message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting team {TeamId} for user {UserId}", clubTeamId, userId);
            return StatusCode(500, new { Message = "An error occurred while deleting the team" });
        }
    }
}
