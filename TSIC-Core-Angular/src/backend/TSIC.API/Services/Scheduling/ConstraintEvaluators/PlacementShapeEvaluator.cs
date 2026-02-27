using TSIC.Contracts.Dtos.Scheduling;

namespace TSIC.API.Services.Scheduling.ConstraintEvaluators;

/// <summary>
/// Placement Shape — preserve whether rounds were spread across fields (horizontal)
/// or stacked on fewer fields (vertical) like last year.
/// Uses the per-round placement shape extracted from the source schedule (Q5).
/// </summary>
public sealed class PlacementShapeEvaluator : IConstraintEvaluator
{
    public string Name => "placement-shape";
    public string SacrificeImpact => "Some rounds couldn't fit all games at the same time slot — games spill to adjacent times when there aren't enough fields for a fully horizontal round.";

    public bool Evaluate(CandidateSlot slot, GameContext game, DivisionSizeProfile profile,
                         PlacementState state)
    {
        var roundKey = (game.DivId, game.Round);

        // If a target time was already set for this round (first game placed), match it
        // This preserves the shape: horizontal = all same time, vertical = stacked times
        if (state.RoundTargetTimes.TryGetValue(roundKey, out var targetTime))
        {
            // Check source shape: was this round horizontal or vertical?
            if (profile.PlacementShapePerRound.TryGetValue(game.Round, out var shape))
            {
                // Horizontal = 1 distinct time slot (all games at same time)
                if (shape.DistinctTimeSlots <= 1)
                {
                    // Horizontal: all games in round must match the target time
                    return slot.GDate.TimeOfDay == targetTime;
                }
                else
                {
                    // Vertical: games stacked at different times on same/fewer fields.
                    // Don't enforce same time — vertical shape is preserved by NOT
                    // requiring time alignment. Any slot on the correct day is fine.
                    return true;
                }
            }

            // No shape data — default to horizontal (all same time)
            return slot.GDate.TimeOfDay == targetTime;
        }

        // First game in this round — if the game has an explicit target time, prefer it
        if (game.TargetTime.HasValue)
            return slot.GDate.TimeOfDay == game.TargetTime.Value;

        // No target set yet and no explicit target — any time is fine for first game
        return true;
    }
}
