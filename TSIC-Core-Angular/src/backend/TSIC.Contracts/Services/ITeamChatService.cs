using TSIC.Contracts.Dtos.TeamChat;

namespace TSIC.Contracts.Services;

/// <summary>
/// Team chat. One thread per team, text only in this revision.
///
/// The controller owns AUTHORIZATION -- cross-job, reach, and who may post. This service owns
/// the ENABLEMENT gate, because that is a property of the job rather than of the caller and
/// has to hold on every path whatever the client believes.
/// </summary>
public interface ITeamChatService
{
    /// <summary>
    /// Whether chat runs on this team's job at all. False for a team that does not exist --
    /// callers answer 403 either way, so "no such team" never leaks as a distinct outcome.
    /// </summary>
    Task<bool> IsEnabledAsync(Guid teamId, CancellationToken ct = default);

    /// <summary>
    /// One page of a thread. <paramref name="since"/> NULL opens on the NEWEST
    /// <paramref name="take"/> messages -- what a client wants on first load. A LastTouchSeq
    /// catches up strictly after that cursor.
    ///
    /// NULL is not 0. 0 means "from the beginning of the thread", so a team carrying a season
    /// of history opens on its oldest messages and pages forward to reach today.
    ///
    /// Either way the page comes back ascending, and the returned
    /// <see cref="ChatPageDto.NextCursor"/> is what to send next; see that property for why it
    /// is not the high-water mark, and <see cref="ChatPageDto.HasMore"/> for why "more" points
    /// a different way in each mode.
    /// </summary>
    Task<ChatPageDto> GetMessagesAsync(
        Guid teamId, Guid regId, string userId, long? since, int take, CancellationToken ct = default);

    /// <summary>
    /// Posts a message and fans the push out. A replayed ClientMessageId returns the original
    /// row and sends NO second push.
    ///
    /// The push is best-effort: a fan-out failure is logged and does not fail the post. The
    /// message is already stored, and every client catches up from the cursor regardless --
    /// losing the doorbell is recoverable, losing the message is not.
    /// </summary>
    Task<ChatPostResult> PostMessageAsync(
        Guid teamId, Guid regId, string userId, PostChatMessageRequest request, CancellationToken ct = default);

    /// <summary>
    /// Soft-deletes a message. The author may delete their own; a moderator may delete any.
    /// The row survives as a tombstone so phones holding it are told to drop it.
    /// </summary>
    Task<ChatDeleteOutcome> DeleteMessageAsync(
        Guid teamId, Guid messageId, string userId, bool canModerate, CancellationToken ct = default);

    Task<ChatReadStateDto> MarkReadAsync(
        Guid teamId, Guid regId, string userId, long lastReadSeq, CancellationToken ct = default);

    Task<ChatPreferencesDto> GetPreferencesAsync(Guid teamId, Guid regId, CancellationToken ct = default);

    Task<ChatPreferencesDto> SetPreferencesAsync(
        Guid teamId, Guid regId, string userId, ChatPreferencesDto prefs, CancellationToken ct = default);
}

/// <summary>What a post did, so the controller can answer 201 or 200 honestly.</summary>
public record ChatPostResult
{
    public required ChatMessageDto Message { get; init; }

    /// <summary>False when the idempotency key had already been used -- answer 200, not 201.</summary>
    public required bool Created { get; init; }

    /// <summary>Devices FCM accepted. Zero is normal on a team whose members have not installed the app.</summary>
    public required int PushesSent { get; init; }
}

/// <summary>
/// Three outcomes, because 404 and 403 are different answers and collapsing them either leaks
/// existence or hides a real permission problem.
/// </summary>
public enum ChatDeleteOutcome
{
    Deleted,
    NotFound,
    Forbidden
}
