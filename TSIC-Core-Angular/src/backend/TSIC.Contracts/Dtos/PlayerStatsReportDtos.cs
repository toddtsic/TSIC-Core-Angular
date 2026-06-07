namespace TSIC.Contracts.Dtos;

/// <summary>
/// One active player's athletic-combine stat row for the E120 stats entry form — the EF replacement
/// for the <c>reporting.PlayerStats_E120</c> proc (legacy Crystal "PlayerStats_E120"). Job-scoped,
/// Player role only, grouped agegroup → team. The four stat values come straight off
/// <c>Registrations</c> and are typically null until a player is tested, so the form renders blank
/// cells for hand-entry (it is an entry form).
/// </summary>
public record PlayerStatsE120RowDto
{
    public required Guid RegistrationId { get; init; }
    public string? AgegroupName { get; init; }
    public string? TeamName { get; init; }
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? UniformNo { get; init; }
    public double? Fastestshot { get; init; }
    public double? FiveTenFive { get; init; }
    public double? Fourtyyarddash { get; init; }
    public double? Threehundredshuttle { get; init; }
}
