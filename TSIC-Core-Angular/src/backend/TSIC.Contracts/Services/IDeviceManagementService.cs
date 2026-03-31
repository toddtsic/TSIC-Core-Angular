using TSIC.Contracts.Dtos;

namespace TSIC.Contracts.Services;

/// <summary>
/// Service for managing mobile device registrations, team subscriptions, and token lifecycle.
/// </summary>
public interface IDeviceManagementService
{
    /// <summary>Register a device for push notifications on a job.</summary>
    Task RegisterDeviceAsync(RegisterDeviceRequest request, CancellationToken ct = default);

    /// <summary>Toggle team subscription for a device. Returns updated subscribed team IDs.</summary>
    Task<ToggleTeamSubscriptionResponse> ToggleTeamSubscriptionAsync(ToggleTeamSubscriptionRequest request, Guid jobId, CancellationToken ct = default);

    /// <summary>Swap old device token for new one across all records.</summary>
    Task SwapTokenAsync(SwapDeviceTokenRequest request, CancellationToken ct = default);

    /// <summary>Get all team IDs this device is subscribed to for a job.</summary>
    Task<List<Guid>> GetSubscribedTeamIdsAsync(string deviceToken, Guid jobId, CancellationToken ct = default);
}
