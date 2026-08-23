using TSIC.Contracts.Dtos;
using TSIC.Contracts.Dtos.PushNotification;
using TSIC.Domain.Entities;

namespace TSIC.Contracts.Repositories;

/// <summary>
/// Repository for push notification data access: device token queries and broadcast audit trail.
/// </summary>
public interface IPushNotificationRepository
{
    /// <summary>
    /// Count of mobile devices registered for push notifications for a given job.
    /// </summary>
    Task<int> GetDeviceCountForJobAsync(Guid jobId, CancellationToken ct = default);

    /// <summary>
    /// All FCM device tokens registered for a job (for batch sending).
    /// </summary>
    Task<List<string>> GetDeviceTokensForJobAsync(Guid jobId, CancellationToken ct = default);

    /// <summary>
    /// Count of devices subscribed to a team in this job — the TSIC-Teams broadcast pool.
    /// Distinct from the job-level pool: a device that registered for the job but never
    /// picked a team is absent here.
    /// </summary>
    Task<int> GetTeamsDeviceCountForJobAsync(Guid jobId, CancellationToken ct = default);

    /// <summary>
    /// The two job flags that decide which mobile app, if either, this job feeds.
    /// Returns null if the job does not exist.
    /// </summary>
    Task<(bool EventsEnabled, bool TeamsEnabled)?> GetJobPushFlagsAsync(Guid jobId, CancellationToken ct = default);


    /// <summary>
    /// History of "push to all" broadcasts for a job, newest first.
    /// </summary>
    Task<List<PushNotificationHistoryDto>> GetNotificationHistoryAsync(Guid jobId, CancellationToken ct = default);

    /// <summary>
    /// Get job name and logo header filename for building the push payload.
    /// Returns null if the job has no display options.
    /// </summary>
    Task<(string JobName, string? LogoHeader)?> GetJobDisplayInfoAsync(Guid jobId, CancellationToken ct = default);

    void AddNotificationRecord(JobPushNotificationsToAll record);

    Task SaveChangesAsync(CancellationToken ct = default);

    Task<List<EventAlertDto>> GetAlertsByJobIdAsync(Guid jobId, CancellationToken ct = default);
}
