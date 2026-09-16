namespace TSIC.Contracts.Dtos.Usage;

/// <summary>
/// A (event, registration, day) triple from TSICLogs: this registration made at least one
/// request about this event on this server-local calendar day. Days, not requests, are the
/// unit -- one heavy user firing hundreds of requests is still one person on one day.
/// </summary>
public record UsageRegistrationDayDto
{
    public required Guid JobId { get; init; }

    public required Guid RegistrationId { get; init; }

    /// <summary>Server-local date (midnight), like AppUsage.OccurredAt.</summary>
    public required DateTime Day { get; init; }
}
