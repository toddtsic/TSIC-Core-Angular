using TSIC.Contracts.Dtos.Scheduling;

namespace TSIC.API.Services.Scheduling;

/// <summary>
/// V1 helper for finding the next available timeslot by walking
/// dates × fields × game intervals. Used by ScheduleDivisionService.
/// V2 uses PlacementScorer with CandidateSlot generation instead.
/// </summary>
[Obsolete("V1 vertical-fill approach. V2 uses PlacementScorer + CandidateSlot generation.")]
public static class TimeslotSlotFinder
{
    /// <summary>
    /// Find the next available (field, datetime) slot by walking dates, fields,
    /// and game-start intervals, skipping already-occupied slots.
    /// </summary>
    public static (Guid fieldId, DateTime gDate)? FindNextAvailable(
        List<TimeslotDateDto> dates,
        List<TimeslotFieldDto> fields,
        HashSet<(Guid fieldId, DateTime gDate)> occupiedSlots)
    {
        return FindNextAvailable(dates, fields, occupiedSlots, null, default, 0, 0);
    }

    /// <summary>
    /// Find the next available slot that is also BTB-safe for both teams.
    /// A slot is BTB-unsafe if either team already has a game within
    /// <paramref name="btbThresholdMinutes"/> minutes of the proposed time.
    /// </summary>
    public static (Guid fieldId, DateTime gDate)? FindNextAvailable(
        List<TimeslotDateDto> dates,
        List<TimeslotFieldDto> fields,
        HashSet<(Guid fieldId, DateTime gDate)> occupiedSlots,
        BtbTracker? btbTracker,
        Guid divId, int t1No, int t2No)
    {
        // Derive BTB threshold from field config (max gamestartInterval)
        var btbThresholdMinutes = btbTracker != null
            ? fields.Count > 0 ? fields.Max(f => f.GamestartInterval) : 0
            : 0;

        foreach (var date in dates.OrderBy(d => d.GDate))
        {
            var dow = date.GDate.DayOfWeek.ToString();
            var dowFields = fields
                .Where(f => f.Dow.Equals(dow, StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f.FieldId)
                .ToList();

            foreach (var ft in dowFields)
            {
                if (!TimeSpan.TryParse(ft.StartTime, out var startTime))
                    continue;

                var baseDate = date.GDate.Date;

                for (var g = 0; g < ft.MaxGamesPerField; g++)
                {
                    var gameTime = baseDate + startTime + TimeSpan.FromMinutes(g * ft.GamestartInterval);
                    if (occupiedSlots.Contains((ft.FieldId, gameTime)))
                        continue;

                    // BTB check: ensure neither team has a game within threshold
                    if (btbTracker != null && btbThresholdMinutes > 0
                        && btbTracker.HasConflict(divId, t1No, t2No, gameTime, btbThresholdMinutes))
                        continue;

                    return (ft.FieldId, gameTime);
                }
            }
        }

        return null;
    }
}

/// <summary>
/// Tracks team game times to detect back-to-back conflicts.
/// Keyed by (divId, teamNo) because position numbers (1, 2, 3...)
/// repeat across divisions — divId disambiguates.
/// </summary>
public sealed class BtbTracker
{
    private readonly Dictionary<(Guid divId, int teamNo), List<DateTime>> _teamTimes = new();

    /// <summary>Record that a team is playing at the given time.</summary>
    public void Record(Guid divId, int teamNo, DateTime gameTime)
    {
        var key = (divId, teamNo);
        if (!_teamTimes.TryGetValue(key, out var times))
        {
            times = [];
            _teamTimes[key] = times;
        }
        times.Add(gameTime);
    }

    /// <summary>
    /// Returns true if either team already has a game within thresholdMinutes
    /// of the proposed gameTime.
    /// </summary>
    public bool HasConflict(Guid divId, int t1No, int t2No, DateTime gameTime, int thresholdMinutes)
    {
        return HasTeamConflict(divId, t1No, gameTime, thresholdMinutes)
            || HasTeamConflict(divId, t2No, gameTime, thresholdMinutes);
    }

    private bool HasTeamConflict(Guid divId, int teamNo, DateTime gameTime, int thresholdMinutes)
    {
        if (teamNo <= 0) return false;
        if (!_teamTimes.TryGetValue((divId, teamNo), out var times)) return false;

        foreach (var t in times)
        {
            if (Math.Abs((gameTime - t).TotalMinutes) <= thresholdMinutes)
                return true;
        }
        return false;
    }
}
