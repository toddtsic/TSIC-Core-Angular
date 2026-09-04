namespace TSIC.Contracts.Dtos.Widgets;

/// <summary>
/// One job's bar on the UsageStatsPerJob chart.
/// </summary>
public record UsageStatsPerJobRowDto
{
    public required Guid JobId { get; init; }
    public required string JobName { get; init; }

    public required int TotalRequests { get; init; }

    /// <summary>Requests carrying a signed-in user.</summary>
    public required int SignedInRequests { get; init; }

    /// <summary>Total minus signed-in. Derived, never counted separately, so they always sum.</summary>
    public required int AnonymousRequests { get; init; }

    /// <summary>
    /// Distinct signed-in users. There is NO equivalent for anonymous traffic -- the fact
    /// table holds no session or device key -- so this must never be presented as
    /// "visitors".
    /// </summary>
    public required int DistinctUsers { get; init; }

    /// <summary>Server-local (Arizona), like every other timestamp in this system.</summary>
    public required DateTime LastActivity { get; init; }
}

/// <summary>
/// Usage per job over a window: the top N jobs as chart rows, plus a rollup that accounts
/// for everything the chart does not show.
///
/// The chart is deliberately truncated -- sixty bars is a wall, not a visualization -- so
/// the rollup carries the remainder explicitly. A truncated chart with no statement of
/// what was truncated is how a reader concludes the total is smaller than it is.
/// </summary>
public record UsageStatsPerJobDto
{
    public required List<UsageStatsPerJobRowDto> Rows { get; init; }

    /// <summary>Days of history the window covers.</summary>
    public required int WindowDays { get; init; }

    /// <summary>Whether bot traffic was excluded from these numbers.</summary>
    public required bool BotsExcluded { get; init; }

    /// <summary>Requests across ALL jobs in scope, including those not shown as rows.</summary>
    public required int TotalRequests { get; init; }

    /// <summary>Jobs with any traffic in the window, including those not shown as rows.</summary>
    public required int TotalJobs { get; init; }

    /// <summary>Jobs beyond the top N. Zero when the chart shows everything.</summary>
    public required int OtherJobCount { get; init; }

    /// <summary>Requests belonging to those other jobs.</summary>
    public required int OtherRequests { get; init; }

    /// <summary>
    /// False when TSICLogs is not configured on this server. The UI must say so rather
    /// than render an empty chart, which would read as "no usage" instead of "no data
    /// source".
    /// </summary>
    public required bool UsageLoggingAvailable { get; init; }
}
