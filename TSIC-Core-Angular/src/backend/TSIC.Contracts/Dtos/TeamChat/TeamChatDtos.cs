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
    /// <summary>
    /// Always ascending -- but on WHICH axis depends on how the page was asked for:
    ///
    ///   opening page (`since` omitted) -> ascending by Seq, the conversation's own order.
    ///   catch-up     (`since` sent)    -> ascending by LastTouchSeq, the change order.
    ///
    /// This is not a wart. An opening page is a slice of the CONVERSATION and has to be the
    /// last N things said; a catch-up page is a slice of the CHANGE LOG and has to be every
    /// row touched since your cursor, edits to ancient messages included. They are different
    /// questions and they sort on different axes.
    ///
    /// Render by Seq either way, and replace in place by MessageId -- then neither axis is
    /// visible to a reader.
    /// </summary>
    public required IReadOnlyList<ChatMessageDto> Messages { get; init; }

    /// <summary>
    /// What to send as <c>since</c> on the NEXT call: the highest LastTouchSeq IN THIS PAGE,
    /// or the caller's own <c>since</c> when the page is empty (0 when <c>since</c> was
    /// omitted, which can only mean an empty thread).
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

    /// <summary>
    /// More NEWER rows exist past <see cref="NextCursor"/>. Poll again immediately.
    ///
    /// ONE MEANING, ONE DIRECTION -- forward, always. An earlier revision let this flag change
    /// direction depending on how the page was asked for, which put two meanings in one bool
    /// and made "keep asking while HasMore" an infinite loop on a newest page. Older history is
    /// <see cref="HasOlder"/>, and the two are never the same question.
    ///
    /// False on a newest page: you are already at the end of the thread.
    /// </summary>
    public required bool HasMore { get; init; }

    /// <summary>
    /// Older messages exist BEHIND this page -- fetch them with
    /// <c>GET chat/messages/history?before=</c><see cref="PrevCursor"/>.
    ///
    /// GUARANTEED: when this is true, <see cref="PrevCursor"/> is non-null. It can only be true
    /// on a page that came back with more rows than <c>take</c>, and <c>take</c> is clamped to
    /// at least 1, so such a page is never empty.
    ///
    /// ONLY EVER SET ON AN OPENING PAGE. On a catch-up page it is false -- empty or not -- and
    /// that false means "NO INFORMATION", not "nothing above you". The server cannot answer the
    /// question on a catch-up poll: whether history sits above you depends on the oldest row YOU
    /// hold, which is yours to know and not in the request.
    ///
    /// SO DO NOT RE-BIND A "LOAD EARLIER" CONTROL FROM EVERY RESPONSE. Open the thread, latch
    /// this, and from then on let the control's state follow the history responses. A client
    /// that re-binds on each poll shows the button at open and loses it one poll later, which
    /// reads as the feature flickering rather than as a paging bug.
    /// </summary>
    public required bool HasOlder { get; init; }

    /// <summary>
    /// The lowest <see cref="ChatMessageDto.Seq"/> in this page -- what to send as <c>before</c>
    /// to walk backwards. Null whenever <see cref="HasOlder"/> is false, and non-null whenever
    /// it is true.
    ///
    /// A Seq, NOT a LastTouchSeq -- and the opening page is ordered by Seq precisely so that
    /// this means what it says. On a LastTouchSeq-ordered page the lowest Seq present is merely
    /// the oldest message that happens to have been touched recently, so everything between it
    /// and the visually-oldest row on screen becomes unreachable through <c>before</c>. The two
    /// decisions are one decision.
    ///
    /// Derivable from the page you already hold -- it is the Seq of the first message. It rides
    /// the response so nothing has to rely on the client re-deriving it correctly.
    /// </summary>
    public long? PrevCursor { get; init; }

    /// <summary>Where this reader's read-marker sits. Per team, not per job.</summary>
    public required long LastReadSeq { get; init; }

    /// <summary>
    /// Unread messages for this reader on this team. Derived, never stored, and never counts
    /// the reader's own messages.
    /// </summary>
    public required int UnreadCount { get; init; }
}

/// <summary>
/// One page of SCROLLBACK -- older messages, walked backwards from a point in the thread.
///
/// A SEPARATE SHAPE FROM <see cref="ChatPageDto"/>, AND DELIBERATELY SO: there is no
/// NextCursor here, because a history page must never move the reader's live position. A
/// scrollback page is full of old rows; feeding its cursor back as <c>since</c> would rewind
/// the client and replay the season. Leaving the field out is the only version of that rule
/// a caller cannot get wrong -- it is not a warning in a comment, it is an absence.
///
/// For the same reason this carries no unread count and no read marker: scrolling up through
/// history is not reading new messages, and it must not move the badge.
/// </summary>
public record ChatHistoryPageDto
{
    /// <summary>
    /// Ascending by <see cref="ChatMessageDto.Seq"/> -- display order, ready to prepend to the
    /// top of the thread.
    /// </summary>
    public required IReadOnlyList<ChatMessageDto> Messages { get; init; }

    /// <summary>
    /// The lowest Seq in this page -- send as <c>before</c> for the page above it. Null when
    /// <see cref="HasOlder"/> is false.
    /// </summary>
    public long? PrevCursor { get; init; }

    /// <summary>Older messages still exist behind this page. Keep the control enabled.</summary>
    public required bool HasOlder { get; init; }
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
