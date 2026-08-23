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
    private readonly bool _teamsSenderConfigured;


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

        // A TSIC-Teams broadcast needs its own Firebase project — TSIC-Teams tokens are
        // minted by a different sender and the Events credential cannot deliver to them.
        // Legacy ran both apps side by side; this stack currently wires only Events.
        var teamsCredentialPath = configuration.GetValue<string>("Firebase:TeamsCredentialFilePath");
        _teamsSenderConfigured = !string.IsNullOrWhiteSpace(teamsCredentialPath);

    }

    public async Task<int> GetDeviceCountForJobAsync(Guid jobId, CancellationToken ct = default)
    {
        return await _repo.GetDeviceCountForJobAsync(jobId, ct);
    }

    public async Task<PushNotificationReadinessDto> GetReadinessAsync(
        Guid jobId, CancellationToken ct = default)
    {
        var flags = await _repo.GetJobPushFlagsAsync(jobId, ct);
        var eventsDevices = await _repo.GetDeviceCountForJobAsync(jobId, ct);
        var teamsDevices = await _repo.GetTeamsDeviceCountForJobAsync(jobId, ct);

        return new PushNotificationReadinessDto
        {
            EventsEnabled = flags?.EventsEnabled ?? false,
            TeamsEnabled = flags?.TeamsEnabled ?? false,
            EventsDeviceCount = eventsDevices,
            TeamsDeviceCount = teamsDevices,
            TeamsSenderConfigured = _teamsSenderConfigured
        };
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
            tokens, jobName, pushText, jobLogoUrl, ct: ct);

        // 4. Record the broadcast in the audit trail
        var record = new JobPushNotificationsToAll
        {
            Id = Guid.NewGuid(),
            JobId = jobId,
            LebUserId = userId,
            PushText = pushText,
            Modified = DateTime.Now,
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
