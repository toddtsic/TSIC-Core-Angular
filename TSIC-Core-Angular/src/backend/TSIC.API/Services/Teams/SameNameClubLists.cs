using TSIC.Application.Services.Clubs;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Repositories;
using TSIC.Domain.Constants;
using TSIC.Domain.Entities;

namespace TSIC.API.Services.Teams;

/// <summary>
/// The same-name club rules (Todd 2026-10-06), as pure rules over already-loaded data. Sign-up lets a rep
/// create a club whose name another club already uses — typically the rep replacing that club's old rep.
/// "Same name" is <see cref="ClubNameMatcher.IsSameClubName"/>: normalized, so "Fury Lax" and
/// "Fury Lacrosse" are one club, and a filler-only name matches nothing.
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
