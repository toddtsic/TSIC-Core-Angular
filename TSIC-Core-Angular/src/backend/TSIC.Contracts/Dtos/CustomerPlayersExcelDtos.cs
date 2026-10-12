namespace TSIC.Contracts.Dtos;

/// <summary>
/// One active player on one of a customer's jobs for the "Player Data - All Customer Jobs (Excel)"
/// export — the EF replacement for the
/// <c>reporting_migrate.Get_Players_ForExcelExport_AllCustomers</c> proc. Customer-scoped: every
/// job of the calling job's customer that is still open to its admins.
/// </summary>
public record CustomerPlayersExcelRowDto
{
    public string? CustomerName { get; init; }
    public string? JobName { get; init; }
    public required Guid RegistrationId { get; init; }
    public required DateTime RegistrationTs { get; init; }
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? Gender { get; init; }
    public DateTime? Dob { get; init; }
    public string? MomFirstName { get; init; }
    public string? MomLastName { get; init; }
    public string? MomCellphone { get; init; }
    public string? MomEmail { get; init; }
    public string? DadFirstName { get; init; }
    public string? DadLastName { get; init; }
    public string? DadCellphone { get; init; }
    public string? DadEmail { get; init; }
    public string? AgegroupName { get; init; }
    public string? TeamName { get; init; }
    /// <summary>The registrant's own club name — no club-rep fallback.</summary>
    public string? ClubName { get; init; }
    public required decimal PaidTotal { get; init; }
    public required decimal OwedTotal { get; init; }
    public string? GradYear { get; init; }
    public string? SchoolName { get; init; }
    public string? SchoolGrade { get; init; }
    public string? Position { get; init; }
    public string? SportAssnId { get; init; }
    public DateTime? SportAssnIdExpDate { get; init; }
    public string? JerseySize { get; init; }
    public string? ShortsSize { get; init; }
    public string? Kilt { get; init; }
    public string? TShirt { get; init; }
    public string? Sweatshirt { get; init; }
    public string? Reversible { get; init; }
    public string? Gloves { get; init; }
    public string? Gpa { get; init; }
    public string? Sat { get; init; }
    public string? Act { get; init; }
    public string? HeightInches { get; init; }
    public string? WeightLbs { get; init; }
    public string? UniformNo { get; init; }
    public string? StreetAddress { get; init; }
    public string? City { get; init; }
    public string? State { get; init; }
    public string? PostalCode { get; init; }
}
