using System;
using System.Linq.Expressions;
using TSIC.Domain.Constants;
using TSIC.Domain.Entities;

namespace TSIC.Domain.JobRules;

/// <summary>
/// The ONE definition of "a player may self-roster onto this team right now".
///
/// Player registration is TEAM-level: the job toggle being on is not enough — the player
/// lands on a team, and only while that team is inside its registration-availability window.
/// The rule was written out three times (the wizard's team list, the public pulse's
/// <c>PlayerTeamsAvailableForRegistration</c>, and — nearly — the admin readiness readout),
/// each with its own copy of the window logic and its own spelling of the system-bucket
/// names. Three copies of a rule the UI explains to a director is exactly the drift the
/// readout exists to eliminate, so it lives here once.
///
/// These are <see cref="Expression{TDelegate}"/> factories rather than methods so they compose
/// into EF <c>IQueryable.Where(...)</c> and translate to SQL — same technique as
/// <see cref="JobExpiry"/>. <c>now</c> is a parameter (not <c>DateTime.Now</c> inside the tree):
/// team windows are stored in local AZ time and callers already hold a single "now" for the
/// whole request, so passing it keeps every clause of one answer on one clock.
/// </summary>
public static class TeamSelfRosterAvailability
{
    /// <summary>
    /// Active, self-rostering-enabled (on the team OR its agegroup), and not a system holding
    /// bucket — everything EXCEPT the date window. Split out because the readout needs the
    /// difference between "no eligible teams at all" and "eligible teams, all closed": those
    /// are different problems with different fixes.
    ///
    /// System buckets are not bookable: the "WAITLIST - {agegroup}" twin is a payment-time
    /// artifact (the cart-split moves seat-gone players onto the $0 twin) and "Dropped" is a
    /// graveyard. Surfacing either as a pickable row produced PL-010/PL-011.
    /// </summary>
    public static Expression<Func<Teams, bool>> EligibleIgnoringWindow =>
        t => (t.Active ?? true)
             && ((t.BAllowSelfRostering ?? false) || (t.Agegroup.BAllowSelfRostering ?? false))
             && !(t.Agegroup.AgegroupName ?? "").StartsWith(AgegroupConstants.DroppedTeams)
             && !(t.Agegroup.AgegroupName ?? "").StartsWith(AgegroupConstants.WaitlistPrefix);

    /// <summary>
    /// The registration window contains <paramref name="now"/>.
    ///
    /// A window gates availability ONLY when it is a real window. A null, zero-width, or
    /// sub-second Eff/Expire pair (e.g. the <c>getdate()</c> insert default a self-rostered
    /// team gets when no window was set) is meaningless → availability rests on the eligibility
    /// clauses alone. A genuine window must still contain now.
    /// </summary>
    public static Expression<Func<Teams, bool>> WindowContains(DateTime now) =>
        t => t.Effectiveasofdate == null
             || t.Expireondate == null
             || t.Expireondate <= t.Effectiveasofdate.Value.AddSeconds(1)
             || (t.Effectiveasofdate <= now && t.Expireondate >= now);

    // NB: there is deliberately no combined "AvailableNow" expression. Writing the two clauses
    // out again as one tree would reintroduce the copy this file exists to delete. Callers AND
    // them by chaining — `.Where(EligibleIgnoringWindow).Where(WindowContains(now))` — which EF
    // composes into a single SQL predicate. (Chaining works at the top level of a query only:
    // a captured Expression cannot be inlined inside a projection body, which is why the pulse
    // asks a repository helper for its team-availability snapshot instead of embedding a
    // subquery per field.)
}
