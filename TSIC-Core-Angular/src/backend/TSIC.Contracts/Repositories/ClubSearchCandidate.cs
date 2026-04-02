namespace TSIC.Contracts.Repositories;

public sealed class ClubSearchCandidate
{
    public int ClubId { get; set; }
    public string ClubName { get; set; } = string.Empty;
    public string? State { get; set; }
    public int TeamCount { get; set; }

    /// <summary>Primary rep full name (Clubs.LebUserId → AspNetUsers).</summary>
    public string? RepName { get; set; }

    /// <summary>Primary rep email.</summary>
    public string? RepEmail { get; set; }
}
