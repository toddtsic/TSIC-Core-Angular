using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Dtos.TeamChat;
using TSIC.Contracts.Repositories;
using TSIC.Domain.Time;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Data.SqlDbContext;

namespace TSIC.Infrastructure.Repositories;

/// <summary>
/// teamchat data access. See <see cref="ITeamChatRepository"/> for the contract and
/// <c>scripts/teamchat/01-create-teamchat-schema.sql</c> for the storage rules this enforces.
///
/// Arizona local time throughout -- <c>DateTime.Now</c>, never <c>UtcNow</c>. Every other
/// timestamp in this database is Arizona local, and one UTC column in the middle of a thread
/// would render seven hours in the future.
/// </summary>
public class TeamChatRepository : ITeamChatRepository
{
    /// <summary>
    /// Serialises sequence allocation and commit for one team. NEXT VALUE FOR allocates
    /// OUTSIDE the transaction, so two concurrent posts can draw 100 and 101 and then commit
    /// in the opposite order -- a reader paging by LastTouchSeq walks past the one that landed
    /// late and never comes back for it. The lock is held to COMMIT, not just to allocation.
    ///
    /// LockTimeout is five seconds: a post that cannot get the thread lock in five seconds is
    /// better off failing loudly than queueing behind an unknown number of others.
    /// </summary>
    private const string TakeThreadLockSql = @"
        DECLARE @rc int;
        EXEC @rc = sp_getapplock
            @Resource    = @resource,
            @LockMode    = 'Exclusive',
            @LockOwner   = 'Transaction',
            @LockTimeout = 5000;
        IF @rc < 0
            RAISERROR('teamchat: could not acquire the thread lock (sp_getapplock returned %d)', 16, 1, @rc);";

    private const string NextSeqSql = "SELECT NEXT VALUE FOR teamchat.MessageSequence AS Value";

    private readonly SqlDbContext _context;

    public TeamChatRepository(SqlDbContext context)
    {
        _context = context;
    }

    // ── Reads ───────────────────────────────────────────────────────────────────────────

    public async Task<ChatTeamContext?> GetTeamContextAsync(Guid teamId, CancellationToken ct = default)
    {
        return await _context.Teams.AsNoTracking()
            .Where(t => t.TeamId == teamId)
            .Select(t => new ChatTeamContext
            {
                TeamId = t.TeamId,
                JobId = t.JobId,
                TeamName = t.TeamName ?? string.Empty,
                JobTypeId = t.Job.JobTypeId,
                // Both flags are nullable in the database and NULL means off on both. Written
                // as == true rather than ?? false so the intent survives a future reader.
                TeamsAppEnabled = t.Job.BEnableTsicteams == true,
                ChatEnabled = t.Job.BEnableMobileTeamChat == true
            })
            .FirstOrDefaultAsync(ct);
    }

    public async Task<ChatMessagePage> GetPageAsync(
        Guid teamId, long? since, int take, CancellationToken ct = default)
    {
        var thread = _context.Messages.AsNoTracking().Where(m => m.TeamId == teamId);

        // take + 1 rather than a second COUNT: one extra row answers HasMore for free. In both
        // modes the extra row is the LAST one read, so trimming the tail is what drops it --
        // ascending that is the newest, descending it is the oldest.
        if (since is not { } cursor)
        {
            // NEWEST PAGE. Read backwards from the end, then reverse: the caller always gets
            // ascending rows, so nothing downstream has to know which mode produced them.
            // The index is on (TeamId, LastTouchSeq), so descending is the same range scan
            // walked the other way -- no sort, no extra cost.
            var newest = await ProjectMessages(thread.OrderByDescending(m => m.LastTouchSeq))
                .Take(take + 1)
                .ToListAsync(ct);

            var hasOlder = newest.Count > take;
            if (hasOlder) newest.RemoveAt(newest.Count - 1);
            newest.Reverse();

            return new ChatMessagePage { Rows = newest, HasMore = hasOlder };
        }

        // CATCH-UP. Strictly after the cursor, ascending.
        var rows = await ProjectMessages(thread
                .Where(m => m.LastTouchSeq > cursor)
                .OrderBy(m => m.LastTouchSeq))
            .Take(take + 1)
            .ToListAsync(ct);

        var hasMore = rows.Count > take;
        if (hasMore) rows.RemoveAt(rows.Count - 1);

        return new ChatMessagePage { Rows = rows, HasMore = hasMore };
    }

    public async Task<long> GetHighWaterSeqAsync(Guid teamId, CancellationToken ct = default)
    {
        // MaxAsync over an empty set throws; Max over a nullable projection returns null.
        return await _context.Messages.AsNoTracking()
            .Where(m => m.TeamId == teamId)
            .MaxAsync(m => (long?)m.LastTouchSeq, ct) ?? 0L;
    }

