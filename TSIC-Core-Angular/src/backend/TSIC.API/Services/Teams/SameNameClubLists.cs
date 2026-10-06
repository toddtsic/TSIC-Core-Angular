using TSIC.Application.Services.Clubs;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Repositories;
using TSIC.Domain.Constants;
using TSIC.Domain.Entities;

namespace TSIC.API.Services.Teams;

/// <summary>
/// The team wizard's two same-name-club lists (Todd 2026-10-06), as pure rules over already-loaded data.
/// Sign-up lets a rep create a club whose name another club already uses — typically the rep replacing
/// that club's old rep. "Same name" is <see cref="ClubNameMatcher.IsSameClubName"/>: normalized, so
/// "Fury Lax" and "Fury Lacrosse" are one club, and a filler-only name matches nothing.
/// </summary>
public static class SameNameClubLists
{
    /// <summary>Every OTHER club whose name is the same club name as <paramref name="ownClubId"/>'s — id → name.</summary>
    public static Dictionary<int, string> SameNameClubs(int ownClubId, IEnumerable<ClubIdName> clubs)
    {
        var all = clubs.ToList();
        var ownName = all.FirstOrDefault(c => c.ClubId == ownClubId)?.ClubName;
        return all
            .Where(c => c.ClubId != ownClubId && ClubNameMatcher.IsSameClubName(c.ClubName, ownName))
            .ToDictionary(c => c.ClubId, c => c.ClubName);
    }

    /// <summary>
    /// The same-name clubs' ACTIVE library teams the rep can pick: one per name + grad year, none the rep's own
    /// library already holds — archived own rows included, since they still reserve that identity and a copy
    /// would collide with them.
    /// </summary>
    public static List<SameNameLibraryTeamDto> LibraryTeams(
        IReadOnlyDictionary<int, string> sameNameClubs,
        IEnumerable<ClubTeams> theirTeams,
        IEnumerable<ClubTeams> ownLibrary)
    {
        var ownKeys = ownLibrary
            .Select(ct => IdentityKey(ct.ClubTeamName, ct.ClubTeamGradYear))
            .ToHashSet();

        return theirTeams
            .Where(ct => ct.Active && sameNameClubs.ContainsKey(ct.ClubId))
            .Where(ct => !ownKeys.Contains(IdentityKey(ct.ClubTeamName, ct.ClubTeamGradYear)))
            .GroupBy(ct => IdentityKey(ct.ClubTeamName, ct.ClubTeamGradYear))
            .Select(g => g.First())
            .Select(ct => new SameNameLibraryTeamDto
            {
                ClubTeamName = ct.ClubTeamName,
                ClubTeamGradYear = ct.ClubTeamGradYear,
                ClubTeamLevelOfPlay = ct.ClubTeamLevelOfPlay ?? string.Empty,
                SourceClubName = sameNameClubs[ct.ClubId],
            })
            .OrderBy(t => t.ClubTeamName)
            .ThenBy(t => t.ClubTeamGradYear)
            .ToList();
    }

    /// <summary>
    /// Teams other club reps already registered in this event under the same club name as this rep's
    /// registration. Matched on the event's club_name on both sides: that is the club's identity inside the
    /// event, and a director's per-event rename that sets two reps apart is respected.
    /// </summary>
    public static List<SameNameEventTeamDto> EventTeams(
        string? clubName, IEnumerable<OtherClubRepTeamInfo> otherRepTeams)
    {
        if (string.IsNullOrWhiteSpace(clubName)) return [];

        return otherRepTeams
            .Where(t => ClubNameMatcher.IsSameClubName(t.ClubName, clubName))
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
