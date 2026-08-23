using TSIC.Domain.JobRules;

namespace TSIC.API.Services.Shared.Firebase;

/// <summary>
/// Abstraction over Firebase Cloud Messaging for sending push notifications.
/// Wraps the FirebaseAdmin SDK so the rest of the application is decoupled from FCM specifics.
/// </summary>
public interface IFirebasePushService
{
    /// <summary>
    /// Send a push notification to a list of device tokens through <paramref name="audience"/>'s
    /// Firebase project. The audience is not a hint — TSIC-Events and TSIC-Teams are separate
    /// projects, and a token sent through the wrong one comes back SenderIdMismatch and reaches
    /// nobody. Resolve it with <see cref="PushAudienceResolver"/>, never by guessing at the pool.
    ///
    /// Returns the number of messages FCM accepted — not the number attempted. Callers write
    /// this to the push audit row, so it has to mean delivered.
    ///
    /// Messages are batched in chunks of 499 to stay under the Firebase API limit.
    /// <paramref name="data"/> is the optional FCM data payload — the mobile app reads it
    /// to render in-app toasts (e.g. game-result fields).
    /// </summary>
    Task<int> SendToDevicesAsync(
        PushAudience audience,
        IReadOnlyList<string> deviceTokens,
        string title,
        string body,
        string? imageUrl = null,
        IReadOnlyDictionary<string, string>? data = null,
        CancellationToken ct = default);

    /// <summary>
    /// Whether a Firebase credential is wired for this audience. False means a send would
    /// throw rather than silently vanish — the push screen warns from this.
    /// </summary>
    bool IsConfiguredFor(PushAudience audience);
}