    public async Task<ChatMessageRow?> GetByClientMessageIdAsync(
        Guid teamId, Guid clientMessageId, CancellationToken ct = default)
    {
        return await ProjectMessages(_context.Messages.AsNoTracking()
                .Where(m => m.TeamId == teamId && m.ClientMessageId == clientMessageId))
            .FirstOrDefaultAsync(ct);
    }

    public async Task<ChatMessageAuthor?> GetAuthorAsync(
        Guid teamId, Guid messageId, CancellationToken ct = default)
    {
        return await _context.Messages.AsNoTracking()
            .Where(m => m.TeamId == teamId && m.MessageId == messageId)
            .Select(m => new ChatMessageAuthor
            {
                RegId = m.RegId,
                CreatorUserId = m.CreatorUserId,
                IsDeleted = m.DeletedSeq != null
            })
            .FirstOrDefaultAsync(ct);
    }

    // ── Append ──────────────────────────────────────────────────────────────────────────

    public async Task<ChatAppendResult> AppendAsync(
        Guid teamId,
        Guid regId,
        string creatorUserId,
        string text,
        Guid clientMessageId,
        Guid? replyToMessageId,
        CancellationToken ct = default)
    {
        // Cheap pre-check outside the lock. The authoritative one is inside it -- this only
        // keeps the common retry off the lock entirely.
        if (await GetByClientMessageIdAsync(teamId, clientMessageId, ct) is { } already)
            return new ChatAppendResult { Row = already, Created = false };

        // JobId comes from the TEAM, never from the caller. A request-supplied jobId is a
        // request-supplied scope, and the whole security model here hangs off it.
        var jobId = await _context.Teams.AsNoTracking()
            .Where(t => t.TeamId == teamId)
            .Select(t => (Guid?)t.JobId)
            .FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException($"teamchat: team {teamId} does not exist.");

        // A reply must belong to the same thread. Without this, a messageId lifted from another
        // team threads one team's conversation onto another's.
        if (replyToMessageId is { } parentId)
        {
            var parentOnThisTeam = await _context.Messages.AsNoTracking()
                .AnyAsync(m => m.MessageId == parentId && m.TeamId == teamId, ct);

            if (!parentOnThisTeam)
                throw new InvalidOperationException(
                    $"teamchat: reply target {parentId} is not on team {teamId}.");
        }

        Guid newMessageId;

        await using (var tx = await _context.Database.BeginTransactionAsync(ct))
        {
            await _context.Database.ExecuteSqlRawAsync(
                TakeThreadLockSql,
                [new SqlParameter("@resource", $"teamchat:{teamId}")],
                ct);

            // Authoritative idempotency check, now that nobody else can be mid-append on this
            // thread. Two retries arriving together both pass the pre-check above.
            var existingId = await _context.Messages.AsNoTracking()
                .Where(m => m.TeamId == teamId && m.ClientMessageId == clientMessageId)
                .Select(m => (Guid?)m.MessageId)
                .FirstOrDefaultAsync(ct);

            if (existingId != null)
            {
                await tx.CommitAsync(ct);
                var dup = await GetByClientMessageIdAsync(teamId, clientMessageId, ct);
                return new ChatAppendResult { Row = dup!, Created = false };
            }

            var seq = await _context.Database.SqlQueryRaw<long>(NextSeqSql).FirstAsync(ct);

            var now = DateTime.Now;
            newMessageId = Guid.NewGuid();

            _context.Messages.Add(new Messages
            {
                MessageId = newMessageId,
                TeamId = teamId,
                JobId = jobId,
                Seq = seq,
                // On insert the two are the same allocated value. They diverge the first time
                // the row is touched again.
                LastTouchSeq = seq,
                Kind = 0,
                Message = text,
                CreatorUserId = creatorUserId,
                RegId = regId,
                ClientMessageId = clientMessageId,
                ReplyToMessageId = replyToMessageId,
                Created = now,
                Modified = now,
                LebUserId = creatorUserId
            });

            await _context.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }

        var row = await ProjectMessages(_context.Messages.AsNoTracking()
                .Where(m => m.MessageId == newMessageId))
            .FirstAsync(ct);

        return new ChatAppendResult { Row = row, Created = true };
    }

