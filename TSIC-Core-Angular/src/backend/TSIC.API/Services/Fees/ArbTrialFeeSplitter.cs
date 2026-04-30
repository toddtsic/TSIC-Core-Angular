namespace TSIC.API.Services.Fees;

/// <summary>
/// Splits a single team's fees into the two ADN ARB charges (deposit + balance) for ARB-Trial mode.
///
/// Inputs are the same shape the existing FeeResolutionService produces:
///   - rawDeposit + rawBalance from the JobFees cascade (Team → Agegroup → Job)
///   - discount + lateFee already resolved on the registration
///   - processingRate + the two job-config flags that govern processing application
///
/// Output is the two charges ADN will run plus the per-charge processing portion.
/// Round-once-remainder: total processing is rounded once; deposit's share is rounded;
/// balance's share is the subtraction remainder so the two always sum exactly.
///
/// Discount and late fee fold into netBase universally and allocate proportionally
/// regardless of the bApplyProcessingFeesToTeamDeposit flag — the flag governs only
/// where the PROCESSING fee lands.
///
/// This helper has no I/O — pure math, easy to test.
/// </summary>
public static class ArbTrialFeeSplitter
{
    public sealed record Result
    {
        /// <summary>What ADN actually bills as the trial (deposit) charge.</summary>
        public required decimal DepositCharge { get; init; }
        /// <summary>What ADN actually bills as the post-trial (balance) charge.</summary>
        public required decimal BalanceCharge { get; init; }
        /// <summary>Processing-fee portion baked into DepositCharge.</summary>
        public required decimal DepositProcessing { get; init; }
        /// <summary>Processing-fee portion baked into BalanceCharge.</summary>
        public required decimal BalanceProcessing { get; init; }
        /// <summary>Total processing fee (DepositProcessing + BalanceProcessing).</summary>
        public required decimal TotalProcessing { get; init; }
    }

    /// <summary>
    /// Compute the deposit/balance split.
    /// </summary>
    /// <param name="rawDeposit">EffectiveDeposit from the JobFees cascade.</param>
    /// <param name="rawBalance">EffectiveBalanceDue from the JobFees cascade.</param>
    /// <param name="discount">Resolved discount (FeeDiscount + FeeDiscountMp). Reduces netBase.</param>
    /// <param name="lateFee">Resolved late fee. Increases netBase.</param>
    /// <param name="processingRate">Whole-percent rate as a fraction (e.g., 0.035 for 3.5%).</param>
    /// <param name="bAddProcessingFees">Per-job processing-fees-on-or-off flag.</param>
    /// <param name="bApplyProcessingFeesToTeamDeposit">
    /// When true, processing fees are levied on the full netBase and split proportionally
    /// between the two ADN charges. When false, processing applies to the balance charge only.
    /// </param>
    public static Result Split(
        decimal rawDeposit,
        decimal rawBalance,
        decimal discount,
        decimal lateFee,
        decimal processingRate,
        bool bAddProcessingFees,
        bool bApplyProcessingFeesToTeamDeposit)
    {
        var rawTotal = rawDeposit + rawBalance;
        var netBase = System.Math.Max(rawTotal - discount + lateFee, 0m);

        // Allocate netBase proportionally to the raw deposit/balance ratio. Defensive:
        // when rawTotal == 0, send everything to the balance side.
        decimal depositBase, balanceBase;
        if (rawTotal == 0m)
        {
            depositBase = 0m;
            balanceBase = netBase;
        }
        else
        {
            depositBase = System.Math.Round(netBase * rawDeposit / rawTotal, 2, System.MidpointRounding.AwayFromZero);
            balanceBase = netBase - depositBase;
        }

        decimal totalProcessing = 0m;
        decimal depositProcessing = 0m;
        decimal balanceProcessing = 0m;

        if (bAddProcessingFees && netBase > 0m)
        {
            if (bApplyProcessingFeesToTeamDeposit)
            {
                // Processing on the full netBase, split proportionally between the two charges.
                totalProcessing = System.Math.Round(netBase * processingRate, 2, System.MidpointRounding.AwayFromZero);
                if (rawTotal == 0m)
                {
                    depositProcessing = 0m;
                    balanceProcessing = totalProcessing;
                }
                else
                {
                    depositProcessing = System.Math.Round(totalProcessing * rawDeposit / rawTotal, 2, System.MidpointRounding.AwayFromZero);
                    balanceProcessing = totalProcessing - depositProcessing;
                }
            }
            else
            {
                // Processing on balance side only — rate × balanceBase.
                balanceProcessing = System.Math.Round(balanceBase * processingRate, 2, System.MidpointRounding.AwayFromZero);
                depositProcessing = 0m;
                totalProcessing = balanceProcessing;
            }
        }

        var depositCharge = depositBase + depositProcessing;
        var balanceCharge = balanceBase + balanceProcessing;

        return new Result
        {
            DepositCharge = depositCharge,
            BalanceCharge = balanceCharge,
            DepositProcessing = depositProcessing,
            BalanceProcessing = balanceProcessing,
            TotalProcessing = totalProcessing,
        };
    }
}
