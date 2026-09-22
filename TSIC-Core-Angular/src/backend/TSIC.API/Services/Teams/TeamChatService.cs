using Microsoft.Extensions.Options;
using TSIC.API.Services.Shared.Firebase;
using TSIC.Contracts.Configuration;
using TSIC.Contracts.Dtos.TeamChat;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Constants;
using TSIC.Domain.JobRules;
using TSIC.Domain.Push;
using TSIC.Domain.Time;

namespace TSIC.API.Services.Teams;

/// <summary>
/// Team chat. Text only in this revision -- photos are storage-capped work that has not been
/// scoped (Todd, 2026-09-21: ~1.1 TB/year at ten photos per team per week across a 5,000-team
/// season), and the schema already carries the columns for when it is.
///
/// PUSH ISOLATION: sends go through IFirebasePushService.SendEachAsync with a typed
/// <see cref="ChatPushPayload"/>. The three live senders are untouched and still use
/// SendToDevicesAsync, so shipping this cannot change what a phone receives today.
/// </summary>
public class TeamChatService : ITeamChatService
{
    /// <summary>
    /// How much of the message rides in the notification body. The push is a doorbell, not
    /// the delivery path -- the client fetches from the cursor regardless.
    /// </summary>
    private const int PushBodyLength = 100;

    /// <summary>
    /// How long the fan-out may hold the POST before it is abandoned.
    ///
    /// The fan-out is awaited inside the request deliberately -- a 201 that means the doorbell
    /// actually rang is worth more than a few hundred milliseconds, and firing it on a
    /// background task disposes the scoped DbContext out from under it. But catching every
    /// exception only covers FCM FAILING. It does nothing about FCM being SLOW: the await holds
    /// the 201, the sender's optimistic bubble never reconciles, their HTTP client times out,
    /// and they tap send again. Idempotency keeps that correct -- one row, a 200 on the replay
    /// -- but the thread looks broken while it happens.
    ///
    /// So the fan-out gets its own budget, not the request's. A late doorbell is recoverable
    /// from the cursor; a post that appears to have failed is not.
    ///
    /// NOTE FOR ANY FUTURE MOVE OFF THE REQUEST PATH: awaiting this is currently also the only
    /// thing throttling a burst of posts, since every send costs the author a round trip. v1
    /// ships without a rate limiter BECAUSE of that. Move the fan-out off the request and the
    /// ceiling goes with it -- a limiter stops being optional at the same moment.
    /// </summary>
    private static readonly TimeSpan FanOutBudget = TimeSpan.FromSeconds(5);

    private readonly ITeamChatRepository _repo;
    private readonly IFirebasePushService _push;
    private readonly string _staticsBaseUrl;
    private readonly ILogger<TeamChatService> _logger;

    public TeamChatService(
        ITeamChatRepository repo,
        IFirebasePushService push,
        IOptions<TsicSettings> tsicSettings,
        ILogger<TeamChatService> logger)
    {
        _repo = repo;
        _push = push;
        _staticsBaseUrl = tsicSettings.Value.StaticsBaseUrl.TrimEnd('/');
        _logger = logger;
    }

    public async Task<bool> IsEnabledAsync(Guid teamId, CancellationToken ct = default)
    {
        var context = await _repo.GetTeamContextAsync(teamId, ct);
        return context?.ChatEnabled == true;
    }

    // ── Read ────────────────────────────────────────────────────────────────────────────

