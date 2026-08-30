using System;
using System.Linq.Expressions;
using TSIC.Domain.Entities;

namespace TSIC.Domain.JobRules;

/// <summary>
/// The ONE definition of "a coach can be placed on this team right now" — the adult
/// counterpart to <see cref="TeamSelfRosterAvailability"/>.
///
/// Coach registration is TEAM-level in the same way player registration is: the wizard's
/// profile step requires at least one team (<c>NeedsTeamSelection</c> is set for Club,
/// Tournament AND League), and the server enforces it, so a job with nothing placeable
/// dead-ends the coach after several steps of typing.
///
/// It exists because the two sides of that question disagreed. The public pulse gated
/// <c>StaffRegistrationOpen</c> on "does ANY team row exist for this job", while the picker
/// that fills the wizard required the team to be active and out of the system buckets. A job
/// whose only team had been DROPPED therefore satisfied the gate and emptied the picker:
/// "Register Coach" rendered, the wizard opened, and the profile step said "No teams are
/// registered yet for this event." Two live jobs sat in that state (AR-054), both because a
/// team was created and then dropped — an ordinary director action, not a misconfiguration.
///
/// An <see cref="Expression{TDelegate}"/> rather than a method so it composes into EF
/// <c>IQueryable.Where(...)</c>/<c>CountAsync(...)</c> and translates to SQL. As with the
/// player rule, a captured Expression cannot be inlined inside a projection body — which is
/// why the pulse carries the toggle only and folds this count in afterwards, rather than
/// embedding a subquery per field.
/// </summary>
public static class AdultTeamPlacementAvailability
{
    /// <summary>
    /// Active, and not sitting in a system holding bucket.
    ///
    /// DELIBERATELY LOOSER THAN THE PLAYER RULE, and it must stay that way: a coach does not
    /// self-roster, so <c>BAllowSelfRostering</c> and the team's player-registration window
    /// (<c>Effectiveasofdate</c>..<c>Expireondate</c>) are none of their business. Reusing
    /// <see cref="TeamSelfRosterAvailability"/> here would hide "Register Coach" on any
    /// tournament that has teams but hasn't enabled self-rostering, or whose player windows
    /// have closed — both perfectly normal states in which coaches must still register.
    ///
    /// The bucket clauses are spelled exactly as
    /// <c>AdultRegistrationRepository.GetAvailableTeamsAsync</c> spelled them — substring
    /// matches on the literals, not the <c>AgegroupConstants</c> prefixes — so that adopting
    /// this expression there changes which teams a coach sees by exactly nothing. Tightening
    /// them to the constants is a separate, behaviour-changing decision.
    /// </summary>
    public static Expression<Func<Teams, bool>> Placeable =>
        t => t.Active == true
             && !(t.Agegroup.AgegroupName ?? "").Contains("Waitlist")
             && !(t.Agegroup.AgegroupName ?? "").Contains("Dropped");
}
