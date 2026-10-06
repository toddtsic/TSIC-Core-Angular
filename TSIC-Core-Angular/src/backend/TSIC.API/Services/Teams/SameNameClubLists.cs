using TSIC.Application.Services.Clubs;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Repositories;
using TSIC.Domain.Constants;
using TSIC.Domain.Entities;

namespace TSIC.API.Services.Teams;

/// <summary>
/// The same-name club rules (Todd 2026-10-06), as pure rules over already-loaded data. Sign-up lets a rep
/// create a club whose name another club already uses — typically the rep replacing that club's old rep.
/// One rule: the EXACT club name is the identity; the loose match only helps a rep FIND their club. At
/// sign-up "same name" is <see cref="ClubNameMatcher.IsSameClubName"/> — normalized, so "Fury Lax" and
/// "Fury Lacrosse" are offered together, and a filler-only name matches nothing — and a pick takes the
/// picked club's exact name. Inside an event (<see cref="EventTeams"/>) only the exact name matches.
/// </summary>
public static class SameNameClubLists
{
    /// <summary>
    /// The library rows sign-up copies from the club the rep picked as theirs: its ACTIVE teams, one per name +
    /// grad year, none the rep's own library already holds — archived own rows included, since they still
    /// reserve that identity. A blank-named team is never copied. New rows, owned by <paramref name="targetClubId"/>.
    /// </summary>
    public static List<ClubTeams> LibraryCopy(
        IEnumerable<ClubTeams> sourceTeams, IEnumerable<ClubTeams> ownLibrary, int targetClubId, string userId)
    {
        var ownKeys = ownLibrary
            .Select(ct => IdentityKey(ct.ClubTeamName, ct.ClubTeamGradYear))
            .ToHashSet();

        return sourceTeams
            .Where(ct => ct.Active && !string.IsNullOrWhiteSpace(ct.ClubTeamName))
            .Where(ct => !ownKeys.Contains(IdentityKey(ct.ClubTeamName, ct.ClubTeamGradYear)))
            .GroupBy(ct => IdentityKey(ct.ClubTeamName, ct.ClubTeamGradYear))
            .Select(g => g.First())
            .Select(ct => new ClubTeams
            {
                ClubId = targetClubId,
                ClubTeamName = ct.ClubTeamName.Trim(),
                ClubTeamGradYear = (ct.ClubTeamGradYear ?? string.Empty).Trim(),
                ClubTeamLevelOfPlay = ct.ClubTeamLevelOfPlay,
                Active = true,
                LebUserId = userId,
                Modified = DateTime.Now,
            })
            .ToList();
    }

    /// <summary>
    /// Sign-up's "Is your club one of these?" list (Todd 2026-10-06), from the club search's results. Each
    /// same-name club carries its ACTIVE team count (what a pick copies) and when it last registered a team.
    /// Clubs whose active teams are IDENTICAL (name + grad year) show once — a fresh copy is its source until
    /// either is edited — keeping the most recently registered (then the oldest club). A club with no active
    /// teams is never folded: it has nothing to tell apart, and an empty club stays claimable. Same-name clubs
    /// come first, most recently registered first; the other results follow unchanged.
    /// </summary>
    public static List<ClubSearchResult> ForSignUp(
        IReadOnlyList<ClubSearchResult> results,
        IEnumerable<ClubTeams> libraries,
        IReadOnlyDictionary<int, DateTime> lastRegistered)
    {
        var activeKeys = libraries
            .Where(ct => ct.Active && !string.IsNullOrWhiteSpace(ct.ClubTeamName))
            .GroupBy(ct => ct.ClubId)
            .ToDictionary(g => g.Key, g => g
                .Select(ct => IdentityKey(ct.ClubTeamName, ct.ClubTeamGradYear))
                .Distinct()
                .OrderBy(k => k, StringComparer.Ordinal)
                .ToList());

        var annotated = results
            .Select(r => r.IsExactMatch
                ? r with
                {
                    ActiveTeamCount = activeKeys.TryGetValue(r.ClubId, out var keys) ? keys.Count : 0,
                    LastRegistered = lastRegistered.TryGetValue(r.ClubId, out var last) ? last : null,
                }
                : r)
            .ToList();

        static DateTime Recency(ClubSearchResult r) => r.LastRegistered ?? DateTime.MinValue;

        var folded = annotated
            .Where(r => r.IsExactMatch && r.ActiveTeamCount > 0)
            .GroupBy(r => string.Join("\n", activeKeys[r.ClubId]))
            .SelectMany(g => g.OrderByDescending(Recency).ThenBy(r => r.ClubId).Skip(1))
            .Select(r => r.ClubId)
            .ToHashSet();

        var sameName = annotated
            .Where(r => r.IsExactMatch && !folded.Contains(r.ClubId))
            .OrderByDescending(Recency)
            .ThenBy(r => r.ClubId);

        return [.. sameName, .. annotated.Where(r => !r.IsExactMatch)];
    }

    /// <summary>
    /// Teams other club reps already registered in this event under the same club name as this rep's
    /// registration. Matched on the event's club_name on both sides: that is the club's identity inside the
    /// event, and a director's per-event rename that sets two reps apart is respected. The EXACT name
    /// (Todd 2026-10-06), compared as the director's CADT trees group it — ordinal, so the rep's warning and
    /// the director's "reps" badge always agree — not the loose sign-up match: a rep who chose "None of
    /// these" under a merely similar name ("Lax Plus Club" vs "Lax Plus") said they are a different club,
    /// and a rep who picked their club at sign-up carries its exact name.
    /// </summary>
    public static List<SameNameEventTeamDto> EventTeams(
        string? clubName, IEnumerable<OtherClubRepTeamInfo> otherRepTeams)
    {
        if (string.IsNullOrWhiteSpace(clubName)) return [];

        return otherRepTeams
            .Where(t => string.Equals(t.ClubName, clubName, StringComparison.Ordinal))
            .Select(t => new SameNameEventTeamDto
            {
                TeamName = t.TeamName,
                GradYear = t.GradYear,
                AgeGroupName = AgegroupConstants.StripWaitlistPrefix(t.AgegroupName),
                RepName = $"{t.RepFirstName} {t.RepLastName}".Trim(),
            })
            .OrderBy(t => t.TeamName)
            .ToList();
    }

    /// <summary>A library team's identity — name + grad year — trimmed, case- and inner-space-insensitive.</summary>
    private static string IdentityKey(string? name, string? gradYear) =>
        string.Join(' ', (name ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant()
        + "|" + (gradYear ?? string.Empty).Trim().ToLowerInvariant();
}
