namespace TSIC.Contracts.Repositories;

/// <summary>
/// One of the club-rep registrations a user holds on a single job, with the facts needed to
/// choose between them. A (user, job) pair is NOT unique — 118 pairs in production carry more
/// than one club-rep registration: 34 of them name genuinely different clubs (a rep entering two
/// clubs into one event) and the rest are double-submit twins where the teams hang off one row
/// and the other is an empty shell.
/// </summary>
public sealed class ClubRepRegistrationCandidate
{
    public Guid RegistrationId { get; set; }

    /// <summary>The registration's denormalized club stamp. May be stale — see the re-stamp in
    /// InitializeRegistrationAsync — which is exactly why the selection prefers it but never
    /// requires it.</summary>
    public string? ClubName { get; set; }

    public DateTime RegistrationTs { get; set; }

    /// <summary>Teams in any job naming this registration as their club rep.</summary>
    public int TeamCount { get; set; }
}

/// <summary>
/// The one rule for choosing among a user's club-rep registrations on a job. It lives here, in a
/// single place, because the three callers that need it (initialize-registration,
/// set-clubrep-context, check-existing) must not be free to drift into three different answers —
/// the chosen row becomes the regId claim, and everything downstream keys off it.
/// </summary>
public static class ClubRepRegistrationSelector
{
    /// <summary>
    /// Picks deterministically, in declared priority order:
    /// <list type="number">
    /// <item>the registration whose stamp matches the club the caller named, when one is given;</item>
    /// <item>among those, one that actually has teams — an empty twin never wins over a working row;</item>
    /// <item>the oldest, which is the original rather than the double-submit that followed it.</item>
    /// </list>
    /// Returns null only for an empty set. Never throws: the duplicates are real data, and refusing
    /// them would lock out reps who already have teams registered.
    /// </summary>
    /// <param name="candidates">Candidates in a stable order, oldest first.</param>
    /// <param name="preferredClubName">The club the caller selected, or null where no club is in hand.</param>
    public static ClubRepRegistrationCandidate? Select(
        IReadOnlyList<ClubRepRegistrationCandidate> candidates,
        string? preferredClubName = null)
    {
        if (candidates.Count == 0)
            return null;

        if (candidates.Count == 1)
            return candidates[0];

        IEnumerable<ClubRepRegistrationCandidate> pool = candidates;

        if (!string.IsNullOrWhiteSpace(preferredClubName))
        {
            var byClub = candidates
                .Where(c => string.Equals(c.ClubName, preferredClubName, StringComparison.OrdinalIgnoreCase))
                .ToList();

            // No match means the stamps are all stale or all name other clubs; fall through to the
            // whole set rather than returning nothing.
            if (byClub.Count > 0)
                pool = byClub;
        }

        var withTeams = pool.Where(c => c.TeamCount > 0).ToList();
        if (withTeams.Count > 0)
            pool = withTeams;

        // Each filter preserves the caller's ordering, so "first" here means oldest — a decision,
        // not an accident of the query plan.
        return pool.First();
    }
}
