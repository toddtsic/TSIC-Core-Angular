using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;
using TSIC.Domain.JobRules;
using TSIC.Domain.Push;

namespace TSIC.API.Services.Shared.Firebase;

/// <summary>
/// Firebase Cloud Messaging service. Registered as a singleton — FirebaseApp is thread-safe
/// and must be initialized once.
///
/// TWO senders, because there are two apps and two Firebase projects: TSIC-Events
/// (<c>tsic-events</c>) and TSIC-Teams (<c>tsic-teams</c>). Registration tokens are scoped to
/// the project that minted them, so the Events credential cannot deliver to a Teams token and
/// vice versa — FCM answers SenderIdMismatch and the push reaches nobody. Legacy ran the same
/// pair, the default app plus a named "TSICTEAMS" one; this is that arrangement rebuilt.
/// </summary>
public class FirebasePushService : IFirebasePushService
{
    private const int MaxBatchSize = 499;
    private const string TeamsAppName = "TSICTEAMS";

    private readonly FirebaseMessaging _eventsMessaging;
    private readonly FirebaseMessaging? _teamsMessaging;
    private readonly ILogger<FirebasePushService> _logger;

    public FirebasePushService(IConfiguration configuration, ILogger<FirebasePushService> logger)
    {
        _logger = logger;

        var eventsPath = configuration["Firebase:CredentialFilePath"]
            ?? throw new InvalidOperationException("Firebase:CredentialFilePath is not configured in appsettings.");

        _eventsMessaging = FirebaseMessaging.GetMessaging(
            FirebaseApp.DefaultInstance ?? FirebaseApp.Create(new AppOptions { Credential = Load(eventsPath) }));

        // The Teams sender is optional at startup on purpose: a box without the TSIC-Teams
        // credential must still boot and still serve every TSIC-Events job. Sends to the
        // Teams audience throw instead, which is loud at the one place it matters.
        var teamsPath = configuration["Firebase:TeamsCredentialFilePath"];
        if (!string.IsNullOrWhiteSpace(teamsPath) && File.Exists(Resolve(teamsPath)))
        {
            var teamsApp = FirebaseApp.GetInstance(TeamsAppName)
                ?? FirebaseApp.Create(new AppOptions { Credential = Load(teamsPath) }, TeamsAppName);
            _teamsMessaging = FirebaseMessaging.GetMessaging(teamsApp);
        }
        else
        {
            _logger.LogWarning(
                "Firebase:TeamsCredentialFilePath is {State} — TSIC-Teams pushes will fail",
                string.IsNullOrWhiteSpace(teamsPath) ? "not configured" : $"missing on disk ({teamsPath})");
        }
    }

    public bool IsConfiguredFor(PushAudience audience) => audience switch
    {
        PushAudience.Events => true,
        PushAudience.Teams => _teamsMessaging != null,
        _ => false
    };

    public async Task<int> SendToDevicesAsync(
        PushAudience audience,
        IReadOnlyList<string> deviceTokens,
        string title,
        string body,
        string? imageUrl = null,
        IReadOnlyDictionary<string, string>? data = null,
        CancellationToken ct = default)
    {
        var messaging = MessagingFor(audience);

        if (deviceTokens.Count == 0)
        {
            _logger.LogInformation("No {Audience} device tokens to send to — skipping push", audience);
            return 0;
        }

        var notification = new Notification
        {
            Title = title,
            Body = body,
            ImageUrl = imageUrl
        };

        var messages = new List<Message>(deviceTokens.Count);
        foreach (var token in deviceTokens)
        {
            if (string.IsNullOrWhiteSpace(token)) continue;

            messages.Add(new Message
            {
                Notification = notification,
                Token = token,
                Data = data,
                Apns = new ApnsConfig
                {
                    Aps = new Aps { Sound = "default" }
                },
                Android = new AndroidConfig
                {
                    Notification = new AndroidNotification { Sound = "default" }
                }
            });
        }

        if (messages.Count == 0)
        {
            _logger.LogWarning("All {Audience} device tokens were empty — skipping push", audience);
            return 0;
        }

        // Batch in chunks of 499 to stay under Firebase's 500-message limit per SendEachAsync call.
        // SendEachAsync does not throw when individual messages fail — it reports them on the
        // BatchResponse. Count what FCM accepted, not what we handed it: the caller writes this
        // number to the push audit row, and attempted-as-delivered made a rejected send read as
        // a successful one.
        var totalSent = 0;
        foreach (var chunk in Chunk(messages, MaxBatchSize))
        {
            var response = await messaging.SendEachAsync(chunk, ct);
            totalSent += response.SuccessCount;

            if (response.FailureCount > 0)
            {
                // Error codes, not just a count. SenderIdMismatch here means the token belongs
                // to a different Firebase project than the credential in use — i.e. the audience
                // was resolved wrong, which is a routing bug and not a dead device.
                var codes = string.Join(", ", response.Responses
                    .Where(r => !r.IsSuccess)
                    .GroupBy(r => (r.Exception as FirebaseMessagingException)?.MessagingErrorCode?.ToString() ?? "Unknown")
                    .Select(g => $"{g.Key}={g.Count()}"));

                _logger.LogWarning(
                    "Firebase {Audience} batch: {Success} succeeded, {Failed} failed out of {Total} [{Codes}]",
                    audience, response.SuccessCount, response.FailureCount, chunk.Count, codes);
            }
        }

        _logger.LogInformation(
            "Push notification delivered to {Delivered} of {Attempted} {Audience} devices",
            totalSent, deviceTokens.Count, audience);
        return totalSent;
    }

    // ── The typed path (new; see IFirebasePushService.SendEachAsync) ─────────────────────
    // Everything above this line is the live senders' path and is unchanged.

