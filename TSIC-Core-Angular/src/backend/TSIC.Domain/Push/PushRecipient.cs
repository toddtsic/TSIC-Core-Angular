namespace TSIC.Domain.Push;

/// <summary>
/// One device to push to, and the badge number that device should show.
///
/// Per-recipient rather than a token list because the badge is per registration and only the
/// server can compute it: a backgrounded iOS app runs no code, so a client cannot count its own
/// unread. That single fact is why the chat fan-out builds one Message per device instead of
/// one broadcast.
/// </summary>
/// <param name="Token">The FCM registration token. Scoped to one Firebase project -- an
/// Events token handed to the Teams credential comes back SenderIdMismatch.</param>
/// <param name="RegId">The registration this device is being pushed for. Rides in the payload
/// so the client can re-select before routing.</param>
/// <param name="Badge">Unread count to display. Never includes the recipient's own messages.</param>
public readonly record struct PushRecipient(string Token, Guid RegId, int Badge);
