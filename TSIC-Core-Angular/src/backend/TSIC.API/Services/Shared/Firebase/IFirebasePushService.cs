using TSIC.Domain.JobRules;
using TSIC.Domain.Push;

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
    /// The NEW typed path, added for team chat. Sends one message PER RECIPIENT, because the
    /// badge is per registration and only the server can compute it — a backgrounded iOS app
    /// runs no code and cannot count its own unread.
    ///
    /// <paramref name="payload"/> is a closed union (<see cref="PushPayload"/>), not a
    /// dictionary: the "type" key the clients fork on is derived from the variant rather than
    /// typed in at the call site, so a push cannot claim to be one thing and carry another's
    /// fields. Collapse key, Android channel and TTL come from the variant too.
    ///
    /// DELIBERATELY BESIDE <see cref="SendToDevicesAsync"/>, NOT REPLACING IT. The three live
    /// senders keep their existing code path byte for byte, so shipping chat cannot change what
    /// today's TSIC-Events or TSIC-Teams installs receive. Moving them onto this method is a
    /// later, separate decision (Todd, 2026-09-21).
    ///
    /// Returns the number of messages FCM accepted, not the number attempted.
    /// </summary>
    Task<int> SendEachAsync(
        PushAudience audience,
        IReadOnlyList<PushRecipient> recipients,
        string title,
        string body,
        PushPayload payload,
        CancellationToken ct = default);

    /// <summary>
    /// Whether a Firebase credential is wired for this audience. False means a send would
    /// throw rather than silently vanish — the push screen warns from this.
    /// </summary>
    bool IsConfiguredFor(PushAudience audience);
}
