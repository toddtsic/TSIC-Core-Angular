using TSIC.Contracts.Dtos.Scheduling;

namespace TSIC.API.Services.Scheduling;

/// <summary>
/// Mutable state container passed through the build loop during V2 placement.
/// Tracks occupied slots, round timing, and team game times.
/// Used by PlacementScorer for hard-filter checks (time overlap detection).
/// </summary>
public sealed class PlacementState
{
    /// <summary>All occupied (FieldId, DateTime) slots across the entire job.</summary>
    public HashSet<(Guid FieldId, DateTime GDate)> OccupiedSlots { get; }

    /// <summary>
    /// The time of the first game placed in each round, per division.
    /// Used by the placement loop for round start time computation.
    /// </summary>
    public Dictionary<(Guid DivId, int Round), TimeSpan> RoundTargetTimes { get; } = new();

    /// <summary>
    /// Per-team per-day game times — tracks when each team plays on a given day.
    /// Used by PlacementScorer for time overlap hard filter (prevents double-booking a team).
    /// Key: (DivId, TeamNo, GameDay) → list of TimeOfDay values.
    /// </summary>
    public Dictionary<(Guid DivId, int TeamNo, DateTime GameDay), List<TimeSpan>> TeamDayGameTimes { get; } = new();

    /// <summary>
    /// Current job's timeslot window start per DOW (earliest configured field start time).
    /// Used by the placement loop for offset-based target time calculation.
    /// </summary>
    public Dictionary<DayOfWeek, TimeSpan> CurrentWindowStart { get; }

    /// <summary>
    /// Per-field quality flags: FieldId → FieldPreference (0=Normal, 1=Preferred, 2=Avoid).
    /// Used by PlacementScorer to skip Avoid fields in early cascade passes.
    /// </summary>
    public Dictionary<Guid, int> FieldPreferences { get; }

    /// <summary>
    /// Tracks which time-of-day slots have been used per (DivId, Round, GameDay).
    /// Used by PlacementScorer for Sequential (vertical) placement: avoids placing
    /// multiple games from the same round at the same time slot, stacking them
    /// in time instead so spectators/recruiters can watch every team.
    /// </summary>
    public Dictionary<(Guid DivId, int Round, DateTime GameDay), HashSet<TimeSpan>> RoundTimeSlots { get; } = new();

    public PlacementState(
        HashSet<(Guid FieldId, DateTime GDate)> occupiedSlots,
        Dictionary<DayOfWeek, TimeSpan>? currentWindowStart = null,
        Dictionary<Guid, int>? fieldPreferences = null)
    {
        OccupiedSlots = occupiedSlots;
        CurrentWindowStart = currentWindowStart ?? new Dictionary<DayOfWeek, TimeSpan>();
        FieldPreferences = fieldPreferences ?? new Dictionary<Guid, int>();
    }

    /// <summary>
    /// Record a game placement — updates occupied slots, round timing, and team game times.
    /// </summary>
    public void RecordPlacement(CandidateSlot slot, GameContext game)
    {
        OccupiedSlots.Add((slot.FieldId, slot.GDate));

        // Track round target time (first game in round sets the target)
        var roundKey = (game.DivId, game.Round);
        RoundTargetTimes.TryAdd(roundKey, slot.GDate.TimeOfDay);

        // Track per-team per-day game times for time overlap detection
        var gameDay = slot.GDate.Date;
        var gameTime = slot.GDate.TimeOfDay;

        var tdKey1 = (game.DivId, game.T1No, gameDay);
        if (!TeamDayGameTimes.TryGetValue(tdKey1, out var times1))
            TeamDayGameTimes[tdKey1] = times1 = [];
        times1.Add(gameTime);

        var tdKey2 = (game.DivId, game.T2No, gameDay);
        if (!TeamDayGameTimes.TryGetValue(tdKey2, out var times2))
            TeamDayGameTimes[tdKey2] = times2 = [];
        times2.Add(gameTime);

        // Track round time slots for sequential (vertical) placement
        var roundDayKey = (game.DivId, game.Round, gameDay);
        if (!RoundTimeSlots.TryGetValue(roundDayKey, out var roundTimes))
            RoundTimeSlots[roundDayKey] = roundTimes = [];
        roundTimes.Add(gameTime);
    }
}
