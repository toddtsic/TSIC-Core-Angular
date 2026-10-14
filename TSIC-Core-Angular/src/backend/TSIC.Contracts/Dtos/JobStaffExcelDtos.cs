namespace TSIC.Contracts.Dtos;

/// <summary>
/// One Staff or Referee registration for the "Staff Data (Excel)" export — the EF replacement for
/// the <c>reporting_migrate.Get_Staff_ForExcelExport</c> proc. Job-scoped; inactive registrations
/// are included (the sheet shows the flag). Team and agegroup are null for unassigned staff.
/// </summary>
public record JobStaffExcelRowDto
{
    public required Guid RegistrationId { get; init; }
    public bool? Active { get; init; }
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? Email { get; init; }
    public string? UserName { get; init; }
    public string? AgegroupName { get; init; }
    public string? TeamName { get; init; }
    public string? Cellphone { get; init; }
}
