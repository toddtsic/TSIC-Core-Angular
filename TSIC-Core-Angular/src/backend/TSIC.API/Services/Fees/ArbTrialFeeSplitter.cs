using TSIC.Contracts.Payments;

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
/// The discount and late fee FRONT-LOAD onto the deposit (what is owed first) rather than
/// allocating proportionally, so the deposit charged equals the deposit the rep was shown
/// (the display column derives from the same FeeMath.DepositObligation). The donation does NOT
/// front-load — it is not discounted and is excluded from the displayed deposit-due, so it rides
/// the two charges on the raw deposit/balance ratio (preserving its processing treatment).
/// The bApplyProcessingFeesToTeamDeposit flag governs only where the PROCESSING fee lands.
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
    /// <param name="donation">Resolved donation add-on. Increases netBase like a late fee, so the
    /// donation principal is charged and processing is levied on it.</param>
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
        decimal donation,
        decimal processingRate,
        bool bAddProcessingFees,
        bool bApplyProcessingFeesToTeamDeposit)
    {
        var rawTotal = rawDeposit + rawBalance;

        // ── Principal split (discount + late fee front-load onto the deposit) ──
        // The discount and late fee land on the deposit — what is owed FIRST — not amortized
        // proportionally across deposit + balance, so the deposit charged here equals the deposit
        // the rep was SHOWN (RegisteredTeamShaper → PaymentState.DepositPrincipalRemaining, which
        // passes donation:0). Both derive the deposit obligation from the one shared formula
        // FeeMath.DepositObligation, so shown-deposit and charged-deposit cannot drift. The balance
        // is the remainder; a discount larger than the deposit spills onto the balance, and a
        // discount ≥ the whole principal zeroes both. Inputs are 2-dp so these are exact.
        var netPrincipal = System.Math.Max(rawTotal - discount + lateFee, 0m);
        var depositPrincipal = System.Math.Min(
            FeeMath.DepositObligation(rawDeposit, discount, lateFee, donation: 0m),
            netPrincipal);
        var balancePrincipal = netPrincipal - depositPrincipal;

        // ── Donation ride (unchanged) ──
        // A donation is not discounted and is not part of the owed obligation, so it does NOT
        // front-load. It rides the two charges on the raw deposit/balance ratio exactly as before,
        // preserving its processing treatment. (It is excluded from the displayed deposit-due, which
        // is why it is excluded from the front-load above — keeping charge and display aligned.)
        decimal depositDonation, balanceDonation;
        if (rawTotal == 0m)
        {
            depositDonation = 0m;
            balanceDonation = donation;
        }
        else
        {
            depositDonation = System.Math.Round(donation * rawDeposit / rawTotal, 2, System.MidpointRounding.AwayFromZero);
            balanceDonation = donation - depositDonation;
        }

        var depositBase = depositPrincipal + depositDonation;
        var balanceBase = balancePrincipal + balanceDonation;
        // Processing is levied on the actual principal + donation charged (== depositBase + balanceBase).
        // A discount never eats into the donation, so this can exceed the old
        // max(rawTotal − discount + lateFee + donation, 0) only in the degenerate case where the
        // discount alone exceeds the principal — there the donation is correctly preserved.
        var netBase = depositBase + balanceBase;

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
