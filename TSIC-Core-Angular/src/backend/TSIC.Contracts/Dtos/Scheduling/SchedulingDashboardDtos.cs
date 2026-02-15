namespace TSIC.Contracts.Dtos.Scheduling;

public record SchedulingDashboardStatusDto
{
    public required int FieldCount { get; init; }
    public required int DivisionsWithPairings { get; init; }
    public required int TotalPairingCount { get; init; }
    public required int AgegroupsWithTimeslots { get; init; }
    public required int TimeslotDateCount { get; init; }
    public required int ScheduledGameCount { get; init; }
    public required int DivisionsScheduled { get; init; }
    public required int TotalDivisions { get; init; }
    public required int TotalAgegroups { get; init; }
    public required int TeamsAssigned { get; init; }
    public required int TeamsUnassigned { get; init; }
}
