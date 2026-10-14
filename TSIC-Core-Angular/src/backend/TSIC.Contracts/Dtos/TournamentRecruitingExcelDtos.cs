namespace TSIC.Contracts.Dtos;

/// <summary>
/// One active rostered player or staff member with recruiting data and the team's club rep, for
/// the "College Recruiter Data Dump (excel)" export — the EF replacement for the
/// <c>reporting_migrate.JobRosters_ExportTournament</c> proc. Job-scoped. Values are raw; the
/// proc's text formatting (phones, names, yes/no) is applied by the renderer.
/// </summary>
public record TournamentRecruitingExcelRowDto
{
    public string? Role { get; init; }
    public required Guid RegistrationId { get; init; }
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    /// <summary>The person's own value, falling back to the family account's.</summary>
    public string? Email { get; init; }
    public string? Gender { get; init; }
    public DateTime? Dob { get; init; }
    /// <summary>The person's own value, falling back to the family account's.</summary>
    public string? StreetAddress { get; init; }
    /// <summary>The person's own value, falling back to the family account's.</summary>
    public string? City { get; init; }
    /// <summary>The person's own value, falling back to the family account's.</summary>
    public string? State { get; init; }
    /// <summary>The person's own value, falling back to the family account's.</summary>
    public string? PostalCode { get; init; }
    /// <summary>The person's own cellphone, falling back to the mother's.</summary>
    public string? Cellphone { get; init; }
    public string? SportAssnId { get; init; }
    public string? AgegroupName { get; init; }
    public string? DivName { get; init; }
    /// <summary>The club of the team's club rep.</summary>
    public string? ClubName { get; init; }
    /// <summary>The club recorded on the person's own registration.</summary>
    public string? PlayerClubName { get; init; }
    public string? TeamName { get; init; }
    public string? MomFirstName { get; init; }
    public string? MomLastName { get; init; }
    public string? MomEmail { get; init; }
    public string? MomCellphone { get; init; }
    public string? DadFirstName { get; init; }
    public string? DadLastName { get; init; }
    public string? DadEmail { get; init; }
    public string? DadCellphone { get; init; }
    public string? UniformNo { get; init; }
    public string? GradYear { get; init; }
    public string? SchoolName { get; init; }
    public string? Position { get; init; }
    public string? ClubTeamName { get; init; }
    public string? Gpa { get; init; }
    public string? Psat { get; init; }
    public string? ClassRank { get; init; }
    public string? HeightInches { get; init; }
    public string? WeightLbs { get; init; }
    public string? Act { get; init; }
    public string? SatMath { get; init; }
    public string? SatVerbal { get; init; }
    public string? SatWriting { get; init; }
    public bool? CollegeCommitted { get; init; }
    public string? CollegeCommit { get; init; }
    public string? ClubRepFirstName { get; init; }
    public string? ClubRepLastName { get; init; }
    public string? ClubRepEmail { get; init; }
    public string? ClubRepCellphone { get; init; }
}
