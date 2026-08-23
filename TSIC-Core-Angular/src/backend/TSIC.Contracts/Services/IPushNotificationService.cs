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
    /// Delivery readiness for the job: which mobile apps are enabled, how many devices
    /// sit in each pool, and whether a sender exists for each. The screen is always
    /// reachable, so this is what it warns from.
    /// </summary>
    Task<PushNotificationReadinessDto> GetReadinessAsync(Guid jobId, CancellationToken ct = default);


    /// <summary>
    /// Send a push notification to ALL mobile devices registered for the job,
    /// then record the broadcast in the audit trail.
    /// Returns the number of devices targeted.
    /// </summary>
    Task<int> SendPushToAllAsync(Guid jobId, string userId, string pushText, CancellationToken ct = default);

    /// <summary>
    /// Send one push to a chosen set of teams in the job, writing one audit row per team.
    /// Team ids are validated against the job. Returns the deduped delivered count.
    /// </summary>
    Task<SendTeamsPushResponse> SendPushToTeamsAsync(
        Guid jobId, string userId, string pushText, IReadOnlyList<Guid> teamIds,
        CancellationToken ct = default);

    /// <summary>
    /// Teams in the job for the audience selector, each with the number of devices a push to
    /// it would reach. Counts are taken against the job's resolved audience.
    /// </summary>
    Task<List<PushTeamOptionDto>> GetTeamOptionsAsync(Guid jobId, CancellationToken ct = default);

    /// <summary>
    /// Audit trail of all push notifications sent for a job, newest first.
    /// </summary>
    Task<List<PushNotificationHistoryDto>> GetNotificationHistoryAsync(Guid jobId, CancellationToken ct = default);
}
