namespace TSIC.Contracts.Dtos.Scheduling;

public record SchedulingDashboardStatusDto
{
    // Card 1 — LADT Setup
    public required int TotalAgegroups { get; init; }
    public required int TotalDivisions { get; init; }
    public required bool DivisionsAreThemed { get; init; }

    // Card 2 — Pool Assignment
    public required int AgegroupsPoolComplete { get; init; }

    // Card 3 — Fields
    public required int FieldCount { get; init; }

    // Card 4 — Pairings
    public required int PoolSizesWithPairings { get; init; }
    public required int TotalDistinctPoolSizes { get; init; }

    // Card 5 — Timeslots
    public required int AgegroupsReady { get; init; }

    // Card 6 — Schedule
    public required int AgegroupsScheduled { get; init; }
}
