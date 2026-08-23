using TSIC.API.Extensions;
using TSIC.Contracts.Repositories;
using TSIC.Domain.JobRules;

namespace TSIC.API.Services.Shared.Firebase;

/// <summary>
/// Composes and sends the game-result push to devices subscribed to either team.
/// Payload shape matches what the Events mobile app's toast renderer reads
/// (jobName / agegroupName / divName / firstTeam / secondTeam / firstScore / secondScore,
/// winner listed first — legacy PushGameResultsToSubscribedDevices parity).
/// </summary>
public sealed class GameResultPushService : IGameResultPushService
{
    private readonly IScheduleRepository _scheduleRepo;
    private readonly IDeviceRepository _deviceRepo;
    private readonly IPushNotificationRepository _pushRepo;
    private readonly IFirebasePushService _firebase;
    private readonly IHostEnvironment _env;
    private readonly IConfiguration _configuration;
    private readonly ILogger<GameResultPushService> _logger;
    private readonly string _staticsBaseUrl;

    public GameResultPushService(
        IScheduleRepository scheduleRepo,
        IDeviceRepository deviceRepo,
        IPushNotificationRepository pushRepo,
        IFirebasePushService firebase,
        IHostEnvironment env,
        IConfiguration configuration,
        ILogger<GameResultPushService> logger)
    {
        _scheduleRepo = scheduleRepo;
        _deviceRepo = deviceRepo;
        _pushRepo = pushRepo;
        _firebase = firebase;
        _env = env;
        _configuration = configuration;
        _logger = logger;
        _staticsBaseUrl = configuration.GetValue<string>("TsicSettings:StaticsBaseUrl")
                          ?? "https://statics.teamsportsinfo.com";
    }

    public async Task PushGameResultAsync(int gid, CancellationToken ct = default)
    {
        try
        {
            // Sandbox rule: real pushes reach real phones only from Production.
            // Firebase:SendInSandbox (appsettings toggle) opts Staging in for E2E runs.
            if (_env.IsSandbox() && !_configuration.GetValue<bool>("Firebase:SendInSandbox"))
            {
                _logger.LogInformation("Sandbox: game-result push for gid {Gid} suppressed", gid);
                return;
            }

            var keys = await _scheduleRepo.GetGamePushKeysAsync(gid, ct);
            if (keys?.T1Score == null || keys.T2Score == null) return;

            // Which app this job feeds picks both the pool and the sender. In practice a job
            // with games is tournament or league, so this resolves to Events - but resolving
            // rather than assuming is what keeps a club job's scores from being sent through
            // the wrong project's credential.
            var flags = await _pushRepo.GetJobPushFlagsAsync(keys.JobId, ct);
            var audience = flags == null
                ? PushAudience.None
                : PushAudienceResolver.Resolve(flags.Value.JobTypeId, flags.Value.TeamsEnabled);

            if (audience == PushAudience.None)
            {
                _logger.LogInformation(
                    "Game-result push for gid {Gid} skipped - job {JobId} feeds no mobile app",
                    gid, keys.JobId);
                return;
            }

            var tokens = await _deviceRepo.GetTokensSubscribedToTeamsAsync(audience, keys.T1Id, keys.T2Id, ct);
            if (tokens.Count == 0) return;

            var jobInfo = await _pushRepo.GetJobDisplayInfoAsync(keys.JobId, ct);
            var jobName = jobInfo?.JobName ?? "TSIC";
            var jobLogoUrl = jobInfo?.LogoHeader != null
                ? $"{_staticsBaseUrl}/BannerFiles/{jobInfo.Value.LogoHeader}"
                : null;

            // Winner first (legacy ordering; ties keep T1 first).
            var t2Won = keys.T2Score > keys.T1Score;
            var firstTeam = (t2Won ? keys.T2Name : keys.T1Name) ?? "";
            var secondTeam = (t2Won ? keys.T1Name : keys.T2Name) ?? "";
            var firstScore = t2Won ? keys.T2Score.Value : keys.T1Score.Value;
            var secondScore = t2Won ? keys.T1Score.Value : keys.T2Score.Value;
            var agDiv = $"{keys.AgegroupName}:{keys.DivName}";

            var body = $"{agDiv}\n{firstScore} \t {firstTeam}\n{secondScore} \t {secondTeam}";

            var data = new Dictionary<string, string>
            {
                { "jobName", jobName },
                { "agegroupName", keys.AgegroupName ?? "" },
                { "divName", keys.DivName ?? "" },
                { "firstTeam", firstTeam },
                { "secondTeam", secondTeam },
                { "firstScore", firstScore.ToString() },
                { "secondScore", secondScore.ToString() },
                { "jobLogoUrl", jobLogoUrl ?? "" }
            };

            await _firebase.SendToDevicesAsync(audience, tokens, jobName, body, jobLogoUrl, data, ct);
        }
        catch (Exception ex)
        {
            // Push delivery is best-effort — the score write must never fail because of it.
            _logger.LogError(ex, "Game-result push failed for gid {Gid}", gid);
        }
    }
}
