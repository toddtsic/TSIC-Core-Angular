using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;
using TSIC.Domain.JobRules;

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
