namespace TSIC.Contracts.Dtos;

/// <summary>
/// One team-versus-team game for the TournyMachine "Schedule" export — the EF replacement for the
/// <c>reporting.Schedule_Export_ToTournyMachine</c> proc. Job-scoped. Team names are the current
/// "club:team" where the stored schedule name has drifted from it; nothing is written back.
/// </summary>
public record TournyMachineGameExcelRowDto
{
    public string? Team1 { get; init; }
    public string? Team2 { get; init; }
    public DateTime? GameDate { get; init; }
    public string? FieldName { get; init; }
    public string? AgegroupName { get; init; }
    public string? DivName { get; init; }
}

/// <summary>
/// The two sheets of the TournyMachine teams export — the EF replacement for the
/// <c>reporting.Schedule_Export_Teams_ToTournyMachine</c> proc: the scheduled teams, and the same
/// teams with their club rep's contact details.
/// </summary>
public record TournyMachineTeamsDto
{
    public required List<TournyMachineTeamExcelRowDto> Teams { get; init; }
    public required List<TournyMachineTeamContactExcelRowDto> TeamContacts { get; init; }
}

/// <summary>One distinct scheduled team with its agegroup, pool and the agegroup's gender.</summary>
public record TournyMachineTeamExcelRowDto
{
    public string? AgegroupName { get; init; }
    public string? DivName { get; init; }
    public string? TeamName { get; init; }
    public string? Gender { get; init; }
}

/// <summary>One distinct scheduled team with its club rep's contact details and gender.</summary>
public record TournyMachineTeamContactExcelRowDto
{
    public string? AgegroupName { get; init; }
    public string? DivName { get; init; }
    public string? Gender { get; init; }
    public string? TeamName { get; init; }
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? Email { get; init; }
    public string? City { get; init; }
    public string? State { get; init; }
    public string? PostalCode { get; init; }
}
