using Microsoft.AspNetCore.Mvc;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Services;

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
    /// Toggle team subscription for push notifications.
    /// If currently subscribed → unsubscribes. If not → subscribes.
    /// Returns the updated list of subscribed team IDs for this device + job.
    /// </summary>
    [HttpPost("subscribe-team")]
    [ProducesResponseType(typeof(ToggleTeamSubscriptionResponse), 200)]
    [ProducesResponseType(400)]
    public async Task<IActionResult> ToggleTeamSubscription(
        [FromBody] ToggleTeamSubscriptionRequest request,
        [FromQuery] Guid jobId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.DeviceToken))
            return BadRequest(new { Error = "DeviceToken is required" });

        if (jobId == Guid.Empty)
            return BadRequest(new { Error = "jobId query parameter is required" });

        var response = await _deviceService.ToggleTeamSubscriptionAsync(request, jobId, ct);
        return Ok(response);
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
