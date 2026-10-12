namespace TSIC.Contracts.Dtos;

/// <summary>
/// One active player's row for the "Player Data (Excel)" export in the STEPS layout — the EF
/// replacement for the <c>reporting_migrate.Get_JobPlayers_STEPS_Excel</c> proc. Job-scoped, Player
/// role only. Properties are declared in the legacy 40-column order; the sheet's header names
/// (which include non-identifiers such as <c>t-shirt</c>) are owned by the renderer.
/// </summary>
public record JobPlayersStepsExcelRowDto
{
    public required Guid RegistrationId { get; init; }
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
    public string? ClubName { get; init; }
    public string? TeamName { get; init; }
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
    public string? Reversible { get; init; }
    public string? Gloves { get; init; }
    public string? UniformNo { get; init; }
    public string? StreetAddress { get; init; }
    public string? City { get; init; }
    public string? State { get; init; }
    public string? PostalCode { get; init; }
    public string? Email { get; init; }
    public string? Cellphone { get; init; }
    public string? SpecialRequests { get; init; }
    public string? MedicalNote { get; init; }
    public string? Shoes { get; init; }
}
