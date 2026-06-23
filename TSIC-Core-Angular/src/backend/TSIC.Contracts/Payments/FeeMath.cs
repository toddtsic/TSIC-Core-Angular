namespace TSIC.Contracts.Payments;

/// <summary>
/// THE single definition of the registration/team fee-total and amount-owed formulas.
/// Every write path derives FeeTotal/OwedTotal from this — directly or via the
/// RecalcTotals entity helpers — so the arithmetic cannot drift across the codebase.
///
///   FeeTotal  = FeeBase + FeeProcessing − FeeDiscount + FeeDonation + FeeLatefee
///   OwedTotal = FeeTotal − PaidTotal
///
/// OwedTotal is signed: overpayment is negative and stays auditable. Clamping to ≥0 is a
/// display/charge concern owned by <see cref="PaymentState.ResolveOwed"/>, not this formula.
///
/// <para>
/// <b>FeeDiscountMp is intentionally NOT part of this formula.</b> It is a retired
/// "multi-player" discount used only by clients who have since left; the column is kept on
/// the Registrations/Teams entities as a reserved stub so it can be revived later, but no
/// active client relies on it (it is 0 for current registrations). To re-enable it, add a
/// <c>feeDiscountMp</c> parameter here and subtract it — <b>this method is the single place
/// that decision is made.</b> Legacy ARB/sweep paths that still subtract it are reconciled to
/// this formula during migration; behavior is unchanged for active clients where it is 0.
/// </para>
///
/// Pure, no I/O, decimal-only — trivially testable and shared by Registrations (non-nullable
/// columns) and Teams (nullable columns; callers coalesce with <c>?? 0m</c> before calling).
/// </summary>
public static class FeeMath
{
    /// <summary>
    /// FeeTotal from its components:
    /// <c>FeeBase + FeeProcessing − FeeDiscount + FeeDonation + FeeLatefee</c>.
    /// (FeeDiscountMp is intentionally excluded — see the type remarks.)
    /// </summary>
    public static decimal ComputeFeeTotal(
        decimal feeBase,
        decimal feeProcessing,
        decimal feeDiscount,
        decimal feeDonation,
        decimal feeLatefee)
        => feeBase + feeProcessing - feeDiscount + feeDonation + feeLatefee;

    /// <summary>OwedTotal = FeeTotal − PaidTotal. Signed: a negative result means overpayment.</summary>
    public static decimal ComputeOwed(decimal feeTotal, decimal paidTotal)
        => feeTotal - paidTotal;

    /// <summary>
    /// The deposit-phase principal obligation, BEFORE any payment:
    /// <c>max(0, deposit − discount + lateFee + donation)</c>.
    ///
    /// <para>
    /// <b>The discount front-loads onto the deposit</b> — what is owed first — rather than being
    /// amortized proportionally across deposit + balance. This is THE single definition of how a
    /// discount lands across the two phases, shared by the display column
    /// (<see cref="PaymentState.DepositPrincipalRemaining"/>) and the charge engine
    /// (ArbTrialFeeSplitter), so the deposit a club rep is SHOWN can never disagree with the deposit
    /// the gateway actually CHARGES. The balance obligation is the remainder
    /// (<c>totalNetPrincipal − depositObligation</c>); a discount larger than the deposit spills the
    /// excess onto the balance, and a discount ≥ the whole bill zeroes both.
    /// </para>
    /// Pure, no I/O, decimal-only.
    /// </summary>
    public static decimal DepositObligation(decimal deposit, decimal discount, decimal lateFee, decimal donation)
        => System.Math.Max(0m, deposit - discount + lateFee + donation);
}
