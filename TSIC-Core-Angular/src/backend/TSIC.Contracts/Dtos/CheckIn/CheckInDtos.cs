namespace TSIC.Contracts.Dtos.CheckIn;

/// <summary>
/// One row in the team check-in screen (Tournament / League jobs). The check-in
/// unit is the team; balance is the clubrep registration's. <see cref="CheckedInTs"/>
/// is null until the team is checked in.
/// </summary>
public record TeamCheckinRowDto
{
    public required Guid TeamId { get; init; }
    public required string TeamName { get; init; }
    /// <summary>The team's clubrep registration — opens the payment fly-in for team/club scope. Null if unassigned.</summary>
    public Guid? ClubRepRegistrationId { get; init; }
    public string? AgegroupName { get; init; }
    public string? Gender { get; init; }
    public string? DivName { get; init; }
    public string? ClubName { get; init; }
    public string? ClubRepName { get; init; }
    public required decimal OwedTotal { get; init; }
    public required decimal PaidTotal { get; init; }
    /// <summary>When the team "runs" — start of its active window. Null if unset.</summary>
    public DateTime? StartDate { get; init; }
    /// <summary>When the team "runs" — end of its active window. Null if unset.</summary>
    public DateTime? EndDate { get; init; }
    /// <summary>Public registration opens (Effectiveasofdate). Null if unset.</summary>
    public DateTime? EffectiveDate { get; init; }
    /// <summary>Public registration closes (Expireondate). Null if unset.</summary>
    public DateTime? ExpiryDate { get; init; }
    public DateTime? CheckedInTs { get; init; }
    public Guid? CheckedInByRegId { get; init; }
}

/// <summary>
/// One row in the player check-in screen (Camp / Tryouts jobs). The check-in unit
/// is the individual registrant; balance is their own. <see cref="HasMedForm"/> is
/// the queryable BUploadedMedForm flag — the actual View action uses the med-form
/// endpoint. <see cref="CheckedInTs"/> is null until the player is checked in.
/// </summary>
public record PlayerCheckinRowDto
{
    public required Guid RegistrationId { get; init; }
    public required string PlayerUserId { get; init; }   // AspNetUsers.Id — keys the med-form file
    public required string FirstName { get; init; }
    public required string LastName { get; init; }
    public string? ClubName { get; init; }
    public string? SchoolName { get; init; }
    public string? GradYear { get; init; }
    public string? Position { get; init; }
    /// <summary>Camp day-session group label (Registrations.DayGroup). Null if unset.</summary>
    public string? DayGroup { get; init; }
    /// <summary>Camp night-session group label (Registrations.NightGroup). Null if unset.</summary>
    public string? NightGroup { get; init; }
    /// <summary>Camp roommate preference (Registrations.RoommatePref). Null if unset.</summary>
    public string? RoommatePref { get; init; }
    /// <summary>Parent contact (from the player's family) — for reaching a guardian at check-in.</summary>
    public string? MomName { get; init; }
    public string? MomCellphone { get; init; }
    public string? MomEmail { get; init; }
    public string? DadName { get; init; }
    public string? DadCellphone { get; init; }
    public string? DadEmail { get; init; }
    public required decimal OwedTotal { get; init; }
    public required decimal PaidTotal { get; init; }
    public required bool HasMedForm { get; init; }
    public DateTime? CheckedInTs { get; init; }
    public Guid? CheckedInByRegId { get; init; }
}

/// <summary>
/// Result of a check-in write. <see cref="Id"/> is the RegistrationId (player) or
/// teamId (team) that was checked in. Returned so the client can update its row
/// without re-fetching the whole roster.
/// </summary>
public record CheckinStateDto
{
    public required Guid Id { get; init; }
    public required DateTime CheckedInTs { get; init; }
    public Guid? CheckedInByRegId { get; init; }
}
