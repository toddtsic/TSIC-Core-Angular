namespace TSIC.Contracts.Dtos.PushNotification;

/// <summary>
/// Request to send a push notification to all mobile devices registered for the job.
/// </summary>
public record SendPushNotificationRequest
{
    public required string PushText { get; init; }
}

/// <summary>
/// Device count for the current job (shown in the UI before sending).
/// </summary>
public record PushNotificationDeviceCountDto
{
    public required int DeviceCount { get; init; }
}

/// <summary>
/// Audit trail row for a previously sent push notification.
/// </summary>
public record PushNotificationHistoryDto
{
    public required Guid Id { get; init; }
    public required string SentBy { get; init; }
    public required DateTime SentWhen { get; init; }
    public required string PushText { get; init; }
    public required int DeviceCount { get; init; }
}

/// <summary>
/// Response returned after a push notification is sent successfully.
/// </summary>
public record SendPushNotificationResponse
{
    public required int DeviceCount { get; init; }
    public required string Message { get; init; }
}
