using TSIC.Contracts.Dtos;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;

namespace TSIC.API.Services.Shared.Devices;

/// <summary>
/// Manages mobile device registrations, team subscriptions, and token lifecycle.
/// </summary>
public sealed class DeviceManagementService : IDeviceManagementService
{
    private readonly IDeviceRepository _deviceRepo;

    public DeviceManagementService(IDeviceRepository deviceRepo)
    {
        _deviceRepo = deviceRepo;
    }

    public async Task RegisterDeviceAsync(RegisterDeviceRequest request, CancellationToken ct = default)
    {
        var device = await _deviceRepo.GetOrCreateDeviceByTokenAsync(request.DeviceToken, request.DeviceType, ct);
        await _deviceRepo.SaveChangesAsync(ct);

        await _deviceRepo.AddDeviceJobIfNotExistsAsync(device.Id, request.JobId, ct);
        await _deviceRepo.SaveChangesAsync(ct);
    }

    public async Task<ToggleTeamSubscriptionResponse> ToggleTeamSubscriptionAsync(
        ToggleTeamSubscriptionRequest request, Guid jobId, CancellationToken ct = default)
    {
        // Ensure device exists
        var device = await _deviceRepo.GetOrCreateDeviceByTokenAsync(request.DeviceToken, request.DeviceType, ct);
        await _deviceRepo.SaveChangesAsync(ct);

        // Toggle the subscription
        await _deviceRepo.ToggleDeviceTeamAsync(device.Id, request.TeamId, ct);
        await _deviceRepo.SaveChangesAsync(ct);

        // Return updated list
        var subscribedTeamIds = await _deviceRepo.GetSubscribedTeamIdsAsync(request.DeviceToken, jobId, ct);
        return new ToggleTeamSubscriptionResponse { SubscribedTeamIds = subscribedTeamIds };
    }

    public async Task SwapTokenAsync(SwapDeviceTokenRequest request, CancellationToken ct = default)
    {
        await _deviceRepo.SwapDeviceTokensAsync(request.OldDeviceToken, request.NewDeviceToken, ct);
        await _deviceRepo.SaveChangesAsync(ct);
    }

    public async Task<List<Guid>> GetSubscribedTeamIdsAsync(
        string deviceToken, Guid jobId, CancellationToken ct = default)
    {
        return await _deviceRepo.GetSubscribedTeamIdsAsync(deviceToken, jobId, ct);
    }
}
