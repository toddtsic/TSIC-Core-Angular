namespace TSIC.API.Services.Shared.Firebase;

/// <summary>
/// Abstraction over Firebase Cloud Messaging for sending push notifications.
/// Wraps the FirebaseAdmin SDK so the rest of the application is decoupled from FCM specifics.
/// </summary>
public interface IFirebasePushService
{
    /// <summary>
    /// Send a push notification to a list of device tokens via the TSIC Events Firebase project.
    /// Returns the number of tokens that were attempted (list count).
    /// Messages are batched in chunks of 499 to stay under the Firebase API limit.
    /// <paramref name="data"/> is the optional FCM data payload — the mobile app reads it
    /// to render in-app toasts (e.g. game-result fields).
    /// </summary>
    Task<int> SendToDevicesAsync(
        IReadOnlyList<string> deviceTokens,
        string title,
        string body,
        string? imageUrl = null,
        IReadOnlyDictionary<string, string>? data = null,
        CancellationToken ct = default);
}
