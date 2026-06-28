using System;

namespace TSIC.Domain.JobRules;

/// <summary>
/// The ONE authoritative "is this event over" predicate — the MUTATE/CREATE door.
///
/// This is deliberately NOT <see cref="JobExpiry"/>. <c>ExpiryUsers</c>/<c>ExpiryAdmin</c>
/// are <i>generous</i> data-access windows (directors set <c>ExpiryUsers</c> ~a year past
/// the event on purpose, so rosters stay viewable and balances stay payable afterward).
/// Using bare <c>ExpiryUsers</c> to decide "may I register" is the wrong-year leak: on
/// <c>lftc-summer-2025</c> the event ended 2025-06-29 but <c>ExpiryUsers</c> sits in 2026,
/// so a bare-expiry gate reads "not over" and a stale toggle resurrects registration.
///
/// The fact hierarchy (first available signal wins):
/// <list type="number">
///   <item>published schedule's last game day (most authoritative — the event literally ran)</item>
///   <item><c>EventEndDate</c> (the director-stated end — the signal bare-expiry missed)</item>
///   <item><c>ExpiryUsers</c> as a LAST-RESORT fallback only (no end date, no schedule)</item>
/// </list>
/// Day-granular (<c>.Date</c>) so the comparison matches the frontend's start-of-day phase
/// logic (<c>derivePhase</c>) exactly — strict <c>&lt;</c>, so the last game day / end date
/// itself still reads in-season, not concluded.
///
/// MUST be computed server-side and shipped to the FE as a boolean (Finding 1): the FE runs
/// on the client clock, the write-gate on the server clock (AZ) — two computations of the
/// same hierarchy on different clocks drift at the day boundary. One server-authoritative
/// bool removes the drift.
/// </summary>
public static class JobLifecycle
{
    /// <param name="schedulePublished">Public schedule access is on (<c>BScheduleAllowPublicAccess</c>).</param>
    /// <param name="lastGameDate">Latest scheduled game date, or null if no schedule.</param>
    /// <param name="eventEndDate">Director-stated event end (<c>Jobs.EventEndDate</c>), or null.</param>
    /// <param name="expiryUsers">The user data-access window end (<c>Jobs.ExpiryUsers</c>, non-null column).</param>
    /// <param name="now">Server "now" (caller passes <c>DateTime.Now</c>; injected for testability).</param>
    /// <returns>True when the event has concluded by the most authoritative date signal available.</returns>
    public static bool EventConcluded(
        bool schedulePublished,
        DateTime? lastGameDate,
        DateTime? eventEndDate,
        DateTime expiryUsers,
        DateTime now)
    {
        var today = now.Date;

        if (schedulePublished && lastGameDate.HasValue)
            return lastGameDate.Value.Date < today;

        if (eventEndDate.HasValue)
            return eventEndDate.Value.Date < today;

        // Last-resort fallback: ExpiryUsers is non-null by column type, so this branch always
        // resolves. For a generous future ExpiryUsers it reads "not concluded" (correct — we
        // have no "over" signal, so toggles/preconditions decide). The display-only job-age
        // tie-break (Smart-Bulletins phase label) covers the residual where this is too lax.
        return expiryUsers.Date < today;
    }
}
