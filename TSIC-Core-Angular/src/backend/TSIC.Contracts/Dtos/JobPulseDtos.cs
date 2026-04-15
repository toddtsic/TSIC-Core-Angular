namespace TSIC.Contracts.Dtos;

/// <summary>
/// Real-time availability snapshot for a job's public page.
/// Drives the "Job Pulse" widget — each true flag = one card shown.
/// </summary>
public record JobPulseDto
{
    public required bool PlayerRegistrationOpen { get; init; }
    public required bool PlayerRegRequiresToken { get; init; }
    public required bool TeamRegistrationOpen { get; init; }
    public required bool TeamRegRequiresToken { get; init; }
    public required bool ClubRepAllowAdd { get; init; }
    public required bool ClubRepAllowEdit { get; init; }
    public required bool ClubRepAllowDelete { get; init; }
    public required bool StoreEnabled { get; init; }
    public required bool StoreHasActiveItems { get; init; }
    public required bool AllowStoreWalkup { get; init; }
    public required bool SchedulePublished { get; init; }
    public required bool PlayerRegistrationPlanned { get; init; }
    public required bool AdultRegistrationPlanned { get; init; }
    public required bool PublicSuspended { get; init; }
    public DateTime? RegistrationExpiry { get; init; }
}
