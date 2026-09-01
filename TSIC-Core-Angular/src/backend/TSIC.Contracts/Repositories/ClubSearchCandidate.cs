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

    /// <summary>
    /// True when at least one rep is linked to this club. A club with NO reps is
    /// UNCLAIMED — admin-provisioned for a rep who is about to claim it at signup.
    /// Carried explicitly rather than inferred from a null RepName, because it gates
    /// who may attach themselves to a club.
    /// </summary>
    public bool HasRep { get; set; }
}
