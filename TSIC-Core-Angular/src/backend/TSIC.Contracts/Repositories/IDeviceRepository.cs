using TSIC.Domain.Entities;
using TSIC.Domain.JobRules;

namespace TSIC.Contracts.Repositories;

/// <summary>
/// Repository for managing DeviceTeams and DeviceRegistrationIds entities.
/// Used by Roster Swapper to maintain push notification mappings during transfers.
/// </summary>
public interface IDeviceRepository
{
    /// <summary>
    /// Get DeviceTeams records for a specific registration + team combo (tracked for update/delete).
    /// </summary>
    Task<List<DeviceTeams>> GetDeviceTeamsByRegistrationAndTeamAsync(Guid registrationId, Guid teamId, CancellationToken ct = default);

    /// <summary>
    /// Get distinct DeviceIds linked to a registration via DeviceRegistrationIds (where Active=true).
    /// AsNoTracking — used to discover which devices to create DeviceTeams for.
    /// </summary>
    Task<List<string>> GetDeviceIdsByRegistrationAsync(Guid registrationId, CancellationToken ct = default);

    /// <summary>
    /// Get DeviceRegistrationIds records for a registration (tracked for deletion).
    /// </summary>
    Task<List<DeviceRegistrationIds>> GetDeviceRegistrationIdsByRegistrationAsync(Guid registrationId, CancellationToken ct = default);

    void AddDeviceTeam(DeviceTeams entity);
    void AddDeviceRegistrationId(DeviceRegistrationIds entity);
    void RemoveDeviceTeams(IEnumerable<DeviceTeams> entities);
    void RemoveDeviceRegistrationIds(IEnumerable<DeviceRegistrationIds> entities);

    Task<int> SaveChangesAsync(CancellationToken ct = default);

    // ── Device Management (mobile app registration) ──

    Task<Devices> GetOrCreateDeviceByTokenAsync(string deviceToken, string deviceType, CancellationToken ct = default);
    Task AddDeviceJobIfNotExistsAsync(string deviceId, Guid jobId, CancellationToken ct = default);
    Task<bool> ToggleDeviceTeamAsync(string deviceId, Guid teamId, CancellationToken ct = default);

    /// <summary>
    /// Idempotent team subscribe. Sync cannot use ToggleDeviceTeamAsync -- that DELETES the
    /// row on a second call, so a relaunch would unsubscribe the phone it just subscribed.
    /// </summary>
    Task<bool> AddDeviceTeamIfNotExistsAsync(string deviceId, Guid teamId, Guid? registrationId, CancellationToken ct = default);

    /// <summary>Idempotent device-to-registration link. Returns true when a row was added.</summary>
    Task<bool> AddDeviceRegistrationIdIfNotExistsAsync(string deviceId, Guid registrationId, CancellationToken ct = default);
    Task<List<Guid>> GetSubscribedTeamIdsAsync(string deviceToken, Guid jobId, CancellationToken ct = default);

    /// <summary>
    /// Distinct active-device tokens subscribed to either of a game's two teams, narrowed to
    /// one app's devices. Used for game-result and team-scoped push notifications.
    ///
    /// Device_Teams holds both apps' subscriptions: TSIC-Events favourite-team rows have a null
    /// RegistrationId, TSIC-Teams app rows carry one. Sending the unfiltered union through
    /// either credential means half the batch comes back SenderIdMismatch, so the audience is
    /// required rather than optional.
    /// </summary>
    Task<List<string>> GetTokensSubscribedToTeamsAsync(PushAudience audience, Guid? t1Id, Guid? t2Id, CancellationToken ct = default);
    Task SwapDeviceTokensAsync(string oldToken, string newToken, CancellationToken ct = default);
    Task<Devices?> GetDeviceByTokenAsync(string deviceToken, CancellationToken ct = default);
}
