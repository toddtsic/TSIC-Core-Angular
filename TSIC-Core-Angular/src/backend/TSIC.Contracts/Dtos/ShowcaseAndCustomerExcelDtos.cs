namespace TSIC.Contracts.Dtos;

/// <summary>
/// One rostered registrant's profile for the Players showcase "Player Profiles" exports — the EF
/// replacement for the <c>reporting.ThePlayers_PlayerShowCaseExport</c>, <c>…ExportII</c> and
/// <c>…_IncludeInactive</c> procs, which share one set of joins. Job-scoped. Values are raw; the
/// procs' text formatting is applied by the renderer.
/// </summary>
public record ShowcasePlayerExcelRowDto
{
    public required Guid RegistrationId { get; init; }
    public required DateTime RegistrationTs { get; init; }
    public string? AgegroupName { get; init; }
    public string? TeamName { get; init; }
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? RoleName { get; init; }
    public string? UniformNo { get; init; }
    public bool? Active { get; init; }
    public string? Cellphone { get; init; }
    public string? Email { get; init; }
    public string? City { get; init; }
    public string? State { get; init; }
    public string? StreetAddress { get; init; }
    public string? PostalCode { get; init; }
    public DateTime? Dob { get; init; }
    public string? MomFirstName { get; init; }
    public string? MomLastName { get; init; }
    public string? MomCellphone { get; init; }
    public string? MomEmail { get; init; }
    public string? DadFirstName { get; init; }
    public string? DadLastName { get; init; }
    public string? DadCellphone { get; init; }
    public string? DadEmail { get; init; }
    public string? GradYear { get; init; }
    public string? Position { get; init; }
    public string? ClubTeamName { get; init; }
    public string? SchoolName { get; init; }
    public string? SportAssnId { get; init; }
    public string? ShortsSize { get; init; }
    public string? TShirt { get; init; }
    public string? HeightInches { get; init; }
    public string? WeightLbs { get; init; }
    public string? StrongHand { get; init; }
    public string? ClubCoach { get; init; }
    public string? ClubCoachEmail { get; init; }
    public string? SchoolCoach { get; init; }
    public string? SchoolCoachEmail { get; init; }
    public string? HonorsAcademic { get; init; }
    public string? HonorsAthletic { get; init; }
    public string? Instagram { get; init; }
    public string? Twitter { get; init; }
}

/// <summary>
/// One distinct end user (club rep or rostered registrant) across a customer's jobs for the "Job
/// Users" dump — the EF replacement for the <c>reporting.CustomerEndUsersDump</c> proc.
/// Customer-scoped. Club-rep rows carry nulls in the player-only columns.
/// </summary>
public record CustomerEndUserExcelRowDto
{
    public string? Year { get; init; }
    public string? JobName { get; init; }
    public string? Role { get; init; }
    public string? LastName { get; init; }
    public string? FirstName { get; init; }
    /// <summary>Date of birth without its time part, as the proc de-duplicates on the day.</summary>
    public DateTime? Dob { get; init; }
    public string? GradYear { get; init; }
    public string? ClubName { get; init; }
    public string? TeamName { get; init; }
    public string? Position { get; init; }
    public string? SchoolName { get; init; }
    public string? Email { get; init; }
    public string? MomEmail { get; init; }
    public string? DadEmail { get; init; }
    public string? Cellphone { get; init; }
    public string? MomCellphone { get; init; }
    public string? DadCellphone { get; init; }
    public string? StreetAddress { get; init; }
    public string? City { get; init; }
    public string? State { get; init; }
    public string? PostalCode { get; init; }
}
