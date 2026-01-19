namespace TSIC.Application.Services.Teams;

/// <summary>
/// Pure business logic for team registration fee calculation.
/// Handles both deposit phase and balance due phase with processing fee calculations.
/// No framework dependencies - pure business logic.
/// </summary>
public sealed class TeamFeeCalculator : ITeamFeeCalculator
{
    private readonly decimal _defaultProcessingFeePercent;

    public TeamFeeCalculator(decimal defaultProcessingFeePercent)
    {
        _defaultProcessingFeePercent = defaultProcessingFeePercent;
    }

    public (decimal FeeBase, decimal FeeProcessing) CalculateTeamFees(
        decimal rosterFee,
        decimal teamFee,
        bool bTeamsFullPaymentRequired,
        bool bAddProcessingFees,
        bool bApplyProcessingFeesToTeamDeposit,
        decimal? jobProcessingFeePercent,
        decimal paidTotal,
        decimal currentFeeTotal)
    {
        // Step 1: Calculate FeeBase based on phase
        decimal feeBase;
        if (bTeamsFullPaymentRequired)
        {
            // Balance due phase: full amount
            feeBase = rosterFee + teamFee;
        }
        else
        {
            // Deposit phase: roster fee only
            feeBase = rosterFee;
        }

        // Step 2: Calculate FeeProcessing
        decimal feeProcessing = 0m;

        // No processing fee if already fully paid
        if (paidTotal >= currentFeeTotal)
        {
            return (feeBase, feeProcessing);
        }

        // Calculate processing fee if enabled
        if (bAddProcessingFees)
        {
            // Get effective processing fee percentage (job override or default from appsettings)
            decimal processingFeePercent = jobProcessingFeePercent ?? _defaultProcessingFeePercent;

            if (bTeamsFullPaymentRequired)
            {
                // Balance due phase
                if (bApplyProcessingFeesToTeamDeposit)
                {
                    // Apply to full amount (RosterFee + TeamFee)
                    feeProcessing = feeBase * processingFeePercent;
                }
                else
                {
                    // Apply to TeamFee only
                    feeProcessing = teamFee * processingFeePercent;
                }
            }
            else
            {
                // Deposit phase
                if (bApplyProcessingFeesToTeamDeposit)
                {
                    // Apply to RosterFee
                    feeProcessing = rosterFee * processingFeePercent;
                }
                else
                {
                    // No processing fee in deposit phase if flag is false
                    feeProcessing = 0m;
                }
            }
        }

        return (feeBase, feeProcessing);
    }
}
