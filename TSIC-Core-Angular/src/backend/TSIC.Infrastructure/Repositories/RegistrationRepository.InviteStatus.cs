using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Dtos.RegistrationSearch;
using TSIC.Contracts.Repositories;

namespace TSIC.Infrastructure.Repositories;

/// <summary>
/// Invitation status resolution — the ONE place the seven statuses are decided.
///
/// Both callers go through <see cref="GetInviteStatusesAsync"/>: the Invitations filter (which
/// asks for every invited registration in the job) and the Invite grid column (which asks only
/// for the ids on the page being shown). One resolver means the column can never label a row
/// differently from the filter that selected it.
///
/// NOTHING about take-up is stored. Accepted is worked out here, live, from what currently exists
/// in the target event — a registration for the invited player, a team of the invited club. Neither
/// <c>Registrations.bActive</c> nor <c>teams.active</c> is consulted and no date is compared against
/// the invitation's expiry: existence is success. Registering late, inactive, dropped, or without
/// ever clicking the link still counts.
/// </summary>
public partial class RegistrationRepository
{
    /// <summary>Ids of <c>invites.InviteStatuses</c>, seeded by scripts/27-create-invites-schema.sql.</summary>
    private static class InviteStatusIds
    {
        public const int Accepted = 1;
        public const int Sent = 2;           // schedule preview — terminal, nothing to resolve
        public const int FailedToSend = 3;
        public const int OptedOut = 4;
        public const int Expired = 5;
        public const int Offered = 6;
        public const int NotInvited = 7;
    }

    /// <summary>
    /// Options for the Invitations filter. FOUR, deliberately — one per thing a director does,
    /// not one per status. Each names a distinct population:
    ///   Any invite       — has a send record
    ///   Not yet accepted — invited to register, hasn't come back. The re-invite list.
    ///   Accepted         — came back
    ///   Not invited      — never asked. The first-wave list, and the one that catches anyone
    ///                      added to the event since the last blast.
    ///
    /// All seven statuses still exist and the Invite column labels every row with its own. Slicing
    /// the dropdown by each of them produced a list nobody could read serving decisions nobody
    /// makes: Offered and Expired lead to the same action, and so do Failed to send and Opted out.
    /// "Not yet accepted" is that action, whatever went wrong on the way.
    ///
    /// This is why the send path has no skip-the-accepted guard: the search scopes the recipients,
    /// which is this screen's whole model.
    ///
    /// Accepted and Not invited are labelled from <c>invites.InviteStatuses</c> so the dropdown and
    /// the grid column can never drift apart in wording.
    /// </summary>
    public async Task<List<FilterOption>> GetInviteStatusOptionsAsync(Guid jobId, CancellationToken ct = default)
    {
        var names = await _context.InviteStatuses.AsNoTracking()
            .ToDictionaryAsync(s => s.InviteStatusId, s => s.InviteStatusName, ct);

        string Label(int id, string fallback) => names.TryGetValue(id, out var n) ? n : fallback;

        // Counts, like every other filter category's. This resolves the job's invite statuses on
        // init load — which is NOT the search path, and is the only place this feature costs
        // anything unfiltered. A job that has sent nothing early-returns on an empty send set, so
        // it pays one indexed query returning no rows; a job that HAS sent pays one resolution
        // alongside the several full scans this call already runs for the role counts.
        var statuses = await GetInviteStatusesAsync(jobId, null, ct);

        // Same base as the role counts: active registrations with a user, so the numbers in this
        // list are comparable with the ones directly above it.
        var baseQuery = _context.Registrations.AsNoTracking()
            .Where(r => r.JobId == jobId && r.UserId != null && r.BActive == true);

        var invitedIds = statuses.Select(s => s.RegistrationId).ToList();
        var neverCount = invitedIds.Count == 0
            ? await baseQuery.CountAsync(ct)
            : await baseQuery.CountAsync(r => !invitedIds.Contains(r.RegistrationId), ct);

        var notAcceptedCount = statuses.Count(s =>
            s.InviteKindId != (int)InviteKind.SchedulePreview
            && s.InviteStatusId != InviteStatusIds.Accepted);

        var acceptedCount = statuses.Count(s => s.InviteStatusId == InviteStatusIds.Accepted);

        return
        [
            new FilterOption { Value = "any", Text = "Any invite", Count = statuses.Count },
            new FilterOption { Value = "not-accepted", Text = "Not yet accepted", Count = notAcceptedCount },
            new FilterOption { Value = "accepted", Text = Label(InviteStatusIds.Accepted, "Accepted"), Count = acceptedCount },
            new FilterOption { Value = "never", Text = Label(InviteStatusIds.NotInvited, "Not invited"), Count = neverCount }
        ];
    }

