using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TSIC.API.Extensions;
using TSIC.API.Services.Shared.Jobs;
using TSIC.Contracts.Dtos.PushNotification;
using TSIC.Contracts.Services;

namespace TSIC.API.Controllers;

/// <summary>
/// Admin endpoints for sending push notifications to all mobile users for a job.
/// Replaces legacy JobPushNotification/Index.
/// </summary>
[ApiController]
[Route("api/push-notifications")]
[Authorize(Policy = "AdminOnly")]
public class PushNotificationController : ControllerBase
{
    private readonly IPushNotificationService _service;
    private readonly IJobLookupService _jobLookupService;

    public PushNotificationController(
        IPushNotificationService service,
        IJobLookupService jobLookupService)
    {
        _service = service;
        _jobLookupService = jobLookupService;
    }

    /// <summary>
    /// Get the count of mobile devices registered for push notifications for the current job.
    /// </summary>
    [HttpGet("device-count")]
    [ProducesResponseType(typeof(PushNotificationDeviceCountDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PushNotificationDeviceCountDto>> GetDeviceCount(
        CancellationToken ct)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null)
            return BadRequest(new { message = "Registration context required." });

        var count = await _service.GetDeviceCountForJobAsync(jobId.Value, ct);

        return Ok(new PushNotificationDeviceCountDto { DeviceCount = count });
    }

    /// <summary>
    /// Send a push notification to ALL mobile devices registered for the current job.
    /// </summary>
    [HttpPost("send")]
    [ProducesResponseType(typeof(SendPushNotificationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<SendPushNotificationResponse>> SendPushNotification(
        [FromBody] SendPushNotificationRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.PushText))
            return BadRequest(new { message = "Push text is required." });

        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null)
            return BadRequest(new { message = "Registration context required." });

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
            return Unauthorized();

        var deviceCount = await _service.SendPushToAllAsync(jobId.Value, userId, request.PushText, ct);

        return Ok(new SendPushNotificationResponse
        {
            DeviceCount = deviceCount,
            Message = $"Push notification sent to {deviceCount} devices."
        });
    }

    /// <summary>
    /// Get the history of all push notifications sent for the current job.
    /// </summary>
    [HttpGet("history")]
    [ProducesResponseType(typeof(List<PushNotificationHistoryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<List<PushNotificationHistoryDto>>> GetHistory(
        CancellationToken ct)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null)
            return BadRequest(new { message = "Registration context required." });

        var history = await _service.GetNotificationHistoryAsync(jobId.Value, ct);
        return Ok(history);
    }
}
