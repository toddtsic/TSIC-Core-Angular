using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
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
    private readonly IMemoryCache _cache;

    // Anonymous username-availability is an enumeration surface. Cap per-IP so legit
    // debounced typing flows freely but bulk harvesting from one host is slowed. It's a
    // speed bump, not an auth control — CreateAsync is the authoritative uniqueness gate,
    // and the client treats a 429 as "unknown" (fail-open), so throttling costs nothing.
    private static readonly TimeSpan UsernameCheckWindow = TimeSpan.FromMinutes(1);
    private const int MaxUsernameChecksPerWindow = 60;

    public AccountController(IUserProfileService profileService, IMemoryCache cache)
    {
        _profileService = profileService;
        _cache = cache;
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

    /// <summary>
    /// Anonymous pre-check the registration wizards call so a user learns a username is
    /// taken BEFORE filling out the whole form. Advisory only: the authoritative gate is
    /// <c>UserManager.CreateAsync</c> at account creation. Per-IP throttled; a 429 means
    /// "unknown" to the client, which then simply doesn't block (fail-open).
    /// </summary>
    [AllowAnonymous]
    [HttpGet("username-available")]
    [ProducesResponseType(typeof(UsernameAvailabilityResponse), 200)]
    [ProducesResponseType(429)]
    public async Task<ActionResult<UsernameAvailabilityResponse>> CheckUsernameAvailable([FromQuery] string username)
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var throttleKey = $"uname-avail:{ip}";
        var recent = _cache.TryGetValue(throttleKey, out int n) ? n : 0;
        if (recent >= MaxUsernameChecksPerWindow)
        {
            return StatusCode(StatusCodes.Status429TooManyRequests);
        }
        _cache.Set(throttleKey, recent + 1, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = UsernameCheckWindow
        });

        var available = await _profileService.IsUsernameAvailableAsync(username);
        return Ok(new UsernameAvailabilityResponse { Available = available });
    }
}
