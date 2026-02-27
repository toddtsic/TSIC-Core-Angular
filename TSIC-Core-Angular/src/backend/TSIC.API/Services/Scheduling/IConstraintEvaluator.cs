using TSIC.Contracts.Dtos.Scheduling;

namespace TSIC.API.Services.Scheduling;

/// <summary>
/// Evaluates a single scheduling constraint for a candidate game placement slot.
/// Each evaluator is independently testable and stateless.
/// </summary>
public interface IConstraintEvaluator
{
    /// <summary>Constraint name (matches the ConstraintPriorities list in AutoBuildV2Request).</summary>
    string Name { get; }

    /// <summary>Human-readable description of the trade-off when this constraint can't be satisfied.</summary>
    string SacrificeImpact { get; }

    /// <summary>
    /// Evaluate whether the candidate slot satisfies this constraint.
    /// </summary>
    /// <returns>True if the constraint is satisfied.</returns>
    bool Evaluate(CandidateSlot slot, GameContext game, DivisionSizeProfile profile,
                  PlacementState state);
}
