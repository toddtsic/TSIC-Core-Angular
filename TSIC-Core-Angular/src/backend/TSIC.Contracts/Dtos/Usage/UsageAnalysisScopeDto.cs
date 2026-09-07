namespace TSIC.Contracts.Dtos.Usage;

/// <summary>
/// The scope names every usage-analysis endpoint accepts. Strings, not an enum, so the
/// wire value is the same word the UI segment shows and the URL carries.
/// </summary>
public static class UsageAnalysisScopes
{
    /// <summary>The job the caller is standing in.</summary>
    public const string Job = "job";

    /// <summary>Every live job of the customer owning the caller's job.</summary>
    public const string Customer = "customer";

    /// <summary>Every live job on the platform.</summary>
    public const string Tsic = "tsic";
}

/// <summary>
/// What the usage-analysis page needs before any tab runs: the scope the server actually
/// resolved, the widest scope this caller may ask for, and the live jobs the scope covers.
///
/// Every scope is constrained to LIVE jobs (ExpiryUsers > now) -- the page never reports on
/// a concluded event. The job list is returned so the page can name what a number covers;
/// it is never sent back. Endpoints take a scope word and derive the job set from the
/// token themselves.
/// </summary>
public record UsageAnalysisScopeDto
{
    /// <summary>One of <see cref="UsageAnalysisScopes"/> -- the scope these jobs were resolved for.</summary>
    public required string Scope { get; init; }

    /// <summary>
    /// The widest scope the caller's role may request. Director = job, SuperDirector =
    /// customer, Superuser = tsic. The UI hides segments above it; the server refuses them.
    /// </summary>
    public required string MaxScope { get; init; }

    /// <summary>
    /// False when TSICLogs is not configured on this server. Distinct from "no traffic":
    /// the page says the data source is missing rather than showing an empty chart.
    /// </summary>
    public required bool UsageLoggingAvailable { get; init; }

    /// <summary>
    /// Whether the job the caller is standing in is itself live. False means the job
    /// scope is empty by rule -- the event has concluded -- and the page should say so.
    /// </summary>
    public required bool CurrentJobIsLive { get; init; }

    /// <summary>Live jobs covered by <see cref="Scope"/>, ordered by name.</summary>
    public required List<UsageAnalysisJobDto> Jobs { get; init; }
}

/// <summary>A live job inside a usage-analysis scope. Name only -- enough to label a row.</summary>
public record UsageAnalysisJobDto
{
    public required Guid JobId { get; init; }

    public required string JobName { get; init; }

    /// <summary>
    /// When the event stops being live. A bucketed report uses it to count an event only
    /// in the buckets it was live in, so a span reaching back a year does not attribute
    /// last spring's usage to "events live today".
    /// </summary>
    public required DateTime ExpiryUsers { get; init; }
}
