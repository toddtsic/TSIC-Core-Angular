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

/// <summary>
/// Delivery readiness for the current job. The Push Notification screen is always
/// reachable; this is what it uses to warn when a send would not actually land.
///
/// Two independent audiences, each with its own device pool and its own Firebase
/// project: TSIC-Events (tournament) and TSIC-Teams (player site).
/// </summary>
public record PushNotificationReadinessDto
{
    /// <summary>Job is visible in the TSIC-Events app (Jobs.bSuspendPublic is not set).</summary>
    public required bool EventsEnabled { get; init; }

    /// <summary>Job has the TSIC-Teams app turned on (Jobs.bEnableTSICTeams).</summary>
    public required bool TeamsEnabled { get; init; }

    /// <summary>Devices registered against the job — the TSIC-Events broadcast pool.</summary>
    public required int EventsDeviceCount { get; init; }

    /// <summary>Devices subscribed to a team in the job — the TSIC-Teams broadcast pool.</summary>
    public required int TeamsDeviceCount { get; init; }

    /// <summary>
    /// A Firebase sender for TSIC-Teams is configured. When false, TSIC-Teams tokens
    /// cannot be delivered to at all — the Events credential rejects them.
    /// </summary>
    public required bool TeamsSenderConfigured { get; init; }
}
