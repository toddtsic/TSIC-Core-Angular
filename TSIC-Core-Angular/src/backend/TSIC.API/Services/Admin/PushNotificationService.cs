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

    /// <summary>Upper bound on a multi-team selection; see SendPushToTeamsAsync.</summary>
    private const int MaxTeamsPerSend = 50;

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

    public async Task<SendTeamsPushResponse> SendPushToTeamsAsync(
        Guid jobId, string userId, string pushText, IReadOnlyList<Guid> teamIds,
        CancellationToken ct = default)
    {
        if (teamIds.Count == 0)
            throw new InvalidOperationException(
                "Select at least one team, or send to the whole event.");

        // Each team is its own Firebase batch (see below), so an unbounded selection is an
        // unbounded number of round trips inside one request. Past this, the event-wide send
        // is the right tool and is a single batch.
        if (teamIds.Count > MaxTeamsPerSend)
            throw new InvalidOperationException(
                $"Select at most {MaxTeamsPerSend} teams at a time, or send to the whole event.");

        var (audience, _) = await ResolveAudienceAsync(jobId, ct);

        if (audience == PushAudience.None)
            throw new InvalidOperationException(
                "This job feeds no mobile app, so a push has no audience. Showcase jobs run "
                + "neither app; other non-scheduling jobs need TSIC-Teams enabled first.");

        var ownedSet = (await _repo.GetOwnedTeamIdsAsync(jobId, teamIds, ct)).ToHashSet();
        var owned = teamIds.Where(ownedSet.Contains).ToList();

        if (owned.Count == 0)
            throw new InvalidOperationException(
                "None of the selected teams belong to this event.");

        var rows = await _repo.GetTeamTokensAsync(jobId, audience, owned, ct);
        var byTeam = rows
            .GroupBy(r => r.TeamId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Token).ToList());

        var jobInfo = await _repo.GetJobDisplayInfoAsync(jobId, ct);
        var jobName = jobInfo?.JobName ?? "TSIC";
        var jobLogoUrl = jobInfo?.LogoHeader != null
            ? $"{_staticsBaseUrl}/BannerFiles/{jobInfo.Value.LogoHeader}"
            : null;

        // One batch per team, over token slices that are mutually exclusive.
        //
        // A parent following two of the selected teams must receive ONE notification, not one
        // per child -- so the first team to claim a token owns it. Slicing this way also keeps
        // every audit row a real delivered count: send the deduped union as a single batch and
        // Firebase hands back one total that cannot be attributed to any team, which is how a
        // per-team row ends up claiming a reach it never had.
        var claimed = new HashSet<string>(StringComparer.Ordinal);
        var totalDelivered = 0;

        foreach (var teamId in owned)
        {
            var slice = byTeam.TryGetValue(teamId, out var tokens)
                ? tokens.Where(claimed.Add).ToList()
                : [];

            var delivered = slice.Count == 0
                ? 0
                : await _firebasePushService.SendToDevicesAsync(
                    audience, slice, jobName, pushText, jobLogoUrl, ct: ct);

            totalDelivered += delivered;

            // A team with nobody following it still gets a row. The director chose it, and
            // "nobody on this team has the app" is the answer the history grid is asked for.
            _repo.AddNotificationRecord(new JobPushNotificationsToAll
            {
                Id = Guid.NewGuid(),
                JobId = jobId,
                TeamId = teamId,
                LebUserId = userId,
                PushText = pushText,
                Modified = DateTime.Now,
                DeviceCount = delivered
            });
        }

        await _repo.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Push notification delivered to {Delivered} {Audience} devices across {Teams} team(s) "
            + "for job {JobId} by user {UserId}",
            totalDelivered, audience, owned.Count, jobId, userId);

        return new SendTeamsPushResponse
        {
            DeviceCount = totalDelivered,
            TeamCount = owned.Count,
            Message = $"Push notification sent to {owned.Count} team(s), "
                + $"{totalDelivered} device(s)."
        };
    }    public async Task<List<PushNotificationHistoryDto>> GetNotificationHistoryAsync(
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
