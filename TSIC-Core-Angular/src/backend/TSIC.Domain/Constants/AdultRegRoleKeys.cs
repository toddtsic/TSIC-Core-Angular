namespace TSIC.Domain.Constants;

/// <summary>
/// Allowed role keys for adult self-registration URLs (<c>?role=...</c>).
/// This allowlist is the FIRST security gate: any request referencing a key
/// not in <see cref="All"/> is rejected outright.
/// <para>
/// The key is intent from the URL — "the user wants to register as a coach/referee/recruiter".
/// The actual <c>RoleId</c> assigned is resolved server-side from (key + job type)
/// per the security model (see <c>IAdultRegistrationService.ResolveAdultRole</c>).
/// </para>
/// <para>
/// To add a new public-facing adult role: add a constant here + update the resolver.
/// </para>
/// </summary>
public static class AdultRegRoleKeys
{
    public const string Coach = "coach";
    public const string Referee = "referee";
    public const string Recruiter = "recruiter";

    /// <summary>
    /// Player-site self-roster — unconditionally creates an UnassignedAdult awaiting
    /// director approval. Decoupled from <see cref="Coach"/> so the URL itself is the
    /// security contract; cannot resolve to Staff regardless of job-type configuration.
    /// Use for new bulletins on Club/League sites. Legacy <see cref="Coach"/> URLs on
    /// Club/League jobs continue to resolve to UnassignedAdult via backend branching.
    /// </summary>
    public const string Unassigned = "unassigned";

    /// <summary>Case-insensitive allowlist of recognized role keys.</summary>
    public static readonly HashSet<string> All = new(StringComparer.OrdinalIgnoreCase)
    {
        Coach,
        Referee,
        Recruiter,
        Unassigned,
    };

    /// <summary>Returns true if the given key is a recognized adult registration role.</summary>
    public static bool IsValid(string? key) =>
        !string.IsNullOrWhiteSpace(key) && All.Contains(key);
}
