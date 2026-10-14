namespace TSIC.Contracts.Dtos;

/// <summary>
/// One active player on an active team with recruiting data and the team's club rep, for the
/// "Rosters Recruiting Data Dump (Excel)" exports, admin and public — the EF replacement for the
/// <c>reporting_migrate.JobRosters_RecruitingReport_DumpExcel</c> and <c>…_Public</c> procs, which
/// share one row set. Job-scoped. Values are raw; the procs' text formatting is applied by the
/// renderer, and the public layout simply omits the contact and score columns.
/// </summary>
public record RecruitingRosterExcelRowDto
{
    public string? AgegroupName { get; init; }
    public string? TeamName { get; init; }
    public string? LastName { get; init; }
    public string? FirstName { get; init; }
    public string? UniformNo { get; init; }
    /// <summary>The player's own cellphone, falling back to the mother's.</summary>
    public string? Cellphone { get; init; }
    /// <summary>The player's own email, falling back to the mother's.</summary>
    public string? Email { get; init; }
    public string? StreetAddress { get; init; }
    public string? City { get; init; }
    public string? State { get; init; }
    public string? PostalCode { get; init; }
    public string? SchoolName { get; init; }
    public string? GradYear { get; init; }
    public string? SatMath { get; init; }
    public string? SatVerbal { get; init; }
    public string? SatWriting { get; init; }
    public string? Act { get; init; }
    public string? Gpa { get; init; }
    public string? Position { get; init; }
    /// <summary>The registration's club name, falling back to its club team name.</summary>
    public string? ClubName { get; init; }
    public string? ClubRepFirstName { get; init; }
    public string? ClubRepLastName { get; init; }
    public string? ClubRepEmail { get; init; }
    public string? ClubRepCellphone { get; init; }
    public string? ClubTeamName { get; init; }
}
