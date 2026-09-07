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
    // ADMISSION RULE (Todd, 2026-09-07). This table records how OUR CLIENTS use the
    // system. Every query here counts only rows with a recognised client tag and a
    // User-Agent that did not declare itself a machine -- the same test the writer now
    // applies before a row exists. Rows from before the rule stay as evidence and are
    // excluded by the same test. There is no switch: bots and untagged traffic are Seq's.

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
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Distinct (event, registration) pairs: each registration that made at least one request
    /// about one of <paramref name="jobIds"/> since <paramref name="since"/>, keyed by that event.
    /// Anonymous rows (no RegId) contribute nothing. Scope-bounded: rides
    /// IX_AppUsage_JobId_OccurredAt with the resolved live-job list.
    /// </summary>
    Task<IReadOnlyList<UsageRegistrationByJobDto>> GetDistinctRegistrationsByJobAsync(
        IReadOnlyList<Guid> jobIds,
        DateTime since,
        int? appClientId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Which app clients have at least one row about any of <paramref name="jobIds"/> since
    /// <paramref name="since"/>, with a row count each. Feeds the page's client lens, which
    /// offers a client only if it is present here.
    /// </summary>
    Task<IReadOnlyList<UsageClientFacetDto>> GetClientsPresentAsync(
        IReadOnlyList<Guid> jobIds,
        DateTime since,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Report 02: anonymous requests (RegId null) per (job, controller, action) in the window,
    /// split into succeeded (status below 400) and failed. Requests are counted, never people.
    /// </summary>
    Task<IReadOnlyList<UsageRouteCountDto>> GetAnonymousRequestsByRouteAsync(
        IReadOnlyList<Guid> jobIds,
        DateTime since,
        int? appClientId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Report 03: distinct (bucket, registration) pairs for signed-in requests about the
    /// given jobs since <paramref name="since"/>, which MUST be aligned to the bucket
    /// (midnight, a Monday, the 1st) -- the index is whole buckets from it. A registration
    /// active in three buckets is three pairs; that is the point.
    /// </summary>
    Task<IReadOnlyList<UsageRegistrationByBucketDto>> GetDistinctRegistrationsByBucketAsync(
        IReadOnlyList<Guid> jobIds,
        DateTime since,
        UsageBucket bucket,
        int? appClientId,
        CancellationToken cancellationToken = default);
}
