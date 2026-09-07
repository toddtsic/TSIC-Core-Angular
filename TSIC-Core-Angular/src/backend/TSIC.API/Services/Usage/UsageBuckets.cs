using TSIC.Contracts.Dtos.Usage;

namespace TSIC.API.Services.Usage;

/// <summary>
/// The span and alignment of every bucketed usage report. A bucket is the grouping and
/// the window in one (Todd, 2026-09-07): Daily is the last 30 days, Weekly the last 12
/// weeks, Monthly the last 12 months -- each ending in the CURRENT, partial bucket, so
/// the newest bar is always today's, this week's, this month's so far.
///
/// Times are server-local, like OccurredAt and like Since() in the analysis service.
/// Weeks start on Monday.
/// </summary>
public static class UsageBuckets
{
    public static int CountOf(UsageBucket bucket) => bucket switch
    {
        UsageBucket.Day => 30,
        UsageBucket.Week => 12,
        UsageBucket.Month => 12,
        _ => throw new ArgumentOutOfRangeException(nameof(bucket)),
    };

    /// <summary>The wire word: day | week | month. Null for anything else -- the caller decides the default.</summary>
    public static UsageBucket? Parse(string? word) => word?.Trim().ToLowerInvariant() switch
    {
        "day" => UsageBucket.Day,
        "week" => UsageBucket.Week,
        "month" => UsageBucket.Month,
        _ => null,
    };

    public static string ToWord(UsageBucket bucket) => bucket switch
    {
        UsageBucket.Day => "day",
        UsageBucket.Week => "week",
        UsageBucket.Month => "month",
        _ => throw new ArgumentOutOfRangeException(nameof(bucket)),
    };

    /// <summary>Start of the bucket containing <paramref name="at"/>.</summary>
    public static DateTime StartOf(UsageBucket bucket, DateTime at) => bucket switch
    {
        UsageBucket.Day => at.Date,
        // DayOfWeek: Sunday = 0. Roll back to the Monday on or before `at`.
        UsageBucket.Week => at.Date.AddDays(-(((int)at.DayOfWeek + 6) % 7)),
        UsageBucket.Month => new DateTime(at.Year, at.Month, 1),
        _ => throw new ArgumentOutOfRangeException(nameof(bucket)),
    };

    /// <summary>Start of the oldest bucket in the span: CountOf buckets back from the current one, inclusive.</summary>
    public static DateTime SinceFor(UsageBucket bucket, DateTime now) =>
        Add(bucket, StartOf(bucket, now), -(CountOf(bucket) - 1));

    /// <summary><paramref name="start"/> moved by <paramref name="buckets"/> whole buckets.</summary>
    public static DateTime Add(UsageBucket bucket, DateTime start, int buckets) => bucket switch
    {
        UsageBucket.Day => start.AddDays(buckets),
        UsageBucket.Week => start.AddDays(7 * buckets),
        UsageBucket.Month => start.AddMonths(buckets),
        _ => throw new ArgumentOutOfRangeException(nameof(bucket)),
    };

    /// <summary>Every bucket start in the span, oldest first.</summary>
    public static List<DateTime> Starts(UsageBucket bucket, DateTime since) =>
        Enumerable.Range(0, CountOf(bucket)).Select(i => Add(bucket, since, i)).ToList();
}
