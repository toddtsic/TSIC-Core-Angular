using TSIC.Contracts.Dtos.TeamChat;

namespace TSIC.Contracts.Repositories;

/// <summary>
/// Data access for team chat. One thread per team; teamId is the thread key.
///
/// Every method here takes teamId and treats it as the scope boundary -- there is no
/// job-wide read, and a messageId alone is never enough to reach a row.
/// </summary>
public interface ITeamChatRepository
{
    /// <summary>
    /// Everything about a team that gates or addresses a chat operation: its name for the push
    /// title, and its job's type and two mobile flags. One query, because every chat endpoint
    /// needs all of it and none of it is worth three round trips.
    ///
    /// Null when the team does not exist. Callers treat that as a 403, never a 404 -- "no such
    /// team" and "not your team" must be indistinguishable from outside.
    /// </summary>
    Task<ChatTeamContext?> GetTeamContextAsync(Guid teamId, CancellationToken ct = default);

    /// <summary>
    /// One page, always returned ascending by LastTouchSeq, in one of two modes.
    ///
    /// <paramref name="since"/> NULL = the NEWEST <paramref name="take"/> rows -- what an app
    /// opening a thread wants. Read descending and reversed before returning, so the caller
    /// sees the same ascending shape either way.
    ///
    /// <paramref name="since"/> SET = catch-up, strictly after that cursor.
    ///
    /// Null and 0 are DIFFERENT, and conflating them is the bug this overload exists to kill:
    /// 0 means "from the beginning of the thread", so a team with a season of history opened
    /// the app on its oldest hundred messages and paged forward to reach today.
    ///
    /// Ordered by LastTouchSeq and NOT by Seq in both modes: an edit to an old message has to
    /// come back on the next poll.
    ///
    /// Returns <paramref name="take"/> + 1 rows' worth of knowledge -- the caller learns
    /// whether more exist without a second count query. See <see cref="ChatMessagePage.HasMore"/>
    /// for what "more" means in each mode.
    /// </summary>
    Task<ChatMessagePage> GetPageAsync(
        Guid teamId, long? since, int take, CancellationToken ct = default);

    /// <summary>The team's current maximum LastTouchSeq. Zero on an empty thread, never null.</summary>
    Task<long> GetHighWaterSeqAsync(Guid teamId, CancellationToken ct = default);

    /// <summary>
    /// The row a client already created under this idempotency key, or null. Scoped by team,
    /// so a key replayed against a different thread cannot resolve to someone else's message.
    /// </summary>
    Task<ChatMessageRow?> GetByClientMessageIdAsync(
        Guid teamId, Guid clientMessageId, CancellationToken ct = default);

    /// <summary>
    /// Appends a message and returns it, or returns the existing row when
    /// <paramref name="clientMessageId"/> has already been used on this team.
    ///
    /// Holds an application lock on the team for the whole of sequence allocation and commit.
    /// NEXT VALUE FOR allocates OUTSIDE the transaction, so without the lock two concurrent
    /// posts can commit their rows in the opposite order to the numbers they drew -- and a
    /// reader paging by LastTouchSeq walks straight past the one that landed late.
    ///
    /// jobId is looked up from the team inside this call. It is never accepted from a caller.
    /// </summary>
    Task<ChatAppendResult> AppendAsync(
        Guid teamId,
        Guid regId,
        string creatorUserId,
        string text,
        Guid clientMessageId,
        Guid? replyToMessageId,
        CancellationToken ct = default);

    /// <summary>
    /// Soft-deletes a message: stamps DeletedSeq with a NEW sequence value so the tombstone
    /// reaches phones that already hold it. The row is never removed.
    ///
    /// Returns false when the message does not exist on this team or is already deleted.
    /// </summary>
    Task<bool> SoftDeleteAsync(
        Guid teamId, Guid messageId, string deletedByUserId, CancellationToken ct = default);

    /// <summary>The author's registration and userId for one message on this team, or null.</summary>
    Task<ChatMessageAuthor?> GetAuthorAsync(
        Guid teamId, Guid messageId, CancellationToken ct = default);

    /// <summary>
    /// This reader's marker and unread count for one team. Returns zeroes rather than null
    /// when no state row exists -- a member who has never opened the thread is unread-from-zero,
    /// not an error.
    /// </summary>
    Task<ChatReadStateDto> GetReadStateAsync(
        Guid regId, Guid teamId, string userId, CancellationToken ct = default);

    /// <summary>
    /// Advances the marker, creating the state row if absent. FORWARD ONLY: a lower value is
    /// ignored, because two phones on one registration will race and neither is wrong.
    /// </summary>
    Task<ChatReadStateDto> MarkReadAsync(
        Guid regId, Guid teamId, string userId, long lastReadSeq, CancellationToken ct = default);

    /// <summary>This reader's preferences for one team; defaults when no row exists yet.</summary>
    Task<ChatPreferencesDto> GetPreferencesAsync(
        Guid regId, Guid teamId, CancellationToken ct = default);