    public async Task<List<InviteStatusDto>> GetInviteStatusesAsync(
        Guid sourceJobId,
        IReadOnlyList<Guid>? registrationIds = null,
        CancellationToken ct = default)
    {
        // ── 1. The send records for this job ───────────────────────────────────────────────────
        // Small table, and (SourceRegistrationId, InvitationId) is the clustered key, so scoping to
        // a page of ids is a seek. Everything below is driven off this set — never off Registrations.
        var sendsQuery =
            from ir in _context.InvitationRegistrations.AsNoTracking()
            join inv in _context.Invitations.AsNoTracking() on ir.InvitationId equals inv.InvitationId
            where inv.SourceJobId == sourceJobId
            select new
            {
                ir.SourceRegistrationId,
                ir.InvitationOutcomeId,
                inv.TargetJobId,
                inv.InviteKindId,
                inv.ExpiresAt,
                inv.Modified
            };

        if (registrationIds is { Count: > 0 })
        {
            var ids = registrationIds.Distinct().ToList();
            sendsQuery = sendsQuery.Where(s => ids.Contains(s.SourceRegistrationId));
        }

        var sends = await sendsQuery.ToListAsync(ct);
        if (sends.Count == 0) return [];

        // THE LATEST SEND, full stop. Nothing marks a particular send as the accepted one —
        // take-up belongs to the person or the club, not to a send — so "the accepted invite else
        // the latest" would name a selection that cannot be computed.
        var latest = sends
            .GroupBy(s => s.SourceRegistrationId)
            .Select(g => g.OrderByDescending(s => s.Modified).First())
            .ToList();

        var targetJobIds = latest.Select(s => s.TargetJobId).Distinct().ToList();
        var jobNames = await _context.Jobs.AsNoTracking()
            .Where(j => targetJobIds.Contains(j.JobId))
            .Select(j => new { j.JobId, j.JobName })
            .ToDictionaryAsync(j => j.JobId, j => j.JobName ?? "", ct);

        // ── 2. Player take-up: does the invited player have a registration in the target event? ──
        // Keyed on the PLAYER's own UserId, which is stable across events. That is also what keeps a
        // sibling riding the family link from counting: only the invited player's id is looked up.
        var playerRegIds = latest
            .Where(s => s.InviteKindId == (int)InviteKind.PlayerRegistration)
            .Select(s => s.SourceRegistrationId)
            .ToList();

        var invitedUserIdByRegId = new Dictionary<Guid, string>();
        var acceptedPlayerKeys = new HashSet<(Guid JobId, string UserId)>();
        if (playerRegIds.Count > 0)
        {
            invitedUserIdByRegId = await _context.Registrations.AsNoTracking()
                .Where(r => playerRegIds.Contains(r.RegistrationId) && r.UserId != null && r.UserId != "")
                .Select(r => new { r.RegistrationId, UserId = r.UserId! })
                .ToDictionaryAsync(r => r.RegistrationId, r => r.UserId, ct);

            var userIds = invitedUserIdByRegId.Values.Distinct().ToList();
            if (userIds.Count > 0)
            {
                // Seek per user on IX_Registrations_UserId, then the target event. No bActive.
                var hits = await _context.Registrations.AsNoTracking()
                    .Where(r => targetJobIds.Contains(r.JobId) && r.UserId != null && userIds.Contains(r.UserId))
                    .Select(r => new { r.JobId, UserId = r.UserId! })
                    .Distinct()
                    .ToListAsync(ct);

                foreach (var h in hits) acceptedPlayerKeys.Add((h.JobId, h.UserId));
            }
        }

        // ── 3. Club take-up: is a team of the invited CLUB in the target event? ─────────────────
        // Any rep, any state. Resolved as a SET in ONE pass over Leagues.teams — the source side
        // (which club was invited) and the target side (which clubs are there) come back together.
        // Written as a correlated per-row EXISTS instead, this is a full re-scan of every team for
        // every rep: measured 869,505 logical reads against 5,915 for this shape. Do not "simplify"
        // it back into an .Any() inside a .Where().
        var repRegIds = latest
            .Where(s => s.InviteKindId == (int)InviteKind.ClubRepRegistration)
            .Select(s => s.SourceRegistrationId)
            .ToList();

        var invitedClubByRegId = new Dictionary<Guid, int>();
        var clubsInTargetJob = new HashSet<(Guid JobId, int ClubId)>();
        if (repRegIds.Count > 0)
        {
            var clubRows = await (
                from t in _context.Teams.AsNoTracking()
                join ct2 in _context.ClubTeams.AsNoTracking() on t.ClubTeamId equals ct2.ClubTeamId
                where t.ClubTeamId != null
                   && ((t.ClubrepRegistrationid != null && repRegIds.Contains(t.ClubrepRegistrationid.Value))
                       || targetJobIds.Contains(t.JobId))
                select new { t.JobId, t.ClubrepRegistrationid, ct2.ClubId })
                .ToListAsync(ct);

            foreach (var row in clubRows)
            {
                // Source side: this rep's club. First team wins — a rep registration maps to one
                // club in practice (1 of 2,986 since 2025 spans two).
                if (row.ClubrepRegistrationid is { } repReg && repRegIds.Contains(repReg))
                    invitedClubByRegId.TryAdd(repReg, row.ClubId);

                // Target side: every club present in the target event, whoever entered the team.
                if (targetJobIds.Contains(row.JobId))
                    clubsInTargetJob.Add((row.JobId, row.ClubId));
            }
        }

        // ── 4. Decide. First match wins, top to bottom. ────────────────────────────────────────
        var now = DateTime.Now;
        var result = new List<InviteStatusDto>(latest.Count);

        foreach (var s in latest)
        {
            var tookItUp = s.InviteKindId switch
            {
                (int)InviteKind.PlayerRegistration =>
                    invitedUserIdByRegId.TryGetValue(s.SourceRegistrationId, out var uid)
                    && acceptedPlayerKeys.Contains((s.TargetJobId, uid)),

                (int)InviteKind.ClubRepRegistration =>
                    invitedClubByRegId.TryGetValue(s.SourceRegistrationId, out var clubId)
                    && clubsInTargetJob.Contains((s.TargetJobId, clubId)),

                _ => false // schedule preview never resolves further
            };

            var statusId = ResolveStatusId(s.InviteKindId, s.InvitationOutcomeId, s.ExpiresAt, now, tookItUp);

            result.Add(new InviteStatusDto
            {
                RegistrationId = s.SourceRegistrationId,
                InviteStatusId = statusId,
                InviteKindId = s.InviteKindId,
                InviteStatusName = InviteStatusName(statusId),
                TargetJobName = jobNames.TryGetValue(s.TargetJobId, out var n) ? n : ""
            });
        }

        return result;
    }

