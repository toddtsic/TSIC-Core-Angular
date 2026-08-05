using System;

namespace TSIC.Domain.JobRules;

/// <summary>
/// The ONE authoritative "is this event over" predicate — the MUTATE/CREATE door.
///
/// This is deliberately NOT <see cref="JobExpiry"/>. <c>ExpiryUsers</c>/<c>ExpiryAdmin</c>
/// are <i>generous</i> data-access windows (directors set <c>ExpiryUsers</c> ~a year past
/// the event on purpose, so balances stay payable and a registrant can still reach their
/// own records afterward). NOTE: that window no longer keeps PUBLIC ROSTERS open — as of
/// 2026-08-04 tournament directors do not want rosters browsable once an event is over, so
/// <c>IsPublicRostersRestrictedAsync</c> ORs this predicate in and closes them at conclusion
/// regardless of how far out <c>ExpiryUsers</c> sits.
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
        => Resolve(schedulePublished, lastGameDate, eventEndDate, expiryUsers, now).Concluded;

    /// <summary>Which rung of the hierarchy answered, and with what date.</summary>
    public enum ConcludedSignal
    {
        /// <summary>The published schedule's last game day.</summary>
        LastGame,
        /// <summary>The director-stated <c>Jobs.EventEndDate</c>.</summary>
        EventEnd,
        /// <summary>Last-resort <c>Jobs.ExpiryUsers</c>.</summary>
        Expiry,
    }

    /// <summary>
    /// The hierarchy walk itself — verdict AND the signal that produced it. <see cref="EventConcluded"/>
    /// is the bool-only wrapper over this, so the two can never disagree about which rung answered.
    ///
    /// The signal is not decoration: it is the entire diagnosis when a director asks why their
    /// registration links vanished. "The event end date is 2026-07-20, 14 days ago" is actionable;
    /// a bare `false` is not. The admin readout (<see cref="RegistrationReadiness"/>) renders it.
    /// </summary>
    public static (bool Concluded, ConcludedSignal Signal, DateTime Date) Resolve(
        bool schedulePublished,
        DateTime? lastGameDate,
        DateTime? eventEndDate,
        DateTime expiryUsers,
        DateTime now)
    {
        var today = now.Date;

        if (schedulePublished && lastGameDate.HasValue)
            return (lastGameDate.Value.Date < today, ConcludedSignal.LastGame, lastGameDate.Value);

        if (eventEndDate.HasValue)
            return (eventEndDate.Value.Date < today, ConcludedSignal.EventEnd, eventEndDate.Value);

        // Last-resort fallback: ExpiryUsers is non-null by column type, so this branch always
        // resolves. For a generous future ExpiryUsers it reads "not concluded" (correct — we
        // have no "over" signal, so toggles/preconditions decide). The display-only job-age
        // tie-break (Smart-Bulletins phase label) covers the residual where this is too lax.
        return (expiryUsers.Date < today, ConcludedSignal.Expiry, expiryUsers);
    }
}
