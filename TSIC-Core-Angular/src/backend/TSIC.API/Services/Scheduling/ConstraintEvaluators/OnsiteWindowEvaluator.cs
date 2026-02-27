using TSIC.Contracts.Dtos.Scheduling;

namespace TSIC.API.Services.Scheduling.ConstraintEvaluators;

/// <summary>
/// On-Site Window — match the time spread between a team's first and last game
/// to last year's pattern, whether that was tight or spread out across the day.
/// Uses the source's time range per day (Q2) to preserve the on-site window.
/// </summary>
public sealed class OnsiteWindowEvaluator : IConstraintEvaluator
{
    public string Name => "onsite-window";
    public string SacrificeImpact => "A few games fall outside the source schedule's time window for their division — typically when timeslot availability is tight and the next best slot is just outside the expected range.";

    public bool Evaluate(CandidateSlot slot, GameContext game, DivisionSizeProfile profile,
                         PlacementState state)
    {
        var dayOfWeek = slot.GDate.DayOfWeek;
        var timeOfDay = slot.GDate.TimeOfDay;

        // Prefer offset-based window: translate source pattern into current job's timeslot canvas
        if (profile.StartOffsetFromWindow != null
            && profile.StartOffsetFromWindow.TryGetValue(dayOfWeek, out var offset)
            && state.CurrentWindowStart.TryGetValue(dayOfWeek, out var currentWinStart))
        {
            // Rebuild the expected time range in current-job terms:
            // Start = currentWindowStart + source offset
            // End   = start + source onsite interval (or absolute range span as fallback)
            var expectedStart = currentWinStart + offset;
            var span = profile.OnsiteIntervalPerDay.TryGetValue(dayOfWeek, out var onsiteSpan)
                ? onsiteSpan
                : profile.TimeRangeAbsolute.TryGetValue(dayOfWeek, out var abs)
                    ? abs.End - abs.Start
                    : TimeSpan.FromHours(4); // generous fallback
            var expectedEnd = expectedStart + span;

            return timeOfDay >= expectedStart && timeOfDay <= expectedEnd;
        }

        // Fallback: use absolute time range from source (no offset data available)
        if (!profile.TimeRangeAbsolute.TryGetValue(dayOfWeek, out var timeRange))
            return true; // No profile data for this day — don't penalize

        return timeOfDay >= timeRange.Start && timeOfDay <= timeRange.End;
    }
}
