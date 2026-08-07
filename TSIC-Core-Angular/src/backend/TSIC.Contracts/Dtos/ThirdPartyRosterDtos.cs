namespace TSIC.Contracts.Dtos;

/// <summary>
/// One player row for the "Third-Party Roster Export" library report — the in-house
/// replacement for the retired SportsRecruits Basic-auth API dump
/// (legacy ThirdPartyApis/RostersController.GetJobRosterPlayerData). Fixed-column
/// portion only; the job's dynamic player-form fields are read off the Registrations
/// entity separately (FormValueMapper) and appended by the export service.
/// No phone numbers by design — the legacy feed never shipped them.
/// </summary>
public record ThirdPartyRosterPlayerDto
{
    public required Guid RegistrationId { get; init; }
    public DateTime? RegistrationTimestamp { get; init; }
    public DateTime? LastModifiedTimestamp { get; init; }

    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? Email { get; init; }

    /// <summary>Club name via the team's club-rep registration (Teams.ClubrepRegistration.ClubName).</summary>
    public string? TeamClubName { get; init; }
    public string? TeamName { get; init; }
    public string? AgegroupName { get; init; }
    /// <summary>Division name — the legacy feed called this "PoolName".</summary>
    public string? PoolName { get; init; }

    public string? MomFirstName { get; init; }
    public string? MomLastName { get; init; }
    public string? MomEmail { get; init; }
    public string? DadFirstName { get; init; }
    public string? DadLastName { get; init; }
    public string? DadEmail { get; init; }
}

/// <summary>
/// Job-level context for the Third-Party Roster Export sheet banner: the job name and
/// the agegroups currently flagged <c>BAllowApiRosterAccess</c> for the job's
/// league + season (the "Included:" audit line). Empty list = nothing exportable —
/// the sheet renders an explicit "no age groups enabled" message instead of data.
/// </summary>
public record ThirdPartyRosterContextDto
{
    public required string JobName { get; init; }
    public required IReadOnlyList<string> AllowedAgegroupNames { get; init; }
}

/// <summary>
/// One game row for the Schedule worksheet of the "Authorized Rosters and Schedule
/// Export" — the exact column set of the retired legacy feed
/// (ThirdPartyApis/SchedulesController.GetJobSchedule): whole-job schedule, no
/// agegroup gate. Schedules are public information on the site; the
/// <c>BAllowApiRosterAccess</c> flag gates ROSTERS only.
/// </summary>
public record ThirdPartyScheduleGameDto
{
    public required int Gid { get; init; }
    public DateTime? GDate { get; init; }
    public string? AgegroupName { get; init; }
    public string? DivName { get; init; }
    public string? FName { get; init; }
    public string? T1Type { get; init; }
    public int? T1No { get; init; }
    public string? T1Name { get; init; }
    public int? T1Score { get; init; }
    public string? T2Type { get; init; }
    public int? T2No { get; init; }
    public string? T2Name { get; init; }
    public int? T2Score { get; init; }
}
