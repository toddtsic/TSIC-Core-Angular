using System;
using System.Linq.Expressions;
using TSIC.Domain.Entities;

namespace TSIC.Domain.JobRules;

/// <summary>
/// The ONE definition of "a club rep's team that is still on the books at this event":
/// active, and not in the Dropped graveyard. Waitlisted teams ARE included — they sit at $0
/// until placed, and the rep still sees and manages them.
///
/// This is the set the team-registration wizard lists and the payment step charges. The job
/// landing's club-rep card read every team ever tied to the rep instead, so it showed a
/// dropped team's count and balance that Pay Balance Due would never collect (AR-121: "2 teams,
/// $600 due" against a payment screen of 1 team, $300). Both now read this expression.
///
/// An <see cref="Expression{TDelegate}"/> so it composes into EF <c>Where(...)</c> and
/// translates to SQL — same technique as <see cref="TeamSelfRosterAvailability"/>.
/// </summary>
public static class ClubRepTeamsOnTheBooks
{
    public static Expression<Func<Teams, bool>> Predicate =>
        t => t.Active == true
             && t.Agegroup != null && !t.Agegroup!.AgegroupName!.Contains("DROPPED");
}
