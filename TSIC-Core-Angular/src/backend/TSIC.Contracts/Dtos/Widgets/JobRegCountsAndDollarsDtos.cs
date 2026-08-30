namespace TSIC.Contracts.Dtos.Widgets;

/// <summary>
/// One live job on the JobRegCountsAndDollars portfolio table.
///
/// Counts are carried for BOTH units on every row, with the job type named alongside:
/// the registration-allow flags are an open/closed door switch, NOT a billable-unit
/// classification (30% of live jobs have both flags off while still holding players,
/// teams and money), so column visibility must never be driven off them.
///
/// Money comes straight from the registration ledger columns (FeeTotal / PaidTotal /
/// OwedTotal) — the same fields GetDashboardMetricsAsync uses. The ledger is never
/// decomposed or recomputed here.
/// </summary>
public record JobRegCountsAndDollarsRowDto
{
    public required Guid JobId { get; init; }
    public required string JobName { get; init; }
    public required string JobPath { get; init; }

    /// <summary>reference.JobTypes.JobTypeName — tells the reader why a row shows teams vs players.</summary>
    public required string JobTypeName { get; init; }

    public required DateTime? EventStartDate { get; init; }

    /// <summary>Active registrations in the Player role.</summary>
    public required int PlayerCount { get; init; }

    /// <summary>Active teams.</summary>
    public required int TeamCount { get; init; }

    /// <summary>Ledger FeeTotal over active registrations.</summary>
    public required decimal Fees { get; init; }

    /// <summary>Ledger PaidTotal over active registrations — money banked.</summary>
    public required decimal Paid { get; init; }

    /// <summary>
    /// Ledger OwedTotal over active registrations. NOTE: on payment-plan (ARB) jobs this is
    /// dominated by contracted future installments that are NOT yet due — do not label it
    /// "past due" in the UI.
    /// </summary>
    public required decimal Owed { get; init; }
}

/// <summary>
/// The portfolio table: rollup line plus one row per live job.
/// </summary>
public record JobRegCountsAndDollarsDto
{
    public required List<JobRegCountsAndDollarsRowDto> Rows { get; init; }
    public required int TotalPlayers { get; init; }
    public required int TotalTeams { get; init; }
    public required decimal TotalFees { get; init; }
    public required decimal TotalPaid { get; init; }
    public required decimal TotalOwed { get; init; }
}
