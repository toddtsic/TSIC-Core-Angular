using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Repositories;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Data.SqlDbContext;

namespace TSIC.Infrastructure.Repositories;

/// <summary>
/// Repository for DeviceTeams and DeviceRegistrationIds entities.
/// Manages push notification device mappings during roster transfers.
/// </summary>
public class DeviceRepository : IDeviceRepository
{
    private readonly SqlDbContext _context;

    public DeviceRepository(SqlDbContext context)
    {
        _context = context;
    }

    public async Task<List<DeviceTeams>> GetDeviceTeamsByRegistrationAndTeamAsync(
        Guid registrationId, Guid teamId, CancellationToken ct = default)
    {
        return await _context.DeviceTeams
            .Where(dt => dt.RegistrationId == registrationId && dt.TeamId == teamId)
            .ToListAsync(ct);
    }

    public async Task<List<string>> GetDeviceIdsByRegistrationAsync(
        Guid registrationId, CancellationToken ct = default)
    {
        return await _context.DeviceRegistrationIds
            .AsNoTracking()
            .Where(dr => dr.RegistrationId == registrationId && dr.Active)
            .Select(dr => dr.DeviceId)
            .Distinct()
            .ToListAsync(ct);
    }

    public async Task<List<DeviceRegistrationIds>> GetDeviceRegistrationIdsByRegistrationAsync(
        Guid registrationId, CancellationToken ct = default)
    {
        return await _context.DeviceRegistrationIds
            .Where(dr => dr.RegistrationId == registrationId)
            .ToListAsync(ct);
    }

    public void AddDeviceTeam(DeviceTeams entity) => _context.DeviceTeams.Add(entity);

    public void AddDeviceRegistrationId(DeviceRegistrationIds entity) => _context.DeviceRegistrationIds.Add(entity);

    public void RemoveDeviceTeams(IEnumerable<DeviceTeams> entities) => _context.DeviceTeams.RemoveRange(entities);

    public void RemoveDeviceRegistrationIds(IEnumerable<DeviceRegistrationIds> entities) => _context.DeviceRegistrationIds.RemoveRange(entities);

    public async Task<int> SaveChangesAsync(CancellationToken ct = default) => await _context.SaveChangesAsync(ct);
}
