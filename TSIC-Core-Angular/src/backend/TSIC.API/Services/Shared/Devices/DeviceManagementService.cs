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
    private readonly IRegistrationRepository _registrationRepo;

    public DeviceManagementService(
        IDeviceRepository deviceRepo,
        IRegistrationRepository registrationRepo)
    {
        _deviceRepo = deviceRepo;
        _registrationRepo = registrationRepo;
    }

    public async Task<SyncDeviceResponse> SyncDeviceAsync(
        string userId, Guid registrationId, SyncDeviceRequest request, CancellationToken ct = default)
    {
        // Rotation first. If the OS reissued the token, fold the old device row into the new
        // one before writing anything, so the rows below land on one device rather than two.
        if (!string.IsNullOrWhiteSpace(request.PreviousDeviceToken)
            && request.PreviousDeviceToken != request.DeviceToken)
        {
            await _deviceRepo.SwapDeviceTokensAsync(request.PreviousDeviceToken, request.DeviceToken, ct);
            await _deviceRepo.SaveChangesAsync(ct);
        }

        var device = await _deviceRepo.GetOrCreateDeviceByTokenAsync(request.DeviceToken, request.DeviceType, ct);
        await _deviceRepo.SaveChangesAsync(ct);

        // Only the registration the login named. The TSIC-Teams login is a pick of one player
        // and one team, and that pick is what the device is filed against -- the same single
        // row legacy's LoginController wrote. An earlier version walked every registration the
        // family held on any live job, which filed every phone against tournament teams the
        // user never signed into; that is why the registration comes from the bearer and not
        // from a lookup by user.
        var target = await _registrationRepo.GetDeviceSyncTargetAsync(userId, registrationId, ct);
        if (target == null)
            return new SyncDeviceResponse { Jobs = 0, Teams = 0, Registrations = 0 };

        // Deliberately NOT writing Device_Jobs here. That table has exactly two readers, both
        // the TSIC-Events broadcast pool, so a row in it means "send this phone the Events
        // push". Sync is the authenticated TSIC-Teams path, and its tokens belong to the
        // tsic-teams Firebase project -- filing them here put them in the Events blast list,
        // where the Events credential answers SenderIdMismatch and the push reaches nobody.
        // TSIC-Teams devices are reached through Device_Teams. The anonymous register endpoint
        // is what fills Device_Jobs, and that is the TSIC-Events app.
        var teams = 0;
        if (target.TeamId is { } teamId
            && await _deviceRepo.AddDeviceTeamIfNotExistsAsync(device.Id, teamId, target.RegistrationId, ct))
            teams++;

        var regs = await _deviceRepo.AddDeviceRegistrationIdIfNotExistsAsync(device.Id, target.RegistrationId, ct)
            ? 1 : 0;

        await _deviceRepo.SaveChangesAsync(ct);

        return new SyncDeviceResponse { Jobs = 1, Teams = teams, Registrations = regs };
    }

    public async Task RegisterDeviceAsync(RegisterDeviceRequest request, CancellationToken ct = default)
    {
        var device = await _deviceRepo.GetOrCreateDeviceByTokenAsync(request.DeviceToken, request.DeviceType, ct);
        await _deviceRepo.SaveChangesAsync(ct);

        await _deviceRepo.AddDeviceJobIfNotExistsAsync(device.Id, request.JobId, ct);
        await _deviceRepo.SaveChangesAsync(ct);
    }

    public async Task<ToggleTeamSubscriptionResponse> ToggleTeamSubscriptionAsync(
        ToggleTeamSubscriptionRequest request, Guid jobId, Guid? registrationId = null, CancellationToken ct = default)
    {
        // Ensure device exists
        var device = await _deviceRepo.GetOrCreateDeviceByTokenAsync(request.DeviceToken, request.DeviceType, ct);
        await _deviceRepo.SaveChangesAsync(ct);

        // Toggle the subscription. registrationId is the one thing that separates a TSIC-Teams
        // heart from a TSIC-Events heart on the shared table (see PushAudience).
        await _deviceRepo.ToggleDeviceTeamAsync(device.Id, request.TeamId, registrationId, ct);
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
