using TSIC.Contracts.Dtos.Scheduling;

namespace TSIC.Contracts.Dtos;

/// <summary>
/// CADT tree for public roster navigation (Club → Agegroup → Division → Team).
/// </summary>
public record PublicRosterTreeDto
{
    public required List<CadtClubNode> Clubs { get; init; }
}

/// <summary>
/// Public-safe player/staff row — no PII (no contacts, parent info, userId, attendance).
/// </summary>
public record PublicRosterPlayerDto
{
    /// <summary>"LastName, FirstName" for players; "Staff: LastName, FirstName" for staff.</summary>
    public required string DisplayName { get; init; }
    /// <summary>"Player" or "Staff".</summary>
    public required string RoleLabel { get; init; }
    public string? Position { get; init; }
    public string? UniformNo { get; init; }
    public string? ClubName { get; init; }
    public string? TeamName { get; init; }
}