    public async Task<ChatPageDto> GetMessagesAsync(
        Guid teamId, Guid regId, string userId, long? since, int take, CancellationToken ct = default)
    {
        var page = await _repo.GetPageAsync(teamId, since, take, ct);
        var highWater = await _repo.GetHighWaterSeqAsync(teamId, ct);
        var readState = await _repo.GetReadStateAsync(regId, teamId, userId, ct);

        // THE CURSOR IS THE LAST ROW IN THIS PAGE, NOT THE HIGH-WATER MARK.
        //
        // Echoing the team-wide maximum back as the next `since` looks harmless and is not:
        // after a partial page the client asks for everything newer than a mark it never
        // actually received, and every message between the end of the page and that mark is
        // skipped -- silently, with no gap the client can detect. It only bites under load,
        // which is when a chat thread matters most.
        //
        // An empty page returns the caller's own cursor unchanged, so a poll that finds
        // nothing does not move the client backwards to zero. An empty NEWEST page means an
        // empty thread, and 0 is the honest cursor for one.
        //
        // MAX, NOT THE LAST ROW. A catch-up page is sorted by LastTouchSeq, so its last row is
        // also its highest -- but a newest page is sorted by Seq, where the two come apart the
        // moment anyone edits an old message. Taking the last row there would hand back a
        // cursor lower than rows already in the page, and the next poll would re-deliver them.
        var nextCursor = page.Rows.Count > 0 ? page.Rows.Max(r => r.LastTouchSeq) : since ?? 0;

        // The forward and backward questions are answered separately on the wire. The repository
        // reports one "there was a take+1 row"; which direction it was walking is known here.
        var isNewestPage = since is null;

        return new ChatPageDto
        {
            Messages = page.Rows.Select(ToDto).ToList(),
            NextCursor = nextCursor,
            HighWaterSeq = highWater,
            HasMore = !isNewestPage && page.HasMore,
            HasOlder = isNewestPage && page.HasMore,
            PrevCursor = isNewestPage && page.HasMore && page.Rows.Count > 0
                ? page.Rows[0].Seq
                : null,
            LastReadSeq = readState.LastReadSeq,
            UnreadCount = readState.UnreadCount
        };
    }

    // ── Post ────────────────────────────────────────────────────────────────────────────

    public async Task<ChatPostResult> PostMessageAsync(
        Guid teamId, Guid regId, string userId, PostChatMessageRequest request, CancellationToken ct = default)
    {
        var text = request.Text.Trim();

        var result = await _repo.AppendAsync(
            teamId, regId, userId, text, request.ClientMessageId, request.ReplyToMessageId, ct);

        var dto = ToDto(result.Row);

        // A replay is one message, so it is also one push. Returning here is the whole reason
        // AppendAsync reports whether it created the row rather than just handing one back.
        if (!result.Created)
            return new ChatPostResult { Message = dto, Created = false, PushesSent = 0 };

        var pushesSent = await FanOutAsync(teamId, regId, userId, dto, ct);

        return new ChatPostResult { Message = dto, Created = true, PushesSent = pushesSent };
    }

    public async Task<ChatHistoryPageDto> GetHistoryAsync(
        Guid teamId, long beforeSeq, int take, CancellationToken ct = default)
    {
        var page = await _repo.GetHistoryPageAsync(teamId, beforeSeq, take, ct);

        return new ChatHistoryPageDto
        {
            Messages = page.Rows.Select(ToDto).ToList(),
            HasOlder = page.HasMore,
            // The oldest row in the page -- walk up from here. Null when there is nothing above
            // it, so the control disables itself on the value rather than on a separate rule.
            PrevCursor = page.HasMore && page.Rows.Count > 0 ? page.Rows[0].Seq : null
        };
    }

