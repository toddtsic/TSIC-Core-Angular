using TSIC.Contracts.Dtos.Scheduling;

namespace TSIC.API.Services.Scheduling.ConstraintEvaluators;

/// <summary>
/// Field Assignment — keep teams playing on the same set of fields they used last year.
/// If U10 played on Fields 1–4 in the source, they stay on Fields 1–4.
/// Uses the field band extracted from the source schedule (Q3).
/// </summary>
public sealed class FieldAssignmentEvaluator : IConstraintEvaluator
{
    public string Name => "field-assignment";
    public string SacrificeImpact => "Games placed on different fields than the source used for their division — happens when source field assignments can't be matched to current fields.";

    public bool Evaluate(CandidateSlot slot, GameContext game, DivisionSizeProfile profile,
                         PlacementState state)
    {
        if (profile.FieldBand.Count == 0)
            return true; // No field band data from source — don't penalize

        return profile.FieldBand.Contains(slot.FieldName);
    }
}
