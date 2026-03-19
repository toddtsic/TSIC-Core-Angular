namespace TSIC.Application.Services.Teams;

/// <summary>
/// Interface for team registration fee calculation.
/// Handles both deposit and balance due phases with processing fee logic.
/// </summary>
public interface ITeamFeeCalculator
{
    /// <summary>
    /// Calculates team registration fees based on job settings and phase.
    /// Business rules:
    ///  - Deposit phase (BTeamsFullPaymentRequired=false): FeeBase = RosterFee
    ///  - Balance due phase (BTeamsFullPaymentRequired=true): FeeBase = RosterFee + TeamFee
    ///  - Processing fee calculated based on phase, flags, and payment status
    /// </summary>
    (decimal FeeBase, decimal FeeProcessing) CalculateTeamFees(
        decimal rosterFee,
        decimal teamFee,
        bool bTeamsFullPaymentRequired,
        bool bAddProcessingFees,
        bool bApplyProcessingFeesToTeamDeposit,
        decimal? jobProcessingFeePercent,
        decimal paidTotal,
        decimal currentFeeTotal);
}

/// <summary>
/// Consistent FeeTotal formula for team entities. Use this everywhere instead of inline math.
/// FeeTotal = FeeBase + FeeProcessing - FeeDiscount + FeeDonation + FeeLatefee.
/// OwedTotal = FeeTotal - PaidTotal.
/// </summary>
public static class TeamFeeExtensions
{
    /// <summary>
    /// Recalculates FeeTotal and OwedTotal from current fee fields.
    /// Call this after any change to FeeBase, FeeProcessing, FeeDiscount, FeeDonation, or FeeLatefee.
    /// </summary>
    public static void RecalcTotals(this TSIC.Domain.Entities.Teams team)
    {
        team.FeeTotal = (team.FeeBase ?? 0m)
                      + (team.FeeProcessing ?? 0m)
                      - (team.FeeDiscount ?? 0m)
                      + (team.FeeDonation ?? 0m)
                      + (team.FeeLatefee ?? 0m);
        team.OwedTotal = (team.FeeTotal ?? 0m) - (team.PaidTotal ?? 0m);
    }

    /// <summary>
    /// Sets FeeBase + FeeProcessing from calculator output, then recalculates totals.
    /// </summary>
    public static void ApplyCalculatedFees(this TSIC.Domain.Entities.Teams team, decimal feeBase, decimal feeProcessing)
    {
        team.FeeBase = feeBase;
        team.FeeProcessing = feeProcessing;
        team.RecalcTotals();
    }
}
