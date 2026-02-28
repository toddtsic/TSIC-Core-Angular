namespace TSIC.Contracts.Dtos.Bulletin;

public record CreateBulletinRequest
{
    public required string Title { get; init; }
    public required string Text { get; init; }
    public required bool Active { get; init; }
    public DateTime? StartDate { get; init; }
    public DateTime? EndDate { get; init; }
}
