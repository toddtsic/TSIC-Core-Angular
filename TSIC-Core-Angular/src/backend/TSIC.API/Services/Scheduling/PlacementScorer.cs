using TSIC.Contracts.Dtos.Scheduling;

namespace TSIC.API.Services.Scheduling;

/// <summary>
/// Weighted scoring engine for V2 schedule placement.
/// Evaluates all candidate slots against prioritized constraints
/// and returns the highest-scoring slot.
/// </summary>
public static class PlacementScorer
{
    /// <summary>
    /// Find the best slot for a game from the available candidates.
    /// Short-circuits on perfect score for efficiency.
    /// </summary>
    /// <returns>The best candidate with its score and violations, or null if no candidates.</returns>
    public static ScoredCandidate? FindBestSlot(
        List<CandidateSlot> candidates,
        GameContext game,
        DivisionSizeProfile profile,
        List<(IConstraintEvaluator Evaluator, int Weight)> rankedConstraints,
        PlacementState state)
    {
        if (candidates.Count == 0)
            return null;

        var maxPossibleScore = rankedConstraints.Sum(c => c.Weight);

        ScoredCandidate? best = null;

        foreach (var candidate in candidates)
        {
            // Skip occupied slots
            if (state.OccupiedSlots.Contains((candidate.FieldId, candidate.GDate)))
                continue;

            // Skip BTB conflicts
            if (state.BtbTracker.HasConflict(
                game.DivId, game.T1No, game.T2No,
                candidate.GDate, state.BtbThresholdMinutes))
                continue;

            var score = 0;
            var violations = new List<string>();

            foreach (var (evaluator, weight) in rankedConstraints)
            {
                if (evaluator.Evaluate(candidate, game, profile, state))
                {
                    score += weight;
                }
                else
                {
                    violations.Add(evaluator.Name);
                }
            }

            // Perfect score — place immediately, no need to check further
            if (score == maxPossibleScore)
            {
                return new ScoredCandidate
                {
                    Slot = candidate,
                    Score = score,
                    MaxPossibleScore = maxPossibleScore,
                    Violations = []
                };
            }

            if (best == null || score > best.Score)
            {
                best = new ScoredCandidate
                {
                    Slot = candidate,
                    Score = score,
                    MaxPossibleScore = maxPossibleScore,
                    Violations = violations
                };
            }
        }

        return best;
    }

    /// <summary>
    /// Compute the weight for a constraint at a given priority rank.
    /// Uses squared weighting: weight = (totalConstraints - rank + 1)²
    /// </summary>
    public static int ComputeWeight(int priorityRank, int totalConstraints)
    {
        var base_ = totalConstraints - priorityRank + 1;
        return base_ * base_;
    }

    /// <summary>
    /// Build the ranked constraint list from priority names and evaluator instances.
    /// </summary>
    public static List<(IConstraintEvaluator Evaluator, int Weight)> BuildRankedConstraints(
        List<string> priorityNames,
        Dictionary<string, IConstraintEvaluator> evaluatorsByName)
    {
        var result = new List<(IConstraintEvaluator, int)>();
        var totalConstraints = priorityNames.Count;

        for (var i = 0; i < priorityNames.Count; i++)
        {
            var name = priorityNames[i];
            if (evaluatorsByName.TryGetValue(name, out var evaluator))
            {
                var weight = ComputeWeight(i + 1, totalConstraints);
                result.Add((evaluator, weight));
            }
        }

        return result;
    }
}
