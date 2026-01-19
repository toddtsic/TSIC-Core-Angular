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
    /// <param name="rosterFee">Age group roster fee</param>
    /// <param name="teamFee">Age group team fee</param>
    /// <param name="bTeamsFullPaymentRequired">Job phase: false=Deposit, true=Balance Due</param>
    /// <param name="bAddProcessingFees">Master switch for processing fees</param>
    /// <param name="bApplyProcessingFeesToTeamDeposit">Apply to RosterFee in deposit phase vs TeamFee only in balance due</param>
    /// <param name="jobProcessingFeePercent">Job-specific processing fee percentage (null uses default from appsettings)</param>
    /// <param name="paidTotal">Amount already paid</param>
    /// <param name="currentFeeTotal">Current total fee (to check if fully paid)</param>
    /// <returns>Tuple of (FeeBase, FeeProcessing)</returns>
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
