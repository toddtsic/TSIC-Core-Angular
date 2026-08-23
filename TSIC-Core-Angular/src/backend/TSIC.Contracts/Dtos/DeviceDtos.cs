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

/// <summary>
/// Files a device against everything the bearer's user holds — jobs, teams and
/// registrations — in one authenticated call.
///
/// The body carries a device token and a platform string, and nothing else. Job and team
/// are derived from the bearer, never accepted from the caller: an endpoint that took a
/// jobId or teamId here would let any authenticated user subscribe their phone to any team
/// in the system.
///
/// Idempotent — the client calls it on every launch and every token event.
/// </summary>
public record SyncDeviceRequest
{
    public required string DeviceToken { get; init; }
    /// <summary>"ios" or "android".</summary>
    public required string DeviceType { get; init; }
    /// <summary>Set when the OS reissued a token; folds the swap in first.</summary>
    public string? PreviousDeviceToken { get; init; }
}

/// <summary>
/// What sync filed. The client does not branch on this; it exists so the call is
/// diagnosable from logs and tests.
/// </summary>
public record SyncDeviceResponse
{
    public required int Jobs { get; init; }
    public required int Teams { get; init; }
    public required int Registrations { get; init; }
}

/// <summary>
/// One active registration reduced to the three keys a device is filed against.
/// Purpose-built for sync: MobileContextDto carries no JobId, and widening it to serve
/// this would drag the mobile login contract along.
/// </summary>
public record DeviceSyncTargetDto
{
    public required Guid RegistrationId { get; init; }
    public required Guid JobId { get; init; }
    /// <summary>Null when the registration is not yet placed on a team.</summary>
    public Guid? TeamId { get; init; }
}
