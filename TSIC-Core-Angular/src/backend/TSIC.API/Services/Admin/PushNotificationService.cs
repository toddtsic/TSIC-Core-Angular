using TSIC.API.Services.Shared.Firebase;
using TSIC.Contracts.Dtos.PushNotification;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Entities;
using TSIC.Domain.JobRules;

namespace TSIC.API.Services.Admin;

/// <summary>
/// Orchestrates sending push notifications to every mobile device for a job.
///
/// A job feeds exactly one app. <see cref="PushAudienceResolver"/> decides which, and that
/// single decision picks BOTH the device pool and the Firebase sender — they can never be
/// chosen separately, because a pool sent through the other project's credential reaches
/// nobody.
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
        var (audience, _) = await ResolveAudienceAsync(jobId, ct);
        return await CountPoolAsync(audience, jobId, ct);
    }

    public async Task<PushNotificationReadinessDto> GetReadinessAsync(
        Guid jobId, CancellationToken ct = default)
    {
        var (audience, flags) = await ResolveAudienceAsync(jobId, ct);

        return new PushNotificationReadinessDto
        {
            Audience = audience.ToString(),
            DeviceCount = await CountPoolAsync(audience, jobId, ct),
            SenderConfigured = _firebasePushService.IsConfiguredFor(audience),
            JobTypeId = flags?.JobTypeId ?? 0,
            EventsEnabled = flags?.EventsEnabled ?? false,
            TeamsEnabled = flags?.TeamsEnabled ?? false
        };
    }

    public async Task<int> SendPushToAllAsync(
        Guid jobId, string userId, string pushText, CancellationToken ct = default)
    {
        var (audience, _) = await ResolveAudienceAsync(jobId, ct);

        // A job feeding neither app has no pool to fall back to. Refuse rather than quietly
        // recording an audit row for a broadcast that went nowhere.
        if (audience == PushAudience.None)
            throw new InvalidOperationException(
                "This job feeds no mobile app, so a push has no audience. Showcase jobs run "
                + "neither app; other non-scheduling jobs need TSIC-Teams enabled first.");

        var jobInfo = await _repo.GetJobDisplayInfoAsync(jobId, ct);
        var jobName = jobInfo?.JobName ?? "TSIC";
        var jobLogoUrl = jobInfo?.LogoHeader != null
            ? $"{_staticsBaseUrl}/BannerFiles/{jobInfo.Value.LogoHeader}"
            : null;

        var tokens = await GetPoolAsync(audience, jobId, ct);

        var deviceCount = await _firebasePushService.SendToDevicesAsync(
            audience, tokens, jobName, pushText, jobLogoUrl, ct: ct);

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
            "Push notification delivered to {DeviceCount} of {Attempted} {Audience} devices "
            + "for job {JobId} by user {UserId}",
            deviceCount, tokens.Count, audience, jobId, userId);

        return deviceCount;
    }

    public async Task<List<PushTeamOptionDto>> GetTeamOptionsAsync(
        Guid jobId, CancellationToken ct = default)
    {
        // The audience decides which app's subscriptions to count, so it is resolved here
        // rather than left to the caller — a count taken against the wrong app would promise
        // a reach the send cannot deliver.
        var (audience, _) = await ResolveAudienceAsync(jobId, ct);
        return await _repo.GetTeamOptionsWithDeviceCountsAsync(jobId, audience, ct);
    }

    public async Task<List<PushNotificationHistoryDto>> GetNotificationHistoryAsync(
        Guid jobId, CancellationToken ct = default)
    {
        return await _repo.GetNotificationHistoryAsync(jobId, ct);
    }

    private async Task<(PushAudience Audience, (int JobTypeId, bool EventsEnabled, bool TeamsEnabled)? Flags)>
        ResolveAudienceAsync(Guid jobId, CancellationToken ct)
    {
        var flags = await _repo.GetJobPushFlagsAsync(jobId, ct);
        if (flags == null) return (PushAudience.None, null);

        return (PushAudienceResolver.Resolve(flags.Value.JobTypeId, flags.Value.TeamsEnabled), flags);
    }

    private async Task<int> CountPoolAsync(PushAudience audience, Guid jobId, CancellationToken ct) =>
        audience switch
        {
            PushAudience.Events => await _repo.GetDeviceCountForJobAsync(jobId, ct),
            PushAudience.Teams => await _repo.GetTeamsDeviceCountForJobAsync(jobId, ct),
            _ => 0
        };

    private async Task<List<string>> GetPoolAsync(PushAudience audience, Guid jobId, CancellationToken ct) =>
        audience switch
        {
            PushAudience.Events => await _repo.GetDeviceTokensForJobAsync(jobId, ct),
            PushAudience.Teams => await _repo.GetTeamsDeviceTokensForJobAsync(jobId, ct),
            _ => []
        };
}