    public async Task<bool> SoftDeleteAsync(
        Guid teamId, Guid messageId, string deletedByUserId, CancellationToken ct = default)
    {
        var message = await _context.Messages
            .FirstOrDefaultAsync(m => m.TeamId == teamId && m.MessageId == messageId, ct);

        if (message == null || message.DeletedSeq != null) return false;

        // A NEW sequence value, under the same thread lock the append uses. The tombstone has
        // to sort AFTER everything a reader has already seen, or a phone holding the message
        // never learns it is gone.
        await using var tx = await _context.Database.BeginTransactionAsync(ct);

        await _context.Database.ExecuteSqlRawAsync(
            TakeThreadLockSql,
            [new SqlParameter("@resource", $"teamchat:{teamId}")],
            ct);

        var seq = await _context.Database.SqlQueryRaw<long>(NextSeqSql).FirstAsync(ct);

        message.DeletedSeq = seq;
        message.LastTouchSeq = seq;
        message.DeletedByUserId = deletedByUserId;
        message.Modified = DateTime.Now;
        message.LebUserId = deletedByUserId;

        await _context.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return true;
    }

    // ── Read state ──────────────────────────────────────────────────────────────────────

    public async Task<ChatReadStateDto> GetReadStateAsync(
        Guid regId, Guid teamId, string userId, CancellationToken ct = default)
    {
        var lastReadSeq = await _context.MemberTeamState.AsNoTracking()
            .Where(s => s.RegId == regId && s.TeamId == teamId)
            .Select(s => (long?)s.LastReadSeq)
            .FirstOrDefaultAsync(ct) ?? 0L;

        return new ChatReadStateDto
        {
            LastReadSeq = lastReadSeq,
            UnreadCount = await CountUnreadAsync(teamId, userId, lastReadSeq, ct)
        };
    }

    public async Task<ChatReadStateDto> MarkReadAsync(
        Guid regId, Guid teamId, string userId, long lastReadSeq, CancellationToken ct = default)
    {
        var state = await _context.MemberTeamState
            .FirstOrDefaultAsync(s => s.RegId == regId && s.TeamId == teamId, ct);

        if (state == null)
        {
            state = NewState(regId, teamId, userId);
            state.LastReadSeq = Math.Max(0L, lastReadSeq);
            _context.MemberTeamState.Add(state);
        }
        else if (lastReadSeq > state.LastReadSeq)
        {
            // Forward only. Two phones on one registration will race, and the one that got
            // there first is not wrong.
            state.LastReadSeq = lastReadSeq;
            state.Modified = DateTime.Now;
            state.LebUserId = userId;
        }

        await _context.SaveChangesAsync(ct);

        return new ChatReadStateDto
        {
            LastReadSeq = state.LastReadSeq,
            UnreadCount = await CountUnreadAsync(teamId, userId, state.LastReadSeq, ct)
        };
    }

    // ── Preferences ─────────────────────────────────────────────────────────────────────

    public async Task<ChatPreferencesDto> GetPreferencesAsync(
        Guid regId, Guid teamId, CancellationToken ct = default)
    {
        var state = await _context.MemberTeamState.AsNoTracking()
            .FirstOrDefaultAsync(s => s.RegId == regId && s.TeamId == teamId, ct);

        return state == null ? DefaultPreferences() : ToPreferences(state);
    }

    public async Task<ChatPreferencesDto> SetPreferencesAsync(
        Guid regId, Guid teamId, string lebUserId, ChatPreferencesDto prefs, CancellationToken ct = default)
    {
        var state = await _context.MemberTeamState
            .FirstOrDefaultAsync(s => s.RegId == regId && s.TeamId == teamId, ct);

        if (state == null)
        {
            state = NewState(regId, teamId, lebUserId);
            _context.MemberTeamState.Add(state);
        }

        state.Muted = prefs.Muted;
        state.MutedUntil = prefs.MutedUntil?.DateTime;
        state.QuietStartLocal = ParseHhMm(prefs.QuietStartLocal);
        state.QuietEndLocal = ParseHhMm(prefs.QuietEndLocal);
        state.NotifyOnMention = prefs.NotifyOnMention;
        state.Pinned = prefs.Pinned;
        state.Modified = DateTime.Now;
        state.LebUserId = lebUserId;

        await _context.SaveChangesAsync(ct);
        return ToPreferences(state);
    }

