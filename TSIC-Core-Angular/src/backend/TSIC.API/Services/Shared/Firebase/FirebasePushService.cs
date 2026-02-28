using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;

namespace TSIC.API.Services.Shared.Firebase;

/// <summary>
/// Firebase Cloud Messaging service using the TSIC Events project credentials.
/// Registered as a singleton — FirebaseApp is thread-safe and should be initialized once.
/// Credential file path is read from appsettings "Firebase:CredentialFilePath".
/// </summary>
public class FirebasePushService : IFirebasePushService
{
    private const int MaxBatchSize = 499;
    private readonly FirebaseMessaging _messaging;
    private readonly ILogger<FirebasePushService> _logger;

    public FirebasePushService(IConfiguration configuration, ILogger<FirebasePushService> logger)
    {
        _logger = logger;

        var credentialPath = configuration["Firebase:CredentialFilePath"]
            ?? throw new InvalidOperationException("Firebase:CredentialFilePath is not configured in appsettings.");

        var app = FirebaseApp.Create(new AppOptions
        {
            Credential = GoogleCredential
                .FromFile(credentialPath)
                .CreateScoped("https://www.googleapis.com/auth/firebase.messaging")
        });

        _messaging = FirebaseMessaging.GetMessaging(app);
    }

    public async Task<int> SendToDevicesAsync(
        IReadOnlyList<string> deviceTokens,
        string title,
        string body,
        string? imageUrl = null,
        CancellationToken ct = default)
    {
        if (deviceTokens.Count == 0)
        {
            _logger.LogInformation("No device tokens to send to — skipping push");
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
            _logger.LogWarning("All device tokens were empty — skipping push");
            return 0;
        }

        // Batch in chunks of 499 to stay under Firebase's 500-message limit per SendEachAsync call
        var totalSent = 0;
        foreach (var chunk in Chunk(messages, MaxBatchSize))
        {
            var response = await _messaging.SendEachAsync(chunk, ct);
            totalSent += chunk.Count;

            if (response.FailureCount > 0)
            {
                _logger.LogWarning(
                    "Firebase batch: {Success} succeeded, {Failed} failed out of {Total}",
                    response.SuccessCount, response.FailureCount, chunk.Count);
            }
        }

        _logger.LogInformation("Push notification sent to {Count} devices", totalSent);
        return deviceTokens.Count;
    }

    private static IEnumerable<List<T>> Chunk<T>(List<T> source, int size)
    {
        for (var i = 0; i < source.Count; i += size)
        {
            yield return source.GetRange(i, Math.Min(size, source.Count - i));
        }
    }
}
