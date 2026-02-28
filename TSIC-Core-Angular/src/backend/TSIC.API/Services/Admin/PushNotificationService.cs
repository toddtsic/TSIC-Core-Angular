using TSIC.API.Services.Shared.Firebase;
using TSIC.Contracts.Dtos.PushNotification;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Entities;

namespace TSIC.API.Services.Admin;

/// <summary>
/// Orchestrates sending push notifications to all mobile devices for a job.
/// Delegates to IFirebasePushService for FCM delivery and IPushNotificationRepository for data access.
/// </summary>
public class PushNotificationService : IPushNotificationService
{
    private readonly IPushNotificationRepository _repo;
    private readonly IFirebasePushService _firebasePushService;
    private readonly ILogger<PushNotificationService> _logger;
    private readonly string _staticsBaseUrl;

    public PushNotificationService(
        IPushNotificationRepository repo,
        IFirebasePushService firebasePushService,
        IConfiguration configuration,
        ILogger<PushNotificationService> logger)
    {
        _repo = repo;
        _firebasePushService = firebasePushService;
        _logger = logger;
        _staticsBaseUrl = configuration.GetValue<string>("TsicSettings:StaticsBaseUrl")
                          ?? "https://statics.teamsportsinfo.com";
    }

    public async Task<int> GetDeviceCountForJobAsync(Guid jobId, CancellationToken ct = default)
    {
        return await _repo.GetDeviceCountForJobAsync(jobId, ct);
    }

    public async Task<int> SendPushToAllAsync(
        Guid jobId, string userId, string pushText, CancellationToken ct = default)
    {
        // 1. Get job display info for the notification payload
        var jobInfo = await _repo.GetJobDisplayInfoAsync(jobId, ct);
        var jobName = jobInfo?.JobName ?? "TSIC";
        var jobLogoUrl = jobInfo?.LogoHeader != null
            ? $"{_staticsBaseUrl}/BannerFiles/{jobInfo.Value.LogoHeader}"
            : null;

        // 2. Get all device tokens for the job
        var tokens = await _repo.GetDeviceTokensForJobAsync(jobId, ct);

        // 3. Send via Firebase
        var deviceCount = await _firebasePushService.SendToDevicesAsync(
            tokens, jobName, pushText, jobLogoUrl, ct);

        // 4. Record the broadcast in the audit trail
        var record = new JobPushNotificationsToAll
        {
            Id = Guid.NewGuid(),
            JobId = jobId,
            LebUserId = userId,
            PushText = pushText,
            Modified = DateTime.UtcNow,
            DeviceCount = deviceCount
        };

        _repo.AddNotificationRecord(record);
        await _repo.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Push notification sent to {DeviceCount} devices for job {JobId} by user {UserId}",
            deviceCount, jobId, userId);

        return deviceCount;
    }

    public async Task<List<PushNotificationHistoryDto>> GetNotificationHistoryAsync(
        Guid jobId, CancellationToken ct = default)
    {
        return await _repo.GetNotificationHistoryAsync(jobId, ct);
    }
}