    public async Task<int> SendEachAsync(
        PushAudience audience,
        IReadOnlyList<PushRecipient> recipients,
        string title,
        string body,
        PushPayload payload,
        CancellationToken ct = default)
    {
        // Resolve the sender FIRST, before any work. A missing Teams credential has to throw
        // here rather than return 0 — a send that reports success while reaching nobody is the
        // one failure mode nobody notices.
        var messaging = MessagingFor(audience);

        if (recipients.Count == 0)
        {
            _logger.LogInformation("No {Audience} recipients for a {Type} push — skipping", audience, payload.Type);
            return 0;
        }

        var notification = new Notification { Title = title, Body = body };
        var baseData = payload.ToData();
        var ttlSeconds = payload.TimeToLive;
        var expiration = ttlSeconds is { } t ? DateTime.UtcNow.Add(t) : (DateTime?)null;

        var messages = new List<Message>(recipients.Count);
        foreach (var r in recipients)
        {
            if (string.IsNullOrWhiteSpace(r.Token)) continue;

            // regId is per recipient, so the data map cannot be shared across messages the way
            // the untyped path shares it. Copy the payload's map and overlay this recipient's.
            var data = new Dictionary<string, string>(baseData, StringComparer.Ordinal)
            {
                ["regId"] = r.RegId.ToString()
            };

            messages.Add(new Message
            {
                Token = r.Token,
                Notification = notification,
                Data = data,
                Apns = new ApnsConfig
                {
                    Headers = BuildApnsHeaders(payload, expiration),
                    Aps = new Aps { Badge = r.Badge, Sound = "default" }
                },
                Android = new AndroidConfig
                {
                    CollapseKey = payload.CollapseKey,
                    TimeToLive = ttlSeconds,
                    Notification = new AndroidNotification
                    {
                        Sound = "default",
                        ChannelId = payload.AndroidChannelId,
                        NotificationCount = r.Badge
                    }
                }
            });
        }

        if (messages.Count == 0)
        {
            _logger.LogWarning("All {Audience} recipient tokens were empty — skipping {Type} push", audience, payload.Type);
            return 0;
        }

        var totalSent = 0;
        foreach (var chunk in Chunk(messages, MaxBatchSize))
        {
            var response = await messaging.SendEachAsync(chunk, ct);
            totalSent += response.SuccessCount;

            if (response.FailureCount > 0)
            {
                var codes = string.Join(", ", response.Responses
                    .Where(r => !r.IsSuccess)
                    .GroupBy(r => (r.Exception as FirebaseMessagingException)?.MessagingErrorCode?.ToString() ?? "Unknown")
                    .Select(g => $"{g.Key}={g.Count()}"));

                // Do NOT prune tokens on SenderIdMismatch — it means the audience was resolved
                // to the wrong Firebase project, and the device is fine.
                _logger.LogWarning(
                    "Firebase {Audience} {Type} batch: {Success} succeeded, {Failed} failed out of {Total} [{Codes}]",
                    audience, payload.Type, response.SuccessCount, response.FailureCount, chunk.Count, codes);
            }
        }

        _logger.LogInformation(
            "{Type} push delivered to {Delivered} of {Attempted} {Audience} devices",
            payload.Type, totalSent, recipients.Count, audience);
        return totalSent;
    }

    /// <summary>
    /// APNs has no Android-style collapse_key or ttl field — it carries apns-collapse-id and an
    /// absolute apns-expiration epoch in the request headers instead. Same two ideas, different
    /// spelling; omit either header rather than sending an empty one.
    /// </summary>
    private static Dictionary<string, string>? BuildApnsHeaders(PushPayload payload, DateTime? expirationUtc)
    {
        var headers = new Dictionary<string, string>(StringComparer.Ordinal);

        if (!string.IsNullOrWhiteSpace(payload.CollapseKey))
            headers["apns-collapse-id"] = payload.CollapseKey!;

        if (expirationUtc is { } exp)
            headers["apns-expiration"] = new DateTimeOffset(exp, TimeSpan.Zero)
                .ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);

        return headers.Count == 0 ? null : headers;
    }

    private FirebaseMessaging MessagingFor(PushAudience audience) => audience switch
    {
        PushAudience.Events => _eventsMessaging,
        PushAudience.Teams => _teamsMessaging
            ?? throw new InvalidOperationException(
                "No TSIC-Teams Firebase sender is configured (Firebase:TeamsCredentialFilePath). "
                + "TSIC-Teams tokens cannot be delivered to through the TSIC-Events credential."),
        _ => throw new InvalidOperationException(
            $"Cannot send a push to audience '{audience}' — this job feeds no mobile app.")
    };

    private static string Resolve(string relativeOrAbsolute) =>
        Path.IsPathRooted(relativeOrAbsolute)
            ? relativeOrAbsolute
            : Path.Combine(AppContext.BaseDirectory, relativeOrAbsolute);

    // GoogleCredential.FromFile is marked obsolete by Google.Apis.Auth in favor of the new
    // CredentialFactory API. FirebaseAdmin.AppOptions.Credential still types as GoogleCredential,
    // and FirebaseAdmin internally calls the same FromFile path, so the actual migration is a
    // follow-up tied to FirebaseAdmin's API. Suppress the warning until then.
#pragma warning disable CS0618
    private static GoogleCredential Load(string path) =>
        GoogleCredential
            .FromFile(Resolve(path))
            .CreateScoped("https://www.googleapis.com/auth/firebase.messaging");
#pragma warning restore CS0618

    private static IEnumerable<List<T>> Chunk<T>(List<T> source, int size)
    {
        for (var i = 0; i < source.Count; i += size)
        {
            yield return source.GetRange(i, Math.Min(size, source.Count - i));
        }
    }
}
