namespace TSIC.Contracts.Dtos.TeamSearch;

/// <summary>
/// One club-rep REGISTRATION in a job, as the director sees it. Registration-driven, so a rep
/// who signed in and registered nothing is a row here (Search Teams is team-centric and cannot
/// show them). Team counts hang off Teams.ClubrepRegistrationid; the library counts come from
/// the club the registration resolves to (Clubs.ClubTeams), which is READ-ONLY for a director
/// (ruling: Todd 2026-09-22 - aggregate + names, never an edit affordance).
/// </summary>
public record DirectorClubRepDto
{
    public required Guid RegistrationId { get; init; }
    /// <summary>0 when the registration could not be tied to a club (no library-linked team and
    /// no ClubReps row matching the registration's club name).</summary>
    public required int ClubId { get; init; }
    /// <summary>The in-event club name (Registrations.club_name) - the director may have renamed it here.</summary>
    public required string ClubName { get; init; }
    public required string RepName { get; init; }
    public required string Username { get; init; }
    public required string Email { get; init; }
    public required string Cellphone { get; init; }
    public required DateTime RegisteredOn { get; init; }

    /// <summary>Teams that are active and in a real playing agegroup.</summary>
    public required int ActiveTeamCount { get; init; }
    /// <summary>Teams parked in a "WAITLIST - {agegroup}" mirror bucket.</summary>
    public required int WaitlistedTeamCount { get; init; }
    /// <summary>Teams in the Dropped Teams graveyard.</summary>
    public required int DroppedTeamCount { get; init; }
    /// <summary>Sum of the rep's teams' team-level owed_total in this job (accounting is team-level;
    /// never the registration rollup). ARB teams keep a positive owed while they auto-draft.</summary>
    public required decimal OwedTotal { get; init; }

    /// <summary>Active (non-archived) rows in the club's library.</summary>
    public required int LibraryTeamCount { get; init; }
    /// <summary>Active library rows with no copy in this job (registered, waitlisted or dropped).</summary>
    public required int LibraryUnregisteredCount { get; init; }
    /// <summary>The unregistered rows whose grad year the event actually offers (playing up allowed;
    /// null threshold = everything counts). The "they could still bring these" number.</summary>
    public required int LibraryEligibleUnregisteredCount { get; init; }
}

/// <summary>A club's library through a director's eyes: every row, its status in THIS event, and
/// the other events it has played. No mutation anywhere on this shape.</summary>
public record DirectorClubLibraryDto
{
    public required Guid RegistrationId { get; init; }
    public required int ClubId { get; init; }
    public required string ClubName { get; init; }
    /// <summary>Oldest grad year the event offers (null = not grad-year-named; every team counts as eligible).</summary>
    public int? OldestOfferedGradYear { get; init; }
    public required List<DirectorClubLibraryTeamDto> Teams { get; init; }
}

public record DirectorClubLibraryTeamDto
{
    public required int ClubTeamId { get; init; }
    public required string ClubTeamName { get; init; }
    public required string ClubTeamGradYear { get; init; }
    public string? ClubTeamLevelOfPlay { get; init; }
    public required bool Archived { get; init; }
    /// <summary>registered | waitlisted | dropped | available | outside | archived. Mirrors the rep's own
    /// library page so the two never disagree on a row.</summary>
    public required string EventStatus { get; init; }
    /// <summary>This job's copy, when there is one - the director's Search Teams deep link.</summary>
    public Guid? EventTeamId { get; init; }
    public string? EventTeamName { get; init; }
    public string? EventAgeGroupName { get; init; }
    /// <summary>Every OTHER event this library row has a copy in, newest first.</summary>
    public required List<ClubTeamEventHistoryDto> OtherEvents { get; init; }
}
