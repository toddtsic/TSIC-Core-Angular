using TSIC.Contracts.Dtos.Scheduling;

namespace TSIC.API.Services.Scheduling;

/// <summary>
/// Mutable state container passed through the build loop during V2 placement.
/// Tracks occupied slots, round timing, field usage, and team game times.
/// Used by PlacementScorer for distance-from-source scoring.
/// </summary>
public sealed class PlacementState
{
    /// <summary>All occupied (FieldId, DateTime) slots across the entire job.</summary>
    public HashSet<(Guid FieldId, DateTime GDate)> OccupiedSlots { get; }

    /// <summary>
    /// The time of the first game placed in each round, per division.
    /// Used by the scorer for round layout enforcement (horizontal rounds must align).
    /// </summary>
    public Dictionary<(Guid DivId, int Round), TimeSpan> RoundTargetTimes { get; } = new();

    /// <summary>
    /// Per-team per-field game count — used by the scorer for field distribution fairness.
    /// Key: (DivId, TeamNo, FieldName) → game count on that field.
    /// </summary>
    public Dictionary<(Guid DivId, int TeamNo, string FieldName), int> TeamFieldCounts { get; } = new();

    /// <summary>
    /// Per-team per-day game times — tracks when each team plays on a given day.
    /// Used by the scorer for min-team-gap scoring (replaces BTB hard filter).
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
    /// Used by PlacementScorer to penalize "Avoid" fields.
    /// </summary>
    public Dictionary<Guid, int> FieldPreferences { get; }

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
    /// Record a game placement — updates all tracking state.
    /// </summary>
    public void RecordPlacement(CandidateSlot slot, GameContext game)
    {
        OccupiedSlots.Add((slot.FieldId, slot.GDate));

        // Track round target time (first game in round sets the target)
        var roundKey = (game.DivId, game.Round);
        RoundTargetTimes.TryAdd(roundKey, slot.GDate.TimeOfDay);

        // Track team-field counts for field distribution balance
        var tfKey1 = (game.DivId, game.T1No, slot.FieldName);
        TeamFieldCounts[tfKey1] = TeamFieldCounts.GetValueOrDefault(tfKey1) + 1;

        var tfKey2 = (game.DivId, game.T2No, slot.FieldName);
        TeamFieldCounts[tfKey2] = TeamFieldCounts.GetValueOrDefault(tfKey2) + 1;

        // Track per-team per-day game times for min-team-gap scoring
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
    }
}
