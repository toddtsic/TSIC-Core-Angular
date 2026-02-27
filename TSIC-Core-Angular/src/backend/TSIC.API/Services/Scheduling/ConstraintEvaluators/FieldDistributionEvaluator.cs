using TSIC.Contracts.Dtos.Scheduling;

namespace TSIC.API.Services.Scheduling.ConstraintEvaluators;

/// <summary>
/// Field Distribution — maintain per team the balance of different fields used.
/// Prefers placing a game on a field that the team has used less often,
/// spreading field assignments evenly across all available fields.
/// </summary>
public sealed class FieldDistributionEvaluator : IConstraintEvaluator
{
    public string Name => "field-distribution";
    public string SacrificeImpact => "Some teams play on the same field more than once when they could be spread more evenly — happens when balanced field rotation conflicts with higher-priority constraints.";

    public bool Evaluate(CandidateSlot slot, GameContext game, DivisionSizeProfile profile,
                         PlacementState state)
    {
        // For each team, check if placing on this field would create an imbalance.
        // A team should use each field roughly the same number of times.
        // Violation: either team has already played on this field more than
        // the minimum field count (they could be on a less-used field instead).

        var t1FieldCount = GetTeamFieldCount(state, game.DivId, game.T1No, slot.FieldName);
        var t1MinCount = GetTeamMinFieldCount(state, game.DivId, game.T1No);

        var t2FieldCount = GetTeamFieldCount(state, game.DivId, game.T2No, slot.FieldName);
        var t2MinCount = GetTeamMinFieldCount(state, game.DivId, game.T2No);

        // Satisfied if both teams have used this field no more than their least-used field
        return t1FieldCount <= t1MinCount && t2FieldCount <= t2MinCount;
    }

    private static int GetTeamFieldCount(PlacementState state, Guid divId, int teamNo, string fieldName)
    {
        var key = (divId, teamNo, fieldName);
        return state.TeamFieldCounts.GetValueOrDefault(key);
    }

    private static int GetTeamMinFieldCount(PlacementState state, Guid divId, int teamNo)
    {
        var min = int.MaxValue;
        var found = false;
        foreach (var (key, count) in state.TeamFieldCounts)
        {
            if (key.DivId == divId && key.TeamNo == teamNo)
            {
                found = true;
                if (count < min) min = count;
            }
        }
        // If team has no field placements yet, min is 0
        return found ? min : 0;
    }
}
