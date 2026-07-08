using TSIC.Domain.Constants;

namespace TSIC.Domain.Adults;

/// <summary>
/// Single source of truth for the role partition inside <c>Jobs.AdultProfileMetadataJson</c>.
///
/// That column is a role-keyed object whose keys are exactly <see cref="UnassignedAdult"/>,
/// <see cref="Referee"/>, and <see cref="Recruiter"/> (case-sensitive). A registration's stored
/// <c>RoleId</c> maps to one of these keys via <see cref="KeyForRoleId"/> — Staff shares the
/// UnassignedAdult block (same coach/volunteer persona), and any non-adult role (Player, Club Rep,
/// …) maps to <c>null</c> (no adult template).
///
/// Lives in Domain so both the API layer (<c>AdultMetadataRoleKeys</c>,
/// <c>AdultRegistrationService.GetRoleKey</c>) and the Infrastructure repositories can resolve the
/// slice without duplicating the mapping.
/// </summary>
public static class AdultMetadataRoleResolver
{
    public const string UnassignedAdult = "UnassignedAdult";
    public const string Referee = "Referee";
    public const string Recruiter = "Recruiter";

    /// <summary>
    /// Metadata key for a registration's <c>RoleId</c>, or <c>null</c> when the role has no adult
    /// template (player, club rep, director, etc.). Staff folds into <see cref="UnassignedAdult"/>.
    /// </summary>
    public static string? KeyForRoleId(string? roleId) => roleId switch
    {
        RoleConstants.UnassignedAdult => UnassignedAdult,
        RoleConstants.Staff => UnassignedAdult,
        RoleConstants.Referee => Referee,
        RoleConstants.Recruiter => Recruiter,
        _ => null,
    };
}
