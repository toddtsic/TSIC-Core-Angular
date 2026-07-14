namespace TSIC.Domain.Constants;

/// <summary>
/// Well-known agegroup names that carry behavior across the system.
///
/// These name the system "holding" buckets — agegroups that exist for bookkeeping rather than
/// to represent a real, playing agegroup. A team sitting in one of them is not a team whose
/// roster its members are entitled to browse:
///
/// <list type="bullet">
///   <item>WAITLIST — minted as "WAITLIST - {agegroup}" by TeamPlacementService when an agegroup
///         overflows. A waitlisted family must not see the list of everyone else on the waitlist.</item>
///   <item>Dropped Teams — the graveyard for dropped teams.</item>
///   <item>Registration — the pre-placement holding bucket.</item>
/// </list>
///
/// Historical note: legacy hid these rosters two different ways — it excluded "Dropped Teams" and
/// "Registration" by name, but waitlist agegroups are named "WAITLIST - {x}" and slipped through that
/// check, so a <c>Teams.bHideRoster</c> column was bolted on in 2024 purely to plug the waitlist hole.
/// That column is dead: legacy exposed no UI to set it and its stored values are noise (legacy's
/// AddDivisionTeam wrote true on every new team). Do not resurrect it — gate on the agegroup, which is
/// what the team structurally *is*, rather than on a flag someone has to remember to set.
/// </summary>
public static class AgegroupConstants
{
    /// <summary>Prefix of the mirror agegroup minted on overflow ("WAITLIST - {agegroup}").</summary>
    public const string WaitlistPrefix = "WAITLIST";

    /// <summary>Graveyard agegroup for dropped teams.</summary>
    public const string DroppedTeams = "Dropped";

    /// <summary>Pre-placement holding agegroup.</summary>
    public const string Registration = "Registration";

    /// <summary>
    /// True when the agegroup is a system holding bucket rather than a real playing agegroup,
    /// and its teams' rosters must therefore not be exposed to players/staff or to the public.
    ///
    /// For in-memory use — call it on an already-projected AgegroupName. EF cannot translate this
    /// into SQL; query-side filters spell the same three <c>Contains</c> checks out inline and
    /// reference this type so the two stay in step.
    /// </summary>
    public static bool IsSystemBucket(string? agegroupName) =>
        !string.IsNullOrEmpty(agegroupName)
        && (agegroupName.Contains(WaitlistPrefix, StringComparison.OrdinalIgnoreCase)
            || agegroupName.Contains(DroppedTeams, StringComparison.OrdinalIgnoreCase)
            || agegroupName.Contains(Registration, StringComparison.OrdinalIgnoreCase));
}
