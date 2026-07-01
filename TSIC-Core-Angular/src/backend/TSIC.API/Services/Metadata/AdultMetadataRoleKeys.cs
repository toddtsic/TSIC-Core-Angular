using System.Collections.Frozen;

namespace TSIC.API.Services.Metadata;

/// <summary>
/// The exact, case-sensitive keys used inside Jobs.AdultProfileMetadataJson to partition adult form
/// fields by role. These MUST match <c>AdultRegistrationService.GetRoleKey</c> (Coach and Staff both
/// map to <see cref="UnassignedAdult"/>) and are looked up case-sensitively by
/// <c>ProfileMetadataService.ParseForRole</c>.
///
/// NOT the same as <c>AdultRegRoleKeys</c> (lowercase URL keys used for routing) — do not interchange.
/// </summary>
public static class AdultMetadataRoleKeys
{
    public const string UnassignedAdult = "UnassignedAdult";
    public const string Referee = "Referee";
    public const string Recruiter = "Recruiter";

    public static readonly FrozenSet<string> All =
        new[] { UnassignedAdult, Referee, Recruiter }.ToFrozenSet(StringComparer.Ordinal);

    public static bool IsValid(string? roleKey) =>
        !string.IsNullOrWhiteSpace(roleKey) && All.Contains(roleKey);
}
