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

    public async Task<Devices> GetOrCreateDeviceByTokenAsync(string deviceToken, string deviceType, CancellationToken ct = default)
    {
        var device = await _context.Devices.FirstOrDefaultAsync(d => d.Token == deviceToken, ct);
        if (device != null) { device.Type = deviceType; device.Modified = DateTime.UtcNow; device.Active = true; return device; }
        device = new Devices { Id = deviceToken, Token = deviceToken, Type = deviceType, Modified = DateTime.UtcNow, Active = true };
        _context.Devices.Add(device);
        return device;
    }

    public async Task AddDeviceJobIfNotExistsAsync(string deviceId, Guid jobId, CancellationToken ct = default)
    {
        var exists = await _context.DeviceJobs.AnyAsync(dj => dj.DeviceId == deviceId && dj.JobId == jobId, ct);
        if (!exists) _context.DeviceJobs.Add(new DeviceJobs { Id = Guid.NewGuid(), DeviceId = deviceId, JobId = jobId, Modified = DateTime.UtcNow });
    }

    public async Task<bool> ToggleDeviceTeamAsync(string deviceId, Guid teamId, CancellationToken ct = default)
    {
        var existing = await _context.DeviceTeams.FirstOrDefaultAsync(dt => dt.DeviceId == deviceId && dt.TeamId == teamId, ct);
        if (existing != null) { _context.DeviceTeams.Remove(existing); return false; }
        _context.DeviceTeams.Add(new DeviceTeams { Id = Guid.NewGuid(), DeviceId = deviceId, TeamId = teamId, Modified = DateTime.UtcNow });
        return true;
    }

    public async Task<List<Guid>> GetSubscribedTeamIdsAsync(string deviceToken, Guid jobId, CancellationToken ct = default)
    {
        return await _context.DeviceTeams.AsNoTracking()
            .Where(dt => dt.DeviceId == deviceToken && dt.Team.Agegroup.League.JobLeagues.Any(jl => jl.JobId == jobId))
            .Select(dt => dt.TeamId).Distinct().ToListAsync(ct);
    }

    public async Task SwapDeviceTokensAsync(string oldToken, string newToken, CancellationToken ct = default)
    {
        var oldDevice = await _context.Devices.FirstOrDefaultAsync(d => d.Token == oldToken, ct);
        if (oldDevice == null) return;
        var newDevice = await _context.Devices.FirstOrDefaultAsync(d => d.Token == newToken, ct);
        if (newDevice == null) { newDevice = new Devices { Id = newToken, Token = newToken, Type = oldDevice.Type, Modified = DateTime.UtcNow, Active = true }; _context.Devices.Add(newDevice); }
        var deviceJobs = await _context.DeviceJobs.Where(dj => dj.DeviceId == oldDevice.Id).ToListAsync(ct);
        foreach (var dj in deviceJobs) dj.DeviceId = newDevice.Id;
        var deviceTeams = await _context.DeviceTeams.Where(dt => dt.DeviceId == oldDevice.Id).ToListAsync(ct);
        foreach (var dt in deviceTeams) dt.DeviceId = newDevice.Id;
        var deviceRegIds = await _context.DeviceRegistrationIds.Where(dr => dr.DeviceId == oldDevice.Id).ToListAsync(ct);
        foreach (var dr in deviceRegIds) dr.DeviceId = newDevice.Id;
        oldDevice.Active = false; oldDevice.Modified = DateTime.UtcNow;
    }

    public async Task<Devices?> GetDeviceByTokenAsync(string deviceToken, CancellationToken ct = default)
    {
        return await _context.Devices.AsNoTracking().FirstOrDefaultAsync(d => d.Token == deviceToken, ct);
    }
}
