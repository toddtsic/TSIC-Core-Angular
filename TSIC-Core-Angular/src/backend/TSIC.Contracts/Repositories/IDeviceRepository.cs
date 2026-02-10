using TSIC.Domain.Entities;

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
}
