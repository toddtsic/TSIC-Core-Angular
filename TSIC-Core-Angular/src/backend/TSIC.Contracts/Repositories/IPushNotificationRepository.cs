using TSIC.Contracts.Dtos;
using TSIC.Contracts.Dtos.PushNotification;
using TSIC.Domain.Entities;
using TSIC.Domain.JobRules;

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
    /// Count of TSIC-Teams devices for this job. Device_Teams is a MIXED table — TSIC-Events
    /// favourite-team rows carry a null RegistrationId, TSIC-Teams app rows carry one — so this
    /// counts only the latter. Without that filter the number runs roughly 40x too high.
    /// </summary>
    Task<int> GetTeamsDeviceCountForJobAsync(Guid jobId, CancellationToken ct = default);

    /// <summary>
    /// TSIC-Teams device tokens for a job: Device_Teams rows carrying a RegistrationId, on
    /// active devices. The TSIC-Teams broadcast pool.
    /// </summary>
    Task<List<string>> GetTeamsDeviceTokensForJobAsync(Guid jobId, CancellationToken ct = default);

    /// <summary>
    /// Teams in the job for the audience selector, each with the number of devices a push to
    /// it would reach. Counts follow <paramref name="audience"/>, because the two apps' rows
    /// live in the same table and are told apart by RegistrationId.
    /// </summary>
    Task<List<PushTeamOptionDto>> GetTeamOptionsWithDeviceCountsAsync(
        Guid jobId, PushAudience audience, CancellationToken ct = default);

    /// <summary>
    /// What decides which mobile app, if either, this job feeds: its job type plus the
    /// TSIC-Teams switch. Feed both to <c>PushAudienceResolver</c>. EventsEnabled reports
    /// whether the job is visible in the TSIC-Events app; it does not select the app.
    /// Returns null if the job does not exist.
    /// </summary>
    Task<(int JobTypeId, bool EventsEnabled, bool TeamsEnabled)?> GetJobPushFlagsAsync(Guid jobId, CancellationToken ct = default);


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
