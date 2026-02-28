namespace TSIC.Contracts.Dtos.Bulletin;

public record BatchUpdateBulletinStatusRequest
{
    public required bool Active { get; init; }
}
