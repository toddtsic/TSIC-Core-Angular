namespace TSIC.Contracts.Dtos.TeamChat;

/// <summary>
/// One message as the thread renders it. Deleted messages still come back -- as a tombstone
/// with <see cref="IsDeleted"/> set and <see cref="Text"/> emptied -- because a phone that
/// already holds the message has to be told to drop it.
/// </summary>
public record ChatMessageDto
{
    public required Guid MessageId { get; init; }

    /// <summary>
    /// Permanent position in the team's thread. Sort key, stable across edits, and what the
    /// push payload carries. NOT the catch-up cursor -- see <see cref="LastTouchSeq"/>.
    /// </summary>
    public required long Seq { get; init; }

    /// <summary>
    /// Bumped on ANY change to this row -- edit, delete, pin, reaction. The catch-up cursor is
    /// the maximum of these, never of <see cref="Seq"/>: an edit to an old message has to come
    /// back on the next poll, and it must not move to the bottom of the thread when it does.
    /// </summary>
    public required long LastTouchSeq { get; init; }

    /// <summary>
    /// 0 = a person typed it, 1 = the system posted it (joins, leaves, renames). Kind 1 does
    /// NOT mean there is no author -- every row carries one. On the wire so the client can
    /// render system messages differently instead of as somebody's speech.
    /// </summary>
    public required byte Kind { get; init; }

    /// <summary>Emptied on a tombstone. Never null, so the client never branches on null.</summary>
    public required string Text { get; init; }

    /// <summary>
    /// Display name, resolved server-side. A Player registration renders as
    /// "{player name} Account" -- a household login posts under the player's banner, not the
    /// account holder's (Todd, 2026-09-20). Everyone else is their own name, no suffix.
    /// </summary>
    public required string AuthorName { get; init; }

    /// <summary>
    /// Fully-resolved statics URL, or null when the author has no headshot. A URL rather than
    /// an id because the mobile client holds no statics configuration and must not be asked to
    /// assemble one. Headshots are public-by-GUID by design -- this leaks nothing the admin
    /// surfaces do not already publish.
    /// </summary>
    public string? AuthorHeadshotUrl { get; init; }

    /// <summary>
    /// The client's own idempotency key, echoed back so an optimistic bubble can be reconciled
    /// with the server's row rather than duplicated beside it.
    /// </summary>
    public required Guid ClientMessageId { get; init; }

    public Guid? ReplyToMessageId { get; init; }

    /// <summary>
    /// Arizona time, carried with the fixed -07:00 offset so the client converts by arithmetic
    /// instead of hardcoding the zone. Arizona does not observe daylight saving; the offset is
    /// the same in both seasons.
    /// </summary>
    public required DateTimeOffset Created { get; init; }

    public DateTimeOffset? EditedAt { get; init; }

    /// <summary>True on a tombstone. The row is never removed from the store.</summary>
    public required bool IsDeleted { get; init; }

    public DateTimeOffset? PinnedAt { get; init; }
}

/// <summary>
/// One page of catch-up. The cursor contract is the whole point of this shape -- read
/// <see cref="NextCursor"/> before using anything else here.
/// </summary>
public record ChatPageDto
{
    /// <summary>Ascending by <see cref="ChatMessageDto.LastTouchSeq"/>.</summary>
    public required IReadOnlyList<ChatMessageDto> Messages { get; init; }

    /// <summary>
    /// What to send as <c>since</c> on the NEXT call: the highest LastTouchSeq IN THIS PAGE,
    /// or the caller's own <c>since</c> when the page is empty.
    ///
    /// NEVER <see cref="HighWaterSeq"/>. An earlier draft of this contract echoed the
    /// team-wide maximum back as the cursor while also returning <see cref="HasMore"/> -- which
    /// silently skips everything between the end of a partial page and the high-water mark.
    /// The defect only shows under load, which is exactly when it costs the most.
    /// </summary>
    public required long NextCursor { get; init; }

    /// <summary>
    /// The team's current maximum LastTouchSeq. A PROGRESS INDICATOR ONLY -- "you are 140
    /// behind" -- and never a cursor. Zero, not null, on an empty thread.
    /// </summary>
    public required long HighWaterSeq { get; init; }

    /// <summary>True when more rows sit past <see cref="NextCursor"/>. Poll again immediately.</summary>
    public required bool HasMore { get; init; }

    /// <summary>Where this reader's read-marker sits. Per team, not per job.</summary>
    public required long LastReadSeq { get; init; }

    /// <summary>
    /// Unread messages for this reader on this team. Derived, never stored, and never counts
    /// the reader's own messages.
    /// </summary>
    public required int UnreadCount { get; init; }
}

/// <summary>Post a message to a team's thread.</summary>
public record PostChatMessageRequest
{
    public required string Text { get; init; }

    /// <summary>
    /// Client-generated idempotency key. Sending the same one twice yields ONE row: the second
    /// call returns 200 with the existing message and sends no second push. Required -- a retry
    /// on a flaky phone connection is the normal case, not the edge case.
    /// </summary>
    public required Guid ClientMessageId { get; init; }

    public Guid? ReplyToMessageId { get; init; }
}

/// <summary>Advance this reader's marker on a team.</summary>
public record MarkChatReadRequest
{
    /// <summary>
    /// The Seq read up to. Only ever moves forward -- a lower value is ignored rather than
    /// rejected, because two phones on the same registration will race and neither is wrong.
    /// </summary>
    public required long LastReadSeq { get; init; }
}

/// <summary>This reader's read-marker and unread count for one team.</summary>
public record ChatReadStateDto
{
    public required long LastReadSeq { get; init; }
    public required int UnreadCount { get; init; }
}

/// <summary>
/// Per-registration, per-team notification preferences. Evaluated SERVER-SIDE during fan-out:
/// a backgrounded iOS app runs no code and can filter nothing.
/// </summary>
public record ChatPreferencesDto
{
    /// <summary>Silences the doorbell, not the conversation -- the row still arrives and the unread count still moves.</summary>
    public required bool Muted { get; init; }

    /// <summary>When set, the mute lapses on its own at this Arizona instant. "Mute for a week" without having to remember to undo it.</summary>
    public DateTimeOffset? MutedUntil { get; init; }

    /// <summary>
    /// Nightly quiet window, "HH:mm", compared in ARIZONA time. A string rather than a time
    /// type because this crosses a code generator and a nullable time round-trips as anyone's
    /// guess; "22:00" does not.
    ///
    /// The client converts at save and at display. A parent in New York picking 10pm is at a
    /// different Arizona hour in July than in January -- converting once and storing the
    /// result makes the window drift an hour twice a year.
    /// </summary>
    public string? QuietStartLocal { get; init; }

    /// <summary>End of the quiet window, "HH:mm". A window that wraps midnight is normal.</summary>
    public string? QuietEndLocal { get; init; }

    /// <summary>
    /// On by default. Lets a direct mention through even when the team is muted -- the one
    /// exception people actually ask for.
    /// </summary>
    public required bool NotifyOnMention { get; init; }

    /// <summary>
    /// This reader pinning the TEAM to the top of their own thread list. Distinct from
    /// pinning a MESSAGE to the top of a thread, which lives on the message row.
    /// </summary>
    public required bool Pinned { get; init; }
}
