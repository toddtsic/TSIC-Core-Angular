namespace TSIC.Contracts.Dtos;

/// <summary>
/// A public event listing for the event browse/discovery page.
/// Matches the legacy ActiveJob shape used by mobile apps.
/// </summary>
public record EventListingDto
{
    public required Guid JobId { get; init; }
    public required string JobName { get; init; }
    public string? JobLogoUrl { get; init; }
    public string? City { get; init; }
    public string? State { get; init; }
    public string? SportName { get; init; }
    public DateTime? FirstGameDay { get; init; }
    public DateTime? LastGameDay { get; init; }
}

/// <summary>
/// A push notification alert visible to event attendees.
/// </summary>
public record EventAlertDto
{
    public required DateTime SentWhen { get; init; }
    public required string PushText { get; init; }
}

/// <summary>
/// A job-level document or link (from TeamDocs where TeamId is null).
/// </summary>
public record EventDocDto
{
    public required Guid DocId { get; init; }
    public Guid? JobId { get; init; }
    public required string Label { get; init; }
    public required string DocUrl { get; init; }
    public string? User { get; init; }
    public DateTime CreateDate { get; init; }
}

/// <summary>
/// Game clock timing configuration for an event.
/// </summary>
public record GameClockConfigDto
{
    public int? UtcoffsetHours { get; init; }
    public decimal HalfMinutes { get; init; }
    public decimal HalfTimeMinutes { get; init; }
    public decimal? QuarterMinutes { get; init; }
    public decimal? QuarterTimeMinutes { get; init; }
    public decimal TransitionMinutes { get; init; }
    public decimal PlayoffMinutes { get; init; }
    public decimal? PlayoffHalfMinutes { get; init; }
    public decimal? PlayoffHalfTimeMinutes { get; init; }
}
