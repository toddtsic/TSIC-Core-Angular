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
