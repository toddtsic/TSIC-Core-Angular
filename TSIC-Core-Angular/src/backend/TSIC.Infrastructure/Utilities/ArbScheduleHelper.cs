namespace TSIC.Infrastructure.Utilities;

/// <summary>
/// Shared helper for ARB subscription schedule queries used by search projections.
/// Returns whether the next charge is still in the future (=> "Scheduled" badge on
/// the displayed OwedTotal so admins don't read pending-autopay balances as
/// delinquency) and the date of that charge for tooltip display.
/// </summary>
public static class ArbScheduleHelper
{
    /// <summary>
    /// Day-based schedule (team ARB-Trial: deposit at startDate, balance at startDate + intervalDays).
    /// </summary>
    public static (bool PaymentScheduled, DateTime? NextChargeDate) ComputeDayBasedSchedule(
        string? subId,
        string? subStatus,
        DateTime? startDate,
        int? intervalDays,
        int? totalOccurrences,
        DateTime today)
        => Compute(subId, subStatus, startDate, intervalDays, totalOccurrences, intervalIsDays: true, today);

    /// <summary>
    /// Month-based schedule (player ARB: occurrences at startDate + intervalMonths * N).
    /// </summary>
    public static (bool PaymentScheduled, DateTime? NextChargeDate) ComputeMonthBasedSchedule(
        string? subId,
        string? subStatus,
        DateTime? startDate,
        int? intervalMonths,
        int? totalOccurrences,
        DateTime today)
        => Compute(subId, subStatus, startDate, intervalMonths, totalOccurrences, intervalIsDays: false, today);

    private static (bool PaymentScheduled, DateTime? NextChargeDate) Compute(
        string? subId,
        string? subStatus,
        DateTime? startDate,
        int? interval,
        int? totalOccurrences,
        bool intervalIsDays,
        DateTime today)
    {
        if (string.IsNullOrEmpty(subId)) return (false, null);

        // ADN sub statuses we treat as "still on autopay": null (not yet swept) and "active".
        // Anything else (suspended, expired, terminated, canceled) un-suppresses so the
        // displayed OwedTotal reads as a real balance to act on.
        if (!string.IsNullOrEmpty(subStatus)
            && !string.Equals(subStatus, "active", StringComparison.OrdinalIgnoreCase))
        {
            return (false, null);
        }

        if (!startDate.HasValue || !interval.HasValue || !totalOccurrences.HasValue)
            return (false, null);

        for (var i = 0; i < totalOccurrences.Value; i++)
        {
            var d = intervalIsDays
                ? startDate.Value.AddDays((long)i * interval.Value)
                : startDate.Value.AddMonths(i * interval.Value);
            if (d.Date > today.Date) return (true, d);
        }
        return (false, null);
    }
}
