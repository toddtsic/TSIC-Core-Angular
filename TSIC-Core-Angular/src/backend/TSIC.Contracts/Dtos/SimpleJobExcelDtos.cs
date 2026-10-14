namespace TSIC.Contracts.Dtos;

/// <summary>
/// One export run for the "Export History" export — the EF replacement for the
/// <c>reporting.ExportReportsHistory</c> proc. Job-scoped; SuperUser runs are excluded.
/// </summary>
public record ExportHistoryExcelRowDto
{
    public string? JobName { get; init; }
    /// <summary>The stored procedure name, falling back to the report name.</summary>
    public string? Export { get; init; }
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public DateTime? ExportDate { get; init; }
}

/// <summary>
/// One active registration's parent names and family mailing address for the "PlayerParents"
/// export — the EF replacement for the <c>reporting.PlayerParentMailingData</c> proc. Job-scoped.
/// </summary>
public record PlayerParentMailingExcelRowDto
{
    public string? MomLastName { get; init; }
    public string? MomFirstName { get; init; }
    public string? DadLastName { get; init; }
    public string? DadFirstName { get; init; }
    public string? StreetAddress { get; init; }
    public string? City { get; init; }
    public string? State { get; init; }
    public string? PostalCode { get; init; }
}

/// <summary>
/// One active Staff registration with assignment and apparel sizes for the "Recruiters" export —
/// the EF replacement for the <c>reporting.StaffExport</c> proc. Job-scoped.
/// </summary>
public record StaffApparelExcelRowDto
{
    public string? LastName { get; init; }
    public string? FirstName { get; init; }
    public string? Assignment { get; init; }
    public string? Cellphone { get; init; }
    public string? Email { get; init; }
    public string? JerseySize { get; init; }
    public string? ShortsSize { get; init; }
    public string? Sweatpants { get; init; }
    public string? Shoes { get; init; }
}

/// <summary>
/// One Unassigned Adult registration for the "Club Coaches" export — the EF replacement for the
/// <c>reporting.StepsCoaches_Export</c> proc. Job-scoped, active or not.
/// </summary>
public record StepsCoachExcelRowDto
{
    public bool? Active { get; init; }
    public required bool WaiverSigned { get; init; }
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? Gender { get; init; }
    public string? Email { get; init; }
    public string? Cellphone { get; init; }
    public string? StreetAddress { get; init; }
    public string? City { get; init; }
    public string? State { get; init; }
    public string? PostalCode { get; init; }
    public string? JerseySize { get; init; }
    public string? ShortsSize { get; init; }
    public string? Sweatshirt { get; init; }
    public string? Sweatpants { get; init; }
    public string? SpecialRequests { get; init; }
    /// <summary>How many Staff registrations the same user holds on this job.</summary>
    public required int TeamsCoaching { get; init; }
}

/// <summary>
/// One Player registration with club-coach and parent contacts for the MaxExposure "Club Coaches"
/// export — the EF replacement for the <c>utility.MaxExposure_ClubCoaches</c> proc. Job-scoped,
/// active or not.
/// </summary>
public record MaxExposureClubCoachExcelRowDto
{
    public string? LastName { get; init; }
    public string? FirstName { get; init; }
    public DateTime? Dob { get; init; }
    public string? SchoolName { get; init; }
    public string? GradYear { get; init; }
    public string? Cellphone { get; init; }
    public string? Email { get; init; }
    public string? ClubCoach { get; init; }
    public string? ClubCoachEmail { get; init; }
    public string? MomFirstName { get; init; }
    public string? MomLastName { get; init; }
    public string? MomCellphone { get; init; }
    public string? MomEmail { get; init; }
    public string? DadFirstName { get; init; }
    public string? DadCellphone { get; init; }
    public string? DadEmail { get; init; }
}

/// <summary>
/// One registration belonging to a family with more than one active player on the job, for the
/// sibling report — the EF replacement for the <c>utility.sibling_report</c> proc. Job-scoped.
/// </summary>
public record SiblingExcelRowDto
{
    public string? LastName { get; init; }
    public string? FirstName { get; init; }
    public required Guid RegistrationId { get; init; }
}

/// <summary>
/// One active Club Rep registration's contact details for the "Club Rep Contacts" export — the EF
/// replacement for the <c>reporting.JobClubRepContacts</c> proc. Job-scoped.
/// </summary>
public record JobClubRepContactExcelRowDto
{
    public string? Year { get; init; }
    public string? JobName { get; init; }
    public string? ClubName { get; init; }
    public string? LastName { get; init; }
    public string? FirstName { get; init; }
    public string? Email { get; init; }
    public string? Cellphone { get; init; }
    public string? StreetAddress { get; init; }
    public string? City { get; init; }
    public string? State { get; init; }
    public string? PostalCode { get; init; }
}

/// <summary>
/// One active rostered registrant with upload flags, balance and parent contacts for the camp
/// check-in "Rosters" export — the EF replacement for the
/// <c>reporting.CathyCampCheckinWithVacAndMedform</c> proc. Job-scoped; waitlist agegroups excluded.
/// </summary>
public record CampCheckinExcelRowDto
{
    public string? LeagueName { get; init; }
    public string? TeamName { get; init; }
    public string? LastName { get; init; }
    public string? FirstName { get; init; }
    public string? Position { get; init; }
    public bool? UploadedVaccineCard { get; init; }
    public bool? UploadedMedForm { get; init; }
    public required decimal OwedTotal { get; init; }
    public string? MomFirstName { get; init; }
    public string? MomLastName { get; init; }
    public string? MomCellphone { get; init; }
    public string? DadFirstName { get; init; }
    public string? DadLastName { get; init; }
    public string? DadCellphone { get; init; }
}
