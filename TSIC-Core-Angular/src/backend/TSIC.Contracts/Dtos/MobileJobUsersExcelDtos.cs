namespace TSIC.Contracts.Dtos;

/// <summary>
/// One device-to-registration link for the "Mobile Users (Excel)" export — the EF replacement for
/// the <c>reporting_migrate.UsageByJobAndRegistrant</c> proc. Job-scoped.
/// </summary>
public record MobileJobUserExcelRowDto
{
    public string? CustomerName { get; init; }
    public string? JobName { get; init; }
    public string? Role { get; init; }
    public string? LastName { get; init; }
    public string? FirstName { get; init; }
    public required string DeviceType { get; init; }
    public required DateTime Modified { get; init; }
}
