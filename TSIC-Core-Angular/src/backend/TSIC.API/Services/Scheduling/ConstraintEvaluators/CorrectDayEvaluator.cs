using TSIC.Contracts.Dtos.Scheduling;

namespace TSIC.API.Services.Scheduling.ConstraintEvaluators;

/// <summary>
/// Correct Day — preserve the same games/team/day ratio from the source.
/// Checks that the candidate slot falls on the expected day for this round,
/// maintaining the source schedule's day distribution pattern.
/// </summary>
public sealed class CorrectDayEvaluator : IConstraintEvaluator
{
    public string Name => "correct-day";
    public string SacrificeImpact => "Games scheduled on a different day of the week than the source pattern — the source's Saturday/Sunday distribution couldn't be fully preserved.";

    public bool Evaluate(CandidateSlot slot, GameContext game, DivisionSizeProfile profile,
                         PlacementState state)
    {
        // If the game context has an explicit target day (derived from source), use it
        if (game.TargetDay.HasValue)
            return slot.GDate.DayOfWeek == game.TargetDay.Value;

        // Otherwise, check if the slot's day is one of the profile's play days
        return profile.PlayDays.Contains(slot.GDate.DayOfWeek);
    }
}
