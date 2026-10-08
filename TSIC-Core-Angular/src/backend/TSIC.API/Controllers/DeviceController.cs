using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TSIC.API.Extensions;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Services;
using TSIC.Domain.Constants;

namespace TSIC.API.Controllers;

/// <summary>
/// Device management for mobile push notification registration and team subscriptions.
/// All endpoints are anonymous — device token is the identity (no auth required for device ops).
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class DeviceController : ControllerBase
{
    private readonly IDeviceManagementService _deviceService;

    public DeviceController(IDeviceManagementService deviceService)
    {
        _deviceService = deviceService;
    }

    /// <summary>
    /// Files this device against the registration and team the caller logged in as.
    ///
    /// The only authenticated action on this controller -- the rest are anonymous, with the
    /// device token as identity. Here the bearer IS the point: the registration comes from
    /// its regId claim, the team from that registration, and both are deliberately absent
    /// from the request body, because an endpoint that accepted them would let any
    /// authenticated user subscribe their phone to any team. A bearer without a regId is the
    /// phase-one token issued before a role is picked; it names nothing to file.
    ///
    /// Idempotent; the client calls it on every launch and every token event.
    /// </summary>
    [Authorize]
    [HttpPost("sync")]
    [ProducesResponseType(typeof(SyncDeviceResponse), 200)]
    [ProducesResponseType(401)]
    public async Task<IActionResult> Sync(
        [FromBody] SyncDeviceRequest request, CancellationToken ct)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var regId = User.GetRegistrationId();
        if (regId == null) return Unauthorized();

        var result = await _deviceService.SyncDeviceAsync(userId, regId.Value, request, ct);
        return Ok(result);
    }

    /// <summary>
    /// Register a device for push notifications on a specific job.
    /// Call this when the app opens on a new device or when the FCM token refreshes.
    /// </summary>
    [HttpPost("register")]
    [ProducesResponseType(200)]
    [ProducesResponseType(400)]
    public async Task<IActionResult> RegisterDevice(
        [FromBody] RegisterDeviceRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.DeviceToken))
            return BadRequest(new { Error = "DeviceToken is required" });

        await _deviceService.RegisterDeviceAsync(request, ct);
        return Ok(new { Message = "Device registered" });
    }

    /// <summary>
    /// Toggle team subscription (heart) for push notifications.
    /// If currently subscribed → unsubscribes. If not → subscribes.
    /// Returns the updated list of subscribed team IDs for this device + job.
    ///
    /// Anonymous, with ONE branch on the bearer. Both apps call this. The TSIC-Events app
    /// sends no bearer, or a Scorer's; either way the row is written with a null
    /// RegistrationId - the Events heart, exactly as before. Any other VALID bearer is a
    /// TSIC-Teams login (Player, Staff, Director, Superuser all use that app), and the row is
    /// stamped with its regId so it lands in the Teams push pool and is sent through the Teams
    /// Firebase project. Role is the discriminator because Scorer is the only role the Events
    /// app ever bears.
    ///
    /// A bearer that FAILED validation (expired Teams login) must not fall through to the
    /// anonymous path: that would write an Events heart the Teams app then shows as set while
    /// no Teams push ever arrives. It is decoded UNVERIFIED, for the role only, and a non-Scorer
    /// gets 401 so the app refreshes and retries. A Scorer's expired bearer keeps the anonymous
    /// path, because the Events app's interceptor clears its session on any 401 and a heart tap
    /// must never log a scorer out. The stamp itself only ever comes from a validated bearer.
    /// </summary>
    [HttpPost("subscribe-team")]
    [ProducesResponseType(typeof(ToggleTeamSubscriptionResponse), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    public async Task<IActionResult> ToggleTeamSubscription(
        [FromBody] ToggleTeamSubscriptionRequest request,
        [FromQuery] Guid jobId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.DeviceToken))
            return BadRequest(new { Error = "DeviceToken is required" });

        if (jobId == Guid.Empty)
            return BadRequest(new { Error = "jobId query parameter is required" });

        Guid? registrationId = null;
        if (User.Identity?.IsAuthenticated == true)
        {
            if (User.FindFirst(ClaimTypes.Role)?.Value != RoleConstants.Names.ScorerName)
                registrationId = User.GetRegistrationId();
        }
        else if (BearerIsAnUnvalidatedTeamsLogin(Request.Headers.Authorization))
        {
            return Unauthorized();
        }

        var response = await _deviceService.ToggleTeamSubscriptionAsync(request, jobId, registrationId, ct);
        return Ok(response);
    }

    /// <summary>
    /// True when an Authorization header carries a JWT that did not authenticate and whose
    /// (unverified) role claim is anything but Scorer. Reads the token WITHOUT validating it,
    /// and the answer is used for nothing but choosing 401 over the anonymous path - never for
    /// identity, never for the stamp. A header that is absent, not a bearer, or not decodable
    /// is treated as anonymous.
    /// </summary>
    private static bool BearerIsAnUnvalidatedTeamsLogin(string? authorization)
    {
        if (string.IsNullOrWhiteSpace(authorization)) return false;
        const string prefix = "Bearer ";
        if (!authorization.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;

        var raw = authorization[prefix.Length..].Trim();
        var handler = new JwtSecurityTokenHandler();
        if (!handler.CanReadToken(raw)) return false;

        try
        {
            var jwt = handler.ReadJwtToken(raw);
            // TokenService writes ClaimTypes.Role; the handler's outbound map shortens it to
            // "role" in the wire token. Accept either spelling.
            var role = jwt.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Role || c.Type == "role")?.Value;
            return role != null && role != RoleConstants.Names.ScorerName;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Swap an old device token for a new one (phone upgrade, FCM token rotation).
    /// All existing subscriptions and registrations transfer to the new token.
    /// </summary>
    [HttpPost("swap-token")]
    [ProducesResponseType(200)]
    [ProducesResponseType(400)]
    public async Task<IActionResult> SwapToken(
        [FromBody] SwapDeviceTokenRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.OldDeviceToken) || string.IsNullOrWhiteSpace(request.NewDeviceToken))
            return BadRequest(new { Error = "Both OldDeviceToken and NewDeviceToken are required" });

        await _deviceService.SwapTokenAsync(request, ct);
        return Ok(new { Message = "Device token swapped" });
    }

    /// <summary>
    /// Get all team IDs this device is subscribed to for a specific job.
    /// Used to restore favorite team state when the app opens.
    /// </summary>
    [HttpGet("subscriptions/{jobId:guid}")]
    [ProducesResponseType(typeof(List<Guid>), 200)]
    [ProducesResponseType(400)]
    public async Task<IActionResult> GetSubscriptions(
        Guid jobId, [FromQuery] string deviceToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(deviceToken))
            return BadRequest(new { Error = "deviceToken query parameter is required" });

        var teamIds = await _deviceService.GetSubscribedTeamIdsAsync(deviceToken, jobId, ct);
        return Ok(teamIds);
    }
}