    /// <summary>
    /// The status table, in order. Kept as a pure function so the ordering is one readable block
    /// rather than something spread across a query and a projection.
    /// </summary>
    private static int ResolveStatusId(int kindId, int outcomeId, DateTime expiresAt, DateTime now, bool tookItUp)
    {
        // A preview creates nothing to find. Sent is terminal, and it outranks expiry: the point is
        // that it was sent, and an expired preview link is still a preview that was sent.
        if (kindId == (int)InviteKind.SchedulePreview) return InviteStatusIds.Sent;

        if (outcomeId == (int)InvitationOutcome.Failed) return InviteStatusIds.FailedToSend;
        if (outcomeId == (int)InvitationOutcome.OptedOut) return InviteStatusIds.OptedOut;

        // Take-up outranks expiry deliberately: someone who registered after the link died still
        // came back, and that is the whole test.
        if (tookItUp) return InviteStatusIds.Accepted;

        return expiresAt < now ? InviteStatusIds.Expired : InviteStatusIds.Offered;
    }

    /// <summary>
    /// Filter slug to status id. Still maps the five statuses the dropdown no longer offers — they
    /// remain valid statuses on the rows, so a saved search, a chip or a hand-built request that
    /// names one keeps working. Unknown slugs map to a status nothing carries, so an unrecognised
    /// filter returns nothing rather than silently returning everything.
    /// </summary>
    private static int MapFilterToStatusId(string slug) => slug switch
    {
        "accepted" => InviteStatusIds.Accepted,
        "sent" => InviteStatusIds.Sent,
        "failed" => InviteStatusIds.FailedToSend,
        "opted-out" => InviteStatusIds.OptedOut,
        "expired" => InviteStatusIds.Expired,
        "offered" => InviteStatusIds.Offered,
        _ => -1
    };

    private static string InviteStatusName(int statusId) => statusId switch
    {
        InviteStatusIds.Accepted => "Accepted",
        InviteStatusIds.Sent => "Sent",
        InviteStatusIds.FailedToSend => "Failed to send",
        InviteStatusIds.OptedOut => "Opted out",
        InviteStatusIds.Expired => "Expired",
        InviteStatusIds.Offered => "Offered",
        _ => "Not invited"
    };
}
