namespace TSIC.Contracts.Repositories;

public sealed class ClubSearchCandidate
{
    public int ClubId { get; set; }
    public string ClubName { get; set; } = string.Empty;
    public string? State { get; set; }
    public int TeamCount { get; set; }

    /// <summary>
    /// True when at least one rep is linked to this club. A club with NO reps is
    /// UNCLAIMED — admin-provisioned for a rep who is about to claim it at signup.
    /// </summary>
    public bool HasRep { get; set; }
}
