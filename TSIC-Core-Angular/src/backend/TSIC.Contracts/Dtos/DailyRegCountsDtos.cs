namespace TSIC.Contracts.Dtos;

/// <summary>
/// One row of the daily registration-counts report — the EF replacement for the
/// <c>reporting.Get_Registrations_TSIC_Today</c> stored proc (legacy Crystal
/// "JobPlayers_TSICDaily"). One row per (customer, job, role) that took at least one active
/// registration today: <see cref="CountDaily"/> is today's count and <see cref="CountToDate"/>
/// is the running active total for that same combo. Only combos with same-day activity appear,
/// exactly as the proc's @t-join did.
/// </summary>
public record DailyRegCountRowDto
{
    public required string CustomerName { get; init; }
    public required string JobName { get; init; }
    public required string RoleName { get; init; }

    /// <summary>Active registrations recorded today for this (customer, job, role).</summary>
    public required int CountDaily { get; init; }

    /// <summary>Total active registrations to date for this (customer, job, role).</summary>
    public required int CountToDate { get; init; }
}
