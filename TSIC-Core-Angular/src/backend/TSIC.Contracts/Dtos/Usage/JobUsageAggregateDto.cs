namespace TSIC.Contracts.Dtos.Usage;

/// <summary>
/// One job's usage totals over a window, as TSICLogs alone can report them.
///
/// Deliberately carries NO job name: names live in TSICV5 and this comes from
/// TSICLogs. The two databases are separate on purpose -- telemetry must not be able
/// to reach the application's data -- so nothing joins them in SQL. The service pairs
/// this with a name lookup and merges in memory, the same way the usage writer
/// enriches captures, just in the other direction.
/// </summary>
public record JobUsageAggregateDto
{
    public required Guid JobId { get; init; }

    /// <summary>Every logged request for this job in the window, bots included or not per the query.</summary>
    public required int TotalRequests { get; init; }

    /// <summary>
    /// Requests that carried a signed-in user. The remainder is anonymous -- derived by
    /// subtraction rather than counted separately, so the two can never fail to sum to
    /// the total.
    /// </summary>
    public required int SignedInRequests { get; init; }

    /// <summary>
    /// Distinct signed-in users. Anonymous traffic has no identity to count: there is no
    /// session or device key in the fact table, so "distinct anonymous visitors" is not a
    /// question this data can answer and must not be implied in the UI.
    /// </summary>
    public required int DistinctUsers { get; init; }

    /// <summary>Most recent request in the window. Server-local (Arizona), like OccurredAt.</summary>
    public required DateTime LastActivity { get; init; }
}
