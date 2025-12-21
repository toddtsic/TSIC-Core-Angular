namespace TSIC.Contracts.Repositories;

public sealed class ClubSearchCandidate
{
    public int ClubId { get; set; }
    public string ClubName { get; set; } = string.Empty;
    public string? State { get; set; }
    public int TeamCount { get; set; }
}