    /// <summary>
    /// Resolves recipients for the TEAM (not the job), filters on their own preferences, and
    /// sends one message per device with that device's badge.
    ///
    /// Never throws, and never runs longer than <see cref="FanOutBudget"/>. The message is
    /// already committed and every client catches up from the cursor, so a failed or abandoned
    /// doorbell is recoverable and a failed post is not.
    /// </summary>
    private async Task<int> FanOutAsync(
        Guid teamId, Guid authorRegId, string authorUserId, ChatMessageDto message, CancellationToken ct)
    {
        // The budget covers the WHOLE fan-out, not just the FCM call: the recipient lookups are
        // database round trips and a stalled one holds the 201 exactly as hard as a stalled
        // Firebase does. Linked to the request token so a disconnecting client still cuts it
        // short -- the budget can only ever make this finish sooner.
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(FanOutBudget);
        var fanOutCt = budget.Token;

        try
        {
            var context = await _repo.GetTeamContextAsync(teamId, fanOutCt);
            if (context == null) return 0;

            // ONE rule picks the pool AND the sender. Resolving them separately is how a push
            // ends up addressed to the right devices through the wrong Firebase project.
            var audience = PushAudienceResolver.Resolve(context.JobTypeId, context.TeamsAppEnabled);
            if (audience != PushAudience.Teams)
            {
                // Chat only exists on TSIC-Teams jobs, so anything else is a contradiction
                // between the chat flag and the job type -- worth a line in the log.
                _logger.LogWarning(
                    "Chat message on team {TeamId} resolved to push audience {Audience}; no push sent",
                    teamId, audience);
                return 0;
            }

            if (!_push.IsConfiguredFor(audience))
            {
                _logger.LogError(
                    "No TSIC-Teams Firebase credential on this box — chat push for team {TeamId} not sent",
                    teamId);
                return 0;
            }

            var targets = await _repo.GetFanoutTargetsAsync(teamId, authorRegId, authorUserId, fanOutCt);

            var now = DateTime.Now;
            var nowTime = TimeOnly.FromDateTime(now);

            var recipients = targets
                .Where(t => !IsMuted(t, now))
                .Where(t => !InQuietHours(t.QuietStartLocal, t.QuietEndLocal, nowTime))
                .Select(t => new PushRecipient(t.Token, t.RegId, t.UnreadCount))
                .ToList();

            if (recipients.Count == 0) return 0;

            return await _push.SendEachAsync(
                audience,
                recipients,
                context.TeamName,
                BuildBody(message),
                new ChatPushPayload
                {
                    // Overwritten per recipient inside SendEachAsync -- a push carries the
                    // registration it is FOR, which is never the author's.
                    RegId = Guid.Empty,
                    TeamId = teamId,
                    Seq = message.Seq
                },
                fanOutCt);
        }
        catch (OperationCanceledException ex) when (budget.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            // Our budget, not the caller going away. Warning rather than error: nothing is lost,
            // every phone still picks the message up on its next poll. A run of these means
            // Firebase is degraded, which is worth seeing without paging anyone.
            _logger.LogWarning(ex,
                "Chat push fan-out for message {MessageId} on team {TeamId} exceeded its {BudgetSeconds}s budget and was abandoned; the message is stored and clients will catch up from the cursor",
                message.MessageId, teamId, FanOutBudget.TotalSeconds);
            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Chat push fan-out failed for message {MessageId} on team {TeamId}; the message is stored and clients will catch up from the cursor",
                message.MessageId, teamId);
            return 0;
        }
    }

    // ── Delete ──────────────────────────────────────────────────────────────────────────

    public async Task<ChatDeleteOutcome> DeleteMessageAsync(
        Guid teamId, Guid messageId, string userId, bool canModerate, CancellationToken ct = default)
    {
        var author = await _repo.GetAuthorAsync(teamId, messageId, ct);
        if (author == null) return ChatDeleteOutcome.NotFound;

        // Already gone is a success, not a conflict -- a phone retrying a delete it already
        // landed should not be told it failed.
        if (author.IsDeleted) return ChatDeleteOutcome.Deleted;

        // Author by LOGIN, not by registration: a family account deleting the message it
        // posted for one child is the same person whichever child it was posted under.
        if (!canModerate && !string.Equals(author.CreatorUserId, userId, StringComparison.OrdinalIgnoreCase))
            return ChatDeleteOutcome.Forbidden;

        var deleted = await _repo.SoftDeleteAsync(teamId, messageId, userId, ct);
        return deleted ? ChatDeleteOutcome.Deleted : ChatDeleteOutcome.NotFound;
    }

    // ── Read state and preferences ──────────────────────────────────────────────────────

    public Task<ChatReadStateDto> MarkReadAsync(
        Guid teamId, Guid regId, string userId, long lastReadSeq, CancellationToken ct = default) =>
        _repo.MarkReadAsync(regId, teamId, userId, lastReadSeq, ct);

    public Task<ChatPreferencesDto> GetPreferencesAsync(
        Guid teamId, Guid regId, CancellationToken ct = default) =>
        _repo.GetPreferencesAsync(regId, teamId, ct);

    public Task<ChatPreferencesDto> SetPreferencesAsync(
        Guid teamId, Guid regId, string userId, ChatPreferencesDto prefs, CancellationToken ct = default) =>
        _repo.SetPreferencesAsync(regId, teamId, userId, prefs, ct);

    // ── Rules ───────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A mute with a date on it expires on its own. "Mute for a week" that a user has to
    /// remember to switch back on is a mute they forget and then complain about.
    /// </summary>
    private static bool IsMuted(ChatFanoutTarget target, DateTime nowArizona) =>
        target.Muted && (target.MutedUntil == null || target.MutedUntil > nowArizona);

    /// <summary>
    /// Compared in ARIZONA time, because that is the zone the window was converted into when
    /// the user picked it. The client converts at save and at display; converting once and
    /// storing the result drifts an hour twice a year for anyone who observes daylight saving.
    ///
    /// A window that wraps midnight is the normal case for a nightly one, so it is the branch
    /// that has to be right.
    /// </summary>
    private static bool InQuietHours(TimeOnly? start, TimeOnly? end, TimeOnly now)
    {
        if (start is not { } from || end is not { } to) return false;

        // Zero-width is off, not "always quiet" -- a user who set both ends the same meant to
        // clear the window, and silencing them permanently is the worse reading.
        if (from == to) return false;

        return from < to
            ? now >= from && now < to      // 13:00-15:00
            : now >= from || now < to;     // 22:00-07:00, across midnight
    }

    /// <summary>
    /// "{author}: {first 100 characters}". Truncated on a whole character, never mid-surrogate:
    /// half an emoji is a broken glyph in someone's notification shade.
    /// </summary>
    private static string BuildBody(ChatMessageDto message)
    {
        var text = message.Text;

        if (text.Length > PushBodyLength)
        {
            var cut = PushBodyLength;
            if (char.IsHighSurrogate(text[cut - 1])) cut--;
            text = string.Concat(text.AsSpan(0, cut).TrimEnd(), "…");
        }

        return $"{message.AuthorName}: {text}";
    }

    /// <summary>
    /// The display-name rule (Todd, 2026-09-20). A Player registration posts under the PLAYER's
    /// name with an " Account" suffix, because the human at the keyboard is the household login
    /// and the thread needs to say which player they are speaking for. Everyone else -- staff,
    /// director, superuser -- is their own name with no suffix.
    ///
    /// Falls back to the login's name when the registration carries no identity user, and to a
    /// neutral placeholder when neither resolves. A blank author bubble reads as a bug.
    /// </summary>
    private static string ResolveAuthorName(ChatMessageRow row)
    {
        var creatorName = Join(row.CreatorFirstName, row.CreatorLastName);

        if (!string.Equals(row.RegistrantRoleId, RoleConstants.Player, StringComparison.OrdinalIgnoreCase))
            return creatorName.Length > 0 ? creatorName : "Team member";

        var playerName = Join(row.RegistrantFirstName, row.RegistrantLastName);

        return playerName.Length > 0 ? $"{playerName} Account"
            : creatorName.Length > 0 ? creatorName
            : "Team member";
    }

    private static string Join(string? first, string? last) =>
        string.Join(' ', new[] { first, last }
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .Select(part => part!.Trim()));

    private ChatMessageDto ToDto(ChatMessageRow row) => new()
    {
        MessageId = row.MessageId,
        Seq = row.Seq,
        LastTouchSeq = row.LastTouchSeq,
        Kind = row.Kind,
        Text = row.Message,
        AuthorName = ResolveAuthorName(row),
        AuthorHeadshotUrl = BuildHeadshotUrl(row),
        ClientMessageId = row.ClientMessageId,
        ReplyToMessageId = row.ReplyToMessageId,
        Created = Arizona.At(row.Created),
        EditedAt = Arizona.At(row.EditedAt),
        IsDeleted = row.IsDeleted,
        PinnedAt = Arizona.At(row.PinnedAt)
    };

    /// <summary>
    /// Keyed by the REGISTRANT's identity userId, matching the rest of the app -- a player's
    /// headshot is the player's, not the household login's. Null when the registration carries
    /// no identity user; the client renders initials.
    ///
    /// The URL is handed over whole because the mobile client holds no statics configuration.
    /// Whether the file exists is not checked: a stat per message per page is a cost paid on
    /// every scroll to save a broken-image fallback the client needs anyway.
    /// </summary>
    private string? BuildHeadshotUrl(ChatMessageRow row) =>
        string.IsNullOrWhiteSpace(row.RegistrantUserId)
            ? null
            : $"{_staticsBaseUrl}/Headshots-AllRegistrants/{row.RegistrantUserId}.jpg";
}