    /// <summary>Upserts preferences and returns what was stored.</summary>
    Task<ChatPreferencesDto> SetPreferencesAsync(
        Guid regId, Guid teamId, string lebUserId, ChatPreferencesDto prefs, CancellationToken ct = default);

    /// <summary>
    /// Everyone to push to for a message on this team, with the per-registration unread count
    /// and the preferences the fan-out filters on. ONE query.
    ///
    /// The author's own devices are excluded HERE, by device and not by registration: a parent
    /// with two children on one team is subscribed twice from one phone, and dropping only
    /// their own registration still buzzes them through the sibling's.
    /// </summary>
    Task<IReadOnlyList<ChatFanoutTarget>> GetFanoutTargetsAsync(
        Guid teamId, Guid authorRegId, string authorUserId, CancellationToken ct = default);
}

/// <summary>A team, its job, and the flags that decide whether chat runs there at all.</summary>
public record ChatTeamContext
{
    public required Guid TeamId { get; init; }
    public required Guid JobId { get; init; }

    /// <summary>The push notification's title. Empty rather than null when a team is unnamed.</summary>
    public required string TeamName { get; init; }

    /// <summary>Picks the mobile app, with the TSIC-Teams flag as a second gate on top.</summary>
    public required int JobTypeId { get; init; }

    /// <summary>Jobs.bEnableTSICTeams. NULL is off.</summary>
    public required bool TeamsAppEnabled { get; init; }

    /// <summary>
    /// Jobs.bEnableMobileTeamChat. NULL is off, and off is the default -- chat is the one
    /// feature that lets members, including minors, publish to each other, so a club acquires
    /// the surface by deciding to, never by upgrading into it.
    /// </summary>
    public required bool ChatEnabled { get; init; }
}

/// <summary>A message as stored, before display names are resolved.</summary>
public record ChatMessageRow
{
    public required Guid MessageId { get; init; }
    public required long Seq { get; init; }
    public required long LastTouchSeq { get; init; }
    public required byte Kind { get; init; }
    public required string Message { get; init; }
    public required string CreatorUserId { get; init; }
    public required Guid RegId { get; init; }
    public required Guid ClientMessageId { get; init; }
    public Guid? ReplyToMessageId { get; init; }
    public required DateTime Created { get; init; }
    public DateTime? EditedAt { get; init; }
    public required bool IsDeleted { get; init; }
    public DateTime? PinnedAt { get; init; }

    /// <summary>The author's own identity userId, from the REGISTRATION -- the player, not the login.</summary>
    public string? RegistrantUserId { get; init; }

    /// <summary>The author registration's role id, which decides whether the "Account" suffix applies.</summary>
    public string? RegistrantRoleId { get; init; }

    public string? RegistrantFirstName { get; init; }
    public string? RegistrantLastName { get; init; }
    public string? CreatorFirstName { get; init; }
    public string? CreatorLastName { get; init; }
}

/// <summary>A page of rows, always ascending by LastTouchSeq, plus whether the thread holds more.</summary>
public record ChatMessagePage
{
    public required IReadOnlyList<ChatMessageRow> Rows { get; init; }

    /// <summary>
    /// More rows exist in THE DIRECTION THIS PAGE WAS READ -- which is not the same direction
    /// in both modes, and is the one thing about this record that can be got wrong:
    ///
    ///   catch-up (since SET)    -> more NEWER rows ahead; poll again immediately.
    ///   newest page (since NULL) -> more OLDER rows behind; that is history, not a backlog.
    ///
    /// Only one direction is reachable per mode -- a catch-up caller cannot ask for older, and
    /// a newest-page caller is already at the end -- so a single flag is never ambiguous at the
    /// call site. A second flag would be permanently false in one mode and invite exactly the
    /// kind of "poll again" loop that a newest-page caller must NOT run.
    /// </summary>
    public required bool HasMore { get; init; }
}

/// <summary>What <see cref="ITeamChatRepository.AppendAsync"/> did.</summary>
public record ChatAppendResult
{
    public required ChatMessageRow Row { get; init; }

    /// <summary>
    /// False when the idempotency key had already been used. The caller must not push again --
    /// a retried send is one message, not two.
    /// </summary>
    public required bool Created { get; init; }
}

/// <summary>Who wrote a message, for the delete authorization check.</summary>
public record ChatMessageAuthor
{
    public required Guid RegId { get; init; }
    public required string CreatorUserId { get; init; }
    public required bool IsDeleted { get; init; }
}

/// <summary>One device to consider pushing to, with everything the filter needs.</summary>
public record ChatFanoutTarget
{
    public required Guid RegId { get; init; }
    public required string Token { get; init; }
    public required int UnreadCount { get; init; }
    public required bool Muted { get; init; }
    public DateTime? MutedUntil { get; init; }
    public TimeOnly? QuietStartLocal { get; init; }
    public TimeOnly? QuietEndLocal { get; init; }
    public required bool NotifyOnMention { get; init; }
}
