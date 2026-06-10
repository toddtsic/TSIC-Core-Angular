using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Services;

namespace TSIC.API.Controllers;

/// <summary>
/// Role-neutral self-service account endpoints for the authenticated user.
/// "me" always resolves from the JWT subject — never a route/body id — so any
/// signed-in user can read and update their own profile.
/// </summary>
[ApiController]
[Route("api/account")]
[Authorize]
public class AccountController : ControllerBase
{
    private readonly IUserProfileService _profileService;

    public AccountController(IUserProfileService profileService)
    {
        _profileService = profileService;
    }

    [HttpGet("me")]
    [ProducesResponseType(typeof(UserProfileDto), 200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetMyProfile()
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized(new { Message = "User not authenticated" });
        }

        var profile = await _profileService.GetSelfProfileAsync(userId);
        if (profile == null)
        {
            return NotFound();
        }

        return Ok(profile);
    }

    [HttpPut("me")]
    [ProducesResponseType(204)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    public async Task<IActionResult> UpdateMyProfile([FromBody] UserProfileUpdateRequest request)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized(new { Message = "User not authenticated" });
        }

        var success = await _profileService.UpdateSelfProfileAsync(userId, request);
        if (!success)
        {
            return BadRequest(new { Message = "Failed to update profile" });
        }

        return NoContent();
    }
}
