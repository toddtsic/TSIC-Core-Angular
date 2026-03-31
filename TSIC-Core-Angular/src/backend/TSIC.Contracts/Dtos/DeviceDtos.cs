namespace TSIC.Contracts.Dtos;

/// <summary>
/// Register a device for push notifications on a specific job.
/// Creates/updates the Devices record and links it to the job via DeviceJobs.
/// </summary>
public record RegisterDeviceRequest
{
    public required string DeviceToken { get; init; }
    public required Guid JobId { get; init; }
    /// <summary>"ios" or "android".</summary>
    public required string DeviceType { get; init; }
}

/// <summary>
/// Toggle a device's subscription to a specific team for push notifications.
/// If subscribed → unsubscribe. If not → subscribe.
/// </summary>
public record ToggleTeamSubscriptionRequest
{
    public required string DeviceToken { get; init; }
    public required Guid TeamId { get; init; }
    /// <summary>"ios" or "android".</summary>
    public required string DeviceType { get; init; }
}

/// <summary>
/// Swap an old device token for a new one (e.g. after phone upgrade or FCM token refresh).
/// Updates all Devices, DeviceJobs, DeviceTeams, DeviceRegistrationIds records.
/// </summary>
public record SwapDeviceTokenRequest
{
    public required string OldDeviceToken { get; init; }
    public required string NewDeviceToken { get; init; }
}

/// <summary>
/// Response for toggle team subscription — returns the updated list of subscribed team IDs
/// so the mobile app can refresh its local state.
/// </summary>
public record ToggleTeamSubscriptionResponse
{
    public required List<Guid> SubscribedTeamIds { get; init; }
}
