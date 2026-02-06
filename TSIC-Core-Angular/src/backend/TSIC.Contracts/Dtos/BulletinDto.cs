namespace TSIC.Contracts.Dtos;

/// <summary>
/// Lightweight bulletin data for public job landing page display.
/// Only includes fields necessary for anonymous user view.
/// </summary>
public record BulletinDto
{
    public required Guid BulletinId { get; init; }
    public string? Title { get; init; }
    public string? Text { get; init; }
    public DateTime? StartDate { get; init; }
    public DateTime? EndDate { get; init; }
    public required DateTime CreateDate { get; init; }
}
