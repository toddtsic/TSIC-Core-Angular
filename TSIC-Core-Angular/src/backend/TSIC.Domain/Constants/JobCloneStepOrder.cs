namespace TSIC.Domain.Constants;

/// <summary>
/// The single ordered manifest of job-clone steps. Drives:
///   1. Insert order in JobCloneService (forward),
///   2. Dev-undo cascade delete in JobCloneRepository (reversed),
///   3. The plan/summary step lists returned to the frontend,
///   4. The manifest-parity drift test (executor keys ∪ delete keys must equal Steps).
/// Adding a cloned entity means adding a key here — the parity test then fails until
/// both the executor and the dev-undo delete map handle it.
/// </summary>
public static class JobCloneStepOrder
{
    public const string Job = "Job";
    public const string DisplayOptions = "DisplayOptions";
    public const string OwlImages = "OwlImages";
    public const string Bulletins = "Bulletins";
    public const string AgeRanges = "AgeRanges";
    public const string Menus = "Menus";
    public const string JobReports = "JobReports";
    public const string Nav = "Nav";
    public const string AdminRegistrations = "AdminRegistrations";
    public const string Leagues = "Leagues";
    public const string JobLeagues = "JobLeagues";
    public const string Agegroups = "Agegroups";
    public const string Divisions = "Divisions";
    public const string Teams = "Teams";
    public const string JobFees = "JobFees";
    public const string FeeModifiers = "FeeModifiers";

    /// <summary>Insert order. Dev-undo deletes in exact reverse.</summary>
    public static readonly IReadOnlyList<string> Steps =
    [
        Job,
        DisplayOptions,
        OwlImages,
        Bulletins,
        AgeRanges,
        Menus,
        JobReports,
        Nav,
        AdminRegistrations,
        Leagues,
        JobLeagues,
        Agegroups,
        Divisions,
        Teams,
        JobFees,
        FeeModifiers,
    ];
}
