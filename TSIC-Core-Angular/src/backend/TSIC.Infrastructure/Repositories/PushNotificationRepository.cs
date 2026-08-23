using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Dtos.PushNotification;
using TSIC.Contracts.Repositories;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Data.SqlDbContext;

namespace TSIC.Infrastructure.Repositories;

/// <summary>
/// Concrete implementation of IPushNotificationRepository using Entity Framework Core.
/// Queries DeviceJobs for token/count data and JobPushNotificationsToAll for audit trail.
/// </summary>
public class PushNotificationRepository : IPushNotificationRepository
{
    private readonly SqlDbContext _context;

    public PushNotificationRepository(SqlDbContext context)
    {
        _context = context;
    }

    public async Task<int> GetDeviceCountForJobAsync(Guid jobId, CancellationToken ct = default)
    {
        return await _context.DeviceJobs
            .AsNoTracking()
            .Where(dj => dj.JobId == jobId)
            .CountAsync(ct);
    }

    public async Task<List<string>> GetDeviceTokensForJobAsync(Guid jobId, CancellationToken ct = default)
    {
        return await _context.DeviceJobs
            .AsNoTracking()
            .Where(dj => dj.JobId == jobId)
            .Select(dj => dj.Device.Token)
            .ToListAsync(ct);
    }

    public async Task<int> GetTeamsDeviceCountForJobAsync(Guid jobId, CancellationToken ct = default)
    {
        // TSIC-Teams reaches devices through their team subscriptions, not the job registration.
        //
        // RegistrationId is what separates the two apps in this one table. The TSIC-Teams app
        // subscribes a device at login and always carries the registration; the TSIC-Events
        // favourite-team toggle never does. Counting the whole table reported the Events
        // favourites as Teams devices - 167k rows against 4k real ones, job-wide.
        return await _context.DeviceTeams
            .AsNoTracking()
            .Where(dt => dt.Team.JobId == jobId && dt.RegistrationId != null)
            .Select(dt => dt.DeviceId)
            .Distinct()
            .CountAsync(ct);
    }

    public async Task<List<string>> GetTeamsDeviceTokensForJobAsync(Guid jobId, CancellationToken ct = default)
    {
        return await _context.DeviceTeams
            .AsNoTracking()
            .Where(dt => dt.Team.JobId == jobId && dt.RegistrationId != null)
            .Where(dt => dt.Device.Active && dt.Device.Token != "")
            .Select(dt => dt.Device.Token)
            .Distinct()
            .ToListAsync(ct);
    }

    public async Task<(int JobTypeId, bool EventsEnabled, bool TeamsEnabled)?> GetJobPushFlagsAsync(
        Guid jobId, CancellationToken ct = default)
    {
        var flags = await _context.Jobs
            .AsNoTracking()
            .Where(j => j.JobId == jobId)
            .Select(j => new { j.JobTypeId, j.BSuspendPublic, j.BEnableTsicteams })
            .FirstOrDefaultAsync(ct);

        if (flags == null) return null;

        // bSuspendPublic is inverted: set = hidden from the TSIC-Events app.
        return (flags.JobTypeId, flags.BSuspendPublic != true, flags.BEnableTsicteams == true);
    }


    public async Task<List<PushNotificationHistoryDto>> GetNotificationHistoryAsync(
        Guid jobId, CancellationToken ct = default)
    {
        return await _context.JobPushNotificationsToAll
            .AsNoTracking()
            .Where(p => p.JobId == jobId)
            .OrderByDescending(p => p.Modified)
            .Select(p => new PushNotificationHistoryDto
            {
                Id = p.Id,
                SentBy = p.LebUser.FirstName + " " + p.LebUser.LastName,
                SentWhen = p.Modified,
                PushText = p.PushText,
                DeviceCount = p.DeviceCount
            })
            .ToListAsync(ct);
    }

    public async Task<(string JobName, string? LogoHeader)?> GetJobDisplayInfoAsync(
        Guid jobId, CancellationToken ct = default)
    {
        var result = await _context.JobDisplayOptions
            .AsNoTracking()
            .Where(jdo => jdo.JobId == jobId)
            .Select(jdo => new
            {
                jdo.Job.JobName,
                jdo.LogoHeader
            })
            .FirstOrDefaultAsync(ct);

        if (result == null) return null;
        return (result.JobName ?? "", result.LogoHeader);
    }

    public void AddNotificationRecord(JobPushNotificationsToAll record)
    {
        _context.JobPushNotificationsToAll.Add(record);
    }

    public async Task SaveChangesAsync(CancellationToken ct = default)
    {
        await _context.SaveChangesAsync(ct);
    }

    public async Task<List<TSIC.Contracts.Dtos.EventAlertDto>> GetAlertsByJobIdAsync(Guid jobId, CancellationToken ct = default)
    {
        return await _context.JobPushNotificationsToAll.AsNoTracking()
            .Where(p => p.JobId == jobId).OrderByDescending(p => p.Modified)
            .Select(p => new TSIC.Contracts.Dtos.EventAlertDto { SentWhen = p.Modified, PushText = p.PushText })
            .ToListAsync(ct);
    }
}
