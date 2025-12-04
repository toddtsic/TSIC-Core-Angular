using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using TSIC.API.Dtos;
using TSIC.API.Services;

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
    /// Get teams metadata for the current club and event.
    /// </summary>
    [HttpGet("metadata")]
    [ProducesResponseType(typeof(TeamsMetadataResponse), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    public async Task<IActionResult> GetMetadata([FromQuery] string jobPath)
    {
        if (string.IsNullOrWhiteSpace(jobPath))
        {
            return BadRequest(new { Message = "jobPath is required" });
        }

        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized(new { Message = "User not authenticated" });
        }

        try
        {
            var response = await _teamRegistrationService.GetTeamsMetadataAsync(jobPath, userId);
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
}
