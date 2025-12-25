namespace TSIC.Contracts.Dtos;

/// <summary>
/// Lightweight bulletin data for public job landing page display.
/// Only includes fields necessary for anonymous user view.
/// </summary>
public class BulletinDto
{
    public required Guid BulletinId { get; set; }
    public string? Title { get; set; }
    public string? Text { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public DateTime CreateDate { get; set; }
}
