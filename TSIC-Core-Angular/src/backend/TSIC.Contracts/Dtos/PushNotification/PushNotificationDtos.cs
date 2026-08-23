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

    /// <summary>Team this went to, or null for a job-wide send.</summary>
    public Guid? TeamId { get; init; }

    /// <summary>
    /// Team name for display, or null for a job-wide send. Without this the grid shows a
    /// team-scoped send and an everyone send as identical rows — the same ambiguity that let
    /// the original AddAllTeams bug survive unnoticed.
    /// </summary>
    public string? TeamName { get; init; }
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
/// A job feeds exactly ONE mobile app, never both — the two are separate Firebase projects
/// with non-interchangeable tokens. Job type picks it: tournament and league are TSIC-Events,
/// everything else is TSIC-Teams if the director enabled it, and showcase is neither.
/// </summary>
public record PushNotificationReadinessDto
{
    /// <summary>
    /// The resolved audience: "Events", "Teams", or "None". This is what a send will use —
    /// the counts below describe the pool, this names the app.
    /// </summary>
    public required string Audience { get; init; }

    /// <summary>Devices in the resolved audience's pool — who a send would actually reach.</summary>
    public required int DeviceCount { get; init; }

    /// <summary>
    /// A Firebase sender is configured for the resolved audience. When false, a send throws
    /// rather than silently vanishing — the other project's credential rejects these tokens.
    /// </summary>
    public required bool SenderConfigured { get; init; }

    /// <summary>Job type id, so the screen can explain WHY the audience resolved as it did.</summary>
    public required int JobTypeId { get; init; }

    /// <summary>Job is visible in the TSIC-Events app (Jobs.bSuspendPublic is not set).</summary>
    public required bool EventsEnabled { get; init; }

    /// <summary>Job has the TSIC-Teams app turned on (Jobs.bEnableTSICTeams).</summary>
    public required bool TeamsEnabled { get; init; }
}
