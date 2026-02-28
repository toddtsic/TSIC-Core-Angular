namespace TSIC.Contracts.Dtos.Bulletin;

/// <summary>
/// Bulletin data for admin management view.
/// Includes all fields needed for the bulletin editor table.
/// </summary>
public record BulletinAdminDto
{
    public required Guid BulletinId { get; init; }
    public string? Title { get; init; }
    public string? Text { get; init; }
    public required bool Active { get; init; }
    public DateTime? StartDate { get; init; }
    public DateTime? EndDate { get; init; }
    public required DateTime CreateDate { get; init; }
    public required DateTime Modified { get; init; }
    public string? ModifiedByUsername { get; init; }
}