    // ── Fan-out ─────────────────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<ChatFanoutTarget>> GetFanoutTargetsAsync(
        Guid teamId, Guid authorRegId, string authorUserId, CancellationToken ct = default)
    {
        // Device_Teams holds BOTH apps. RegistrationId is what separates them: the TSIC-Teams
        // app writes it at login, the TSIC-Events favourite-team toggle never does. Without
        // this filter a chat push is addressed to 167k Events favourites whose tokens belong
        // to a different Firebase project.
        var subscriptions = _context.DeviceTeams.AsNoTracking()
            .Where(dt => dt.TeamId == teamId
                && dt.RegistrationId != null
                && dt.Device.Active
                && dt.Device.Token != "");

        // The author's DEVICES, not the author's registration. A parent with two children on
        // one team is subscribed twice from one phone; dropping only their own registration
        // still buzzes them through the sibling's subscription.
        var authorDeviceIds = subscriptions
            .Where(dt => dt.RegistrationId == authorRegId)
            .Select(dt => dt.DeviceId);

        return await (
            from dt in subscriptions
            where !authorDeviceIds.Contains(dt.DeviceId)
            join r in _context.Registrations.AsNoTracking()
                on dt.RegistrationId equals r.RegistrationId
            join s in _context.MemberTeamState.AsNoTracking()
                on new { RegId = r.RegistrationId, TeamId = teamId }
                equals new { s.RegId, s.TeamId } into states
            from s in states.DefaultIfEmpty()
            select new ChatFanoutTarget
            {
                RegId = r.RegistrationId,
                Token = dt.Device.Token,
                // Never badge someone for their own words -- by LOGIN, so a family account
                // posting for one child is not badged through the other.
                UnreadCount = _context.Messages
                    .Count(m => m.TeamId == teamId
                        && m.Seq > (s == null ? 0L : s.LastReadSeq)
                        && m.DeletedSeq == null
                        && m.CreatorUserId != r.UserId),
                Muted = s != null && s.Muted,
                MutedUntil = s == null ? null : s.MutedUntil,
                QuietStartLocal = s == null ? null : s.QuietStartLocal,
                QuietEndLocal = s == null ? null : s.QuietEndLocal,
                // No state row means never configured, and the default is ON.
                NotifyOnMention = s == null || s.NotifyOnMention
            })
            .Distinct()
            .ToListAsync(ct);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The ONE message projection. Every read goes through it so a column added to the wire is
    /// added in one place and cannot be half-populated depending on which method served it.
    ///
    /// The two name pairs are both carried because the display rule needs both: a Player
    /// registration renders under the PLAYER's name (from the registration's own userId) with
    /// an " Account" suffix, and everyone else renders under the name of whoever was logged in.
    /// Resolving that here would put a role rule in the data layer.
    /// </summary>
    private static IQueryable<ChatMessageRow> ProjectMessages(IQueryable<Messages> source) =>
        source.Select(m => new ChatMessageRow
        {
            MessageId = m.MessageId,
            Seq = m.Seq,
            LastTouchSeq = m.LastTouchSeq,
            Kind = m.Kind,
            // A tombstone carries no text. Emptied HERE rather than at the controller, so no
            // future read path can forget to.
            Message = m.DeletedSeq == null ? m.Message : string.Empty,
            CreatorUserId = m.CreatorUserId,
            RegId = m.RegId,
            ClientMessageId = m.ClientMessageId,
            ReplyToMessageId = m.ReplyToMessageId,
            Created = m.Created,
            EditedAt = m.EditedAt,
            IsDeleted = m.DeletedSeq != null,
            PinnedAt = m.PinnedAt,
            RegistrantUserId = m.Reg.UserId,
            RegistrantRoleId = m.Reg.RoleId,
            RegistrantFirstName = m.Reg.User != null ? m.Reg.User.FirstName : null,
            RegistrantLastName = m.Reg.User != null ? m.Reg.User.LastName : null,
            CreatorFirstName = m.CreatorUser.FirstName,
            CreatorLastName = m.CreatorUser.LastName
        });

    private Task<int> CountUnreadAsync(Guid teamId, string userId, long lastReadSeq, CancellationToken ct) =>
        _context.Messages.AsNoTracking()
            .CountAsync(m => m.TeamId == teamId
                && m.Seq > lastReadSeq
                && m.DeletedSeq == null
                && m.CreatorUserId != userId, ct);

    private static MemberTeamState NewState(Guid regId, Guid teamId, string lebUserId) => new()
    {
        RegId = regId,
        TeamId = teamId,
        LastReadSeq = 0,
        Muted = false,
        NotifyOnMention = true,
        Pinned = false,
        Modified = DateTime.Now,
        LebUserId = lebUserId
    };

    private static ChatPreferencesDto DefaultPreferences() => new()
    {
        Muted = false,
        MutedUntil = null,
        QuietStartLocal = null,
        QuietEndLocal = null,
        NotifyOnMention = true,
        Pinned = false
    };

    private static ChatPreferencesDto ToPreferences(MemberTeamState s) => new()
    {
        Muted = s.Muted,
        MutedUntil = s.MutedUntil is { } m ? Arizona.At(m) : null,
        QuietStartLocal = s.QuietStartLocal?.ToString("HH\\:mm"),
        QuietEndLocal = s.QuietEndLocal?.ToString("HH\\:mm"),
        NotifyOnMention = s.NotifyOnMention,
        Pinned = s.Pinned
    };

    /// <summary>"HH:mm" to a TimeOnly. Anything unparseable clears the window rather than half-setting it.</summary>
    private static TimeOnly? ParseHhMm(string? value) =>
        TimeOnly.TryParseExact(value, "HH\\:mm", out var parsed) ? parsed : null;
}
