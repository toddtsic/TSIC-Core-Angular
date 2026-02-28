using TSIC.Contracts.Dtos.PushNotification;

namespace TSIC.Contracts.Services;

/// <summary>
/// Business logic for admin push notifications to all mobile devices for a job.
/// </summary>
public interface IPushNotificationService
{
    /// <summary>
    /// Count of mobile devices currently registered for push notifications for the job.
    /// </summary>
    Task<int> GetDeviceCountForJobAsync(Guid jobId, CancellationToken ct = default);

    /// <summary>
    /// Send a push notification to ALL mobile devices registered for the job,
    /// then record the broadcast in the audit trail.
    /// Returns the number of devices targeted.
    /// </summary>
    Task<int> SendPushToAllAsync(Guid jobId, string userId, string pushText, CancellationToken ct = default);

    /// <summary>
    /// Audit trail of all push notifications sent for a job, newest first.
    /// </summary>
    Task<List<PushNotificationHistoryDto>> GetNotificationHistoryAsync(Guid jobId, CancellationToken ct = default);
}
