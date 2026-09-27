using TSIC.Contracts.Payments;
using TSIC.Contracts.Repositories;
using TSIC.Domain.Constants;

namespace TSIC.API.Services.Teams;

/// <summary>
/// The rep's mistake-undo test over the registered-team projection (rule: <see cref="TeamRegistrationUndo"/>).
/// ONE evaluation for both callers — the shaper stamps it on the grid row, unregister-team enforces
/// it — so the trash can a rep sees and the delete the server allows can never disagree.
/// </summary>
public static class TeamUndoEligibility
{
    /// <summary>Seconds left to undo at <paramref name="now"/>; 0 = not undoable (or window closed).</summary>
    public static int SecondsLeft(RegisteredTeamInfo team, PaymentState? state, DateTime now)
    {
        var noMoney = team.PaidTotal == 0m
            && !team.HasArbSubscription
            && (state == null || (state.TenderPaid == 0m && state.CorrectionApplied == 0m));
        if (!noMoney || !TeamRegistrationUndo.IsInHolding(team.DivisionName, team.AgeGroupName)) return 0;
        return TeamRegistrationUndo.SecondsLeft(team.RegistrationTs, now);
    }
}
