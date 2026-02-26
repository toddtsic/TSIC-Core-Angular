using TSIC.Contracts.Dtos.Scheduling;

namespace TSIC.API.Services.Scheduling;

/// <summary>
/// Shared helper for finding the next available timeslot by walking
/// dates × fields × game intervals. Used by both AutoBuildScheduleService
/// and ScheduleDivisionService.
/// </summary>
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
                    if (!occupiedSlots.Contains((ft.FieldId, gameTime)))
                        return (ft.FieldId, gameTime);
                }
            }
        }

        return null;
    }
}
