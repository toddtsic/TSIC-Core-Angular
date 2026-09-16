namespace TSIC.Contracts.Dtos.Widgets;

/// <summary>
/// TeamsAppUsage widget: how much the current event's rostered players and staff use the
/// TSIC-TEAMS app, so a client can judge whether the app earns its place.
///
/// CLIENT-FACING. TSICLogs is proprietary (Todd, 2026-09-16); this widget is the only
/// surface through which a client sees any of it. Aggregates only -- no routes, app
/// versions, platforms or per-person rows ever leave the server through this DTO.
///
/// The unit is PEOPLE and DAYS, never requests. The app loads its roster and alerts
/// together on every open, so request counts measure app opens by heavy users rather than
/// breadth of use, and would let a few families inflate the picture.
/// </summary>
public record TeamsAppUsageDto
{
    /// <summary>False when TSICLogs is not configured on this server -- not the same as no use.</summary>
    public required bool UsageLoggingAvailable { get; init; }

    /// <summary>Jobs.bEnableTSICTeams. When false nothing else is computed.</summary>
    public required bool TeamsAppEnabled { get; init; }

    public required int WindowDays { get; init; }

    /// <summary>
    /// Days of the window the log actually covers. Smaller than WindowDays when logging began
    /// inside the window; a day before logging began is NO DATA, not a day nobody used the app.
    /// </summary>
    public required int DaysCovered { get; init; }

    /// <summary>When usage logging began on this server (server-local), or null for an empty log.</summary>
    public DateTime? LoggingStartedAt { get; init; }

    /// <summary>Rostered players and staff who used the app on at least one day in the window.</summary>
    public required int ActiveUsers { get; init; }

    /// <summary>Players and staff currently on an active team of this event -- the population ActiveUsers is drawn from.</summary>
    public required int RosteredUsers { get; init; }

    /// <summary>Active users who came back: used the app on two or more separate days.</summary>
    public required int ReturningUsers { get; init; }

    /// <summary>Teams with at least one active player or staff member.</summary>
    public required int TeamsReached { get; init; }

    /// <summary>Active teams with at least one rostered player or staff member.</summary>
    public required int TeamsTotal { get; init; }

    /// <summary>Covered days on which at least one rostered player or staff member used the app.</summary>
    public required int DaysWithActivity { get; init; }

    public required int PlayersActive { get; init; }
    public required int PlayersRostered { get; init; }
    public required int StaffActive { get; init; }
    public required int StaffRostered { get; init; }

    /// <summary>
    /// Registrations that used the app for this event but are not on a current roster
    /// (dropped, moved to a waitlist, or unplaced). Kept out of every figure above because
    /// they have no denominator; reported so the totals are not silently short.
    /// </summary>
    public required int OffRosterUsers { get; init; }

    /// <summary>How many active users used the app on how many days. Bands beyond DaysCovered are omitted.</summary>
    public required List<TeamsAppUsageFrequencyBandDto> Frequency { get; init; }

    /// <summary>One row per team in TeamsTotal, ordered by age group then team name.</summary>
    public required List<TeamsAppUsageTeamRowDto> Teams { get; init; }
}

/// <summary>Active users whose distinct days of use fall in [MinDays, MaxDays].</summary>
public record TeamsAppUsageFrequencyBandDto
{
    public required int MinDays { get; init; }

    /// <summary>Null for the open-ended top band.</summary>
    public int? MaxDays { get; init; }

    public required int Users { get; init; }
}

public record TeamsAppUsageTeamRowDto
{
    public required Guid TeamId { get; init; }
    public required string TeamName { get; init; }
    public required string AgegroupName { get; init; }
    public required int PlayersActive { get; init; }
    public required int PlayersRostered { get; init; }
    public required int StaffActive { get; init; }
    public required int StaffRostered { get; init; }

    /// <summary>Active players and staff on this team who used the app on two or more days.</summary>
    public required int ReturningUsers { get; init; }
}

/// <summary>
/// One rostered player or staff registration on an active team of the event, from TSICV5.
/// The population side of the TeamsAppUsage widget; the log side is matched to it by
/// registration id in the service, because the two databases are never joined in SQL.
/// </summary>
public record TeamsAppRosterMemberDto
{
    public required Guid RegistrationId { get; init; }
    public required Guid TeamId { get; init; }
    public required string TeamName { get; init; }
    public required string AgegroupName { get; init; }
    public required bool IsStaff { get; init; }
}
