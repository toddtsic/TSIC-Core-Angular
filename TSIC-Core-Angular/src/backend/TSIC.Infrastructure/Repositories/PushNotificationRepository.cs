using Microsoft.EntityFrameworkCore;
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
        return (result.JobName, result.LogoHeader);
    }

    public void AddNotificationRecord(JobPushNotificationsToAll record)
    {
        _context.JobPushNotificationsToAll.Add(record);
    }

    public async Task SaveChangesAsync(CancellationToken ct = default)
    {
        await _context.SaveChangesAsync(ct);
    }
}
