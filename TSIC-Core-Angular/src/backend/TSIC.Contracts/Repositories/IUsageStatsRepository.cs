using TSIC.Contracts.Dtos.Usage;

namespace TSIC.Contracts.Repositories;

/// <summary>
/// Read side of TSICLogs. The ONLY thing in the codebase that queries logs.AppUsage --
/// the write path is the usage writer, which bypasses EF entirely and uses SqlBulkCopy.
///
/// Separate from IWidgetRepository because it speaks to a different DATABASE, not just a
/// different table. Keeping that boundary visible in the type system is what stops
/// someone eventually writing a three-part-name join between TSICV5 and TSICLogs and
/// quietly welding the two together.
/// </summary>
public interface IUsageStatsRepository
{
    /// <summary>
    /// True when TSICLogs is actually configured on this box. False means LogsConnection
    /// was absent at startup, so there is no context to query -- callers should report
    /// "not configured" rather than an empty dataset, which would read as "no traffic".
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Per-job usage totals for requests since <paramref name="since"/>.
    ///
    /// Window-bounded rather than scope-bounded on purpose: the window is what makes this
    /// cheap. It rides IX_AppUsage_OccurredAt and returns one row per job that actually
    /// saw traffic -- a handful -- where filtering by a customer's job list would ship
    /// hundreds of ids in to discover most of them were idle. Caller applies its own
    /// scope when it names the jobs.
    ///
    /// Jobs with no job context (JobId = Guid.Empty) are excluded: they are unattributable
    /// traffic, not a job, and would otherwise appear as a nameless row.
    /// </summary>
    Task<IReadOnlyList<JobUsageAggregateDto>> GetUsageByJobAsync(
        DateTime since,
        bool excludeBots,
        CancellationToken cancellationToken = default);
}
