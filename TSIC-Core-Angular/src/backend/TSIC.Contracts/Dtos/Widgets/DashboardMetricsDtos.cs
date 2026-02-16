namespace TSIC.Contracts.Dtos.Widgets;

/// <summary>
/// Aggregated live metrics for the widget dashboard hero section.
/// </summary>
public record DashboardMetricsDto
{
    public required RegistrationMetrics Registrations { get; init; }
    public required FinancialMetrics Financials { get; init; }
    public required SchedulingMetrics Scheduling { get; init; }
}

public record RegistrationMetrics
{
    public required int TotalActive { get; init; }
    public required int TotalInactive { get; init; }
    public required int Teams { get; init; }
    public required int Clubs { get; init; }
}

public record FinancialMetrics
{
    public required decimal TotalFees { get; init; }
    public required decimal TotalPaid { get; init; }
    public required decimal TotalOwed { get; init; }
    public required int PaidInFull { get; init; }
    public required int Underpaid { get; init; }
}

public record SchedulingMetrics
{
    public required int TotalAgegroups { get; init; }
    public required int AgegroupsScheduled { get; init; }
    public required int FieldCount { get; init; }
    public required int TotalDivisions { get; init; }
}

// ═══════════════════════════════════════════════════════════
// Registration Time-Series (for dashboard trend chart)
// ═══════════════════════════════════════════════════════════

/// <summary>
/// Daily registration counts and revenue with running cumulative totals.
/// </summary>
public record RegistrationTimeSeriesDto
{
    public required List<DailyRegistrationPointDto> DailyData { get; init; }
    public required RegistrationTrendSummaryDto Summary { get; init; }
}

/// <summary>
/// A single day's registration activity.
/// </summary>
public record DailyRegistrationPointDto
{
    public required DateTime Date { get; init; }
    public required int Count { get; init; }
    public required int CumulativeCount { get; init; }
    public required decimal Revenue { get; init; }
    public required decimal CumulativeRevenue { get; init; }
}

/// <summary>
/// Overall summary totals for the trend chart annotations.
/// </summary>
public record RegistrationTrendSummaryDto
{
    public required int TotalRegistrations { get; init; }
    public required decimal TotalRevenue { get; init; }
    public required decimal TotalOutstanding { get; init; }
    public required int PaidInFull { get; init; }
    public required int Underpaid { get; init; }
}

// ═══════════════════════════════════════════════════════════
// Age-Group Distribution (for dashboard bar chart widget)
// ═══════════════════════════════════════════════════════════

/// <summary>
/// Player and team counts broken down by age group.
/// </summary>
public record AgegroupDistributionDto
{
    public required List<AgegroupDistributionPointDto> Agegroups { get; init; }
    public required int TotalPlayers { get; init; }
    public required int TotalTeams { get; init; }
}

/// <summary>
/// A single age group's player and team counts.
/// </summary>
public record AgegroupDistributionPointDto
{
    public required string AgegroupName { get; init; }
    public required int PlayerCount { get; init; }
    public required int TeamCount { get; init; }
}
