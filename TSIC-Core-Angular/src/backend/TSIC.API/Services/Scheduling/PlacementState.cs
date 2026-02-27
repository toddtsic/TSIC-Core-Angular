using TSIC.Contracts.Dtos.Scheduling;

namespace TSIC.API.Services.Scheduling;

/// <summary>
/// Mutable state container passed through the build loop during V2 horizontal placement.
/// Tracks occupied slots, BTB conflicts, round timing, and field usage per team.
/// </summary>
public sealed class PlacementState
{
    /// <summary>All occupied (FieldId, DateTime) slots across the entire job.</summary>
    public HashSet<(Guid FieldId, DateTime GDate)> OccupiedSlots { get; }

    /// <summary>Back-to-back conflict tracker (keyed by divId + teamNo).</summary>
    public BtbTracker BtbTracker { get; }

    /// <summary>
    /// The time of the first game placed in each round, per division.
    /// Used by PlacementShapeEvaluator to preserve horizontal/vertical layout from source.
    /// </summary>
    public Dictionary<(Guid DivId, int Round), TimeSpan> RoundTargetTimes { get; } = new();

    /// <summary>
    /// Per-team per-field game count. For FieldDistributionEvaluator —
    /// maintains per team the balance of different fields used.
    /// Key: (DivId, TeamNo, FieldName) → game count on that field.
    /// </summary>
    public Dictionary<(Guid DivId, int TeamNo, string FieldName), int> TeamFieldCounts { get; } = new();

    /// <summary>BTB threshold in minutes (derived from field config).</summary>
    public int BtbThresholdMinutes { get; }

    /// <summary>
    /// Current job's timeslot window start per DOW (earliest configured field start time).
    /// Used by OnsiteWindowEvaluator to compute offset-based time windows.
    /// </summary>
    public Dictionary<DayOfWeek, TimeSpan> CurrentWindowStart { get; }

    public PlacementState(
        HashSet<(Guid FieldId, DateTime GDate)> occupiedSlots,
        BtbTracker btbTracker,
        int btbThresholdMinutes,
        Dictionary<DayOfWeek, TimeSpan>? currentWindowStart = null)
    {
        OccupiedSlots = occupiedSlots;
        BtbTracker = btbTracker;
        BtbThresholdMinutes = btbThresholdMinutes;
        CurrentWindowStart = currentWindowStart ?? new Dictionary<DayOfWeek, TimeSpan>();
    }

    /// <summary>
    /// Record a game placement — updates all tracking state.
    /// </summary>
    public void RecordPlacement(CandidateSlot slot, GameContext game)
    {
        OccupiedSlots.Add((slot.FieldId, slot.GDate));
        BtbTracker.Record(game.DivId, game.T1No, slot.GDate);
        BtbTracker.Record(game.DivId, game.T2No, slot.GDate);

        // Track round target time (first game in round sets the target)
        var roundKey = (game.DivId, game.Round);
        RoundTargetTimes.TryAdd(roundKey, slot.GDate.TimeOfDay);

        // Track team-field counts for field distribution balance
        var tfKey1 = (game.DivId, game.T1No, slot.FieldName);
        TeamFieldCounts[tfKey1] = TeamFieldCounts.GetValueOrDefault(tfKey1) + 1;

        var tfKey2 = (game.DivId, game.T2No, slot.FieldName);
        TeamFieldCounts[tfKey2] = TeamFieldCounts.GetValueOrDefault(tfKey2) + 1;
    }
}
