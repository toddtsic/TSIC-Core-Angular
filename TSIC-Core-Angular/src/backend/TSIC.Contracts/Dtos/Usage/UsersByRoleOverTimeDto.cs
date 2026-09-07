namespace TSIC.Contracts.Dtos.Usage;

/// <summary>
/// The time unit a bucketed usage report groups by. Each carries its own span (see
/// UsageBuckets in the API): a bucket is the grouping AND the window, because a monthly
/// bucket over a 30-day window is one bar and says nothing.
/// </summary>
public enum UsageBucket
{
    Day,
    Week,
    Month,
}

/// <summary>
/// Usage report 03 -- Users by Role over Time. Report 01's question (people using the
/// scoped live events, counted by REGISTRATION, grouped by role) asked once per bucket
/// instead of once for the window, so the answer is a series rather than a number.
///
/// A registration counts in every bucket it made a request in: active on three days is
/// three daily rows. That is the daily/weekly/monthly-active reading, and it means the
/// buckets do NOT sum to report 01's window total -- 01 is the distinct count for the
/// whole span, this is the distinct count per slice. Neither is a sum of the other.
///
/// Rows carry no event key. The scope (and the event lens) is resolved before the
/// query, so a row is (bucket, role) across whatever set the caller narrowed to.
///
/// Anonymous traffic is absent for the same reason as in 01: a request is not a person.
/// </summary>
public record UsersByRoleOverTimeDto
{
    /// <summary>The wire word: day | week | month.</summary>
    public required string Bucket { get; init; }

    /// <summary>Start of the first bucket, server-local like OccurredAt. Buckets are aligned to their unit (midnight, Monday, the 1st).</summary>
    public required DateTime Since { get; init; }

    /// <summary>Every bucket start in the span, oldest first, including buckets with no rows. The x axis.</summary>
    public required List<DateTime> Buckets { get; init; }

    /// <summary>
    /// When the log begins -- the earliest admitted row on this server, null for an empty
    /// log. A bucket that ENDS before this has no data, which is not zero users: the page
    /// draws it as a gap and says "no data", never 0.
    /// </summary>
    public required DateTime? FirstRecordedAt { get; init; }

    /// <summary>Live events the numbers cover -- the resolved scope (after any event lens), restated for the audit stamp.</summary>
    public required int JobCount { get; init; }

    /// <summary>One row per (bucket, role) that had at least one user. Unordered; the page fills the gaps and sorts.</summary>
    public required List<UsersByRoleBucketRowDto> Rows { get; init; }

    /// <summary>False when TSICLogs is not configured on this server -- a missing source, not zero traffic.</summary>
    public required bool UsageLoggingAvailable { get; init; }
}

public record UsersByRoleBucketRowDto
{
    /// <summary>The bucket's start, one of UsersByRoleOverTimeDto.Buckets.</summary>
    public required DateTime BucketStart { get; init; }

    public required string RoleName { get; init; }

    /// <summary>Distinct registrations under this role with at least one request in this bucket.</summary>
    public required int Users { get; init; }

    /// <summary>True for the admin tier (RoleConstants.AdminRoleIds), as on report 01.</summary>
    public required bool IsAdmin { get; init; }
}

/// <summary>A (bucket, event, registration) triple from TSICLogs: this registration made at least one request about this event in this bucket.</summary>
public record UsageRegistrationByBucketDto
{
    /// <summary>Whole buckets from the aligned <c>since</c>: 0 is the oldest.</summary>
    public required int BucketIndex { get; init; }

    /// <summary>The event the request was about -- so the report can count it only in buckets the event was live in.</summary>
    public required Guid JobId { get; init; }

    public required Guid RegistrationId { get; init; }
}
