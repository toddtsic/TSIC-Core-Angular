namespace TSIC.Contracts.Payments;

/// <summary>
/// THE single definition of the registration/team fee-total and amount-owed formulas.
/// Every write path derives FeeTotal/OwedTotal from this — directly or via the
/// RecalcTotals entity helpers — so the arithmetic cannot drift across the codebase.
///
///   FeeTotal  = FeeBase + FeeProcessing − FeeDiscount − FeeDiscountMp + FeeDonation + FeeLatefee
///   OwedTotal = FeeTotal − PaidTotal
///
/// OwedTotal is signed: overpayment is negative and stays auditable. Clamping to ≥0 is a
/// display/charge concern owned by <see cref="PaymentState.ResolveOwed"/>, not this formula.
///
/// <para>
/// <b>There are two discount buckets, and BOTH are subtracted, always.</b>
/// <list type="bullet">
///   <item><c>FeeDiscount</c> — early-bird (a FeeModifier) plus any redeemed discount code.</item>
///   <item><c>FeeDiscountMp</c> — reserved for a multi-player / sibling discount. It is currently
///     <b>0.00 on every row</b> (no code writes it), but it is wired through every calculation so
///     that building the feature later means computing a value, not re-opening this formula and
///     re-auditing every money path.</item>
/// </list>
/// The two exist separately because provenance matters: <c>FeeDiscount</c> is blind-overwritten by
/// <c>FeeResolutionService.ApplyNewRegistrationFeesAsync</c> when fees are re-stamped, so anything
/// stacked into it can be silently destroyed. Keep a sibling discount in its own column.
/// </para>
///
/// <para>
/// <b>Any code that subtracts a discount MUST subtract the same total this formula does</b> — use
/// <c>RegistrationFeeExtensions.TotalDiscount()</c> / <c>TeamFeeExtensions.TotalDiscount()</c>, never
/// <c>FeeDiscount</c> alone. A charge path that nets a different discount than FeeTotal bills an amount
/// that disagrees with what is owed, and OwedTotal can never reconcile. (CR-013: the ARB splitter
/// subtracted FeeDiscountMp while this formula ignored it — harmless only because the column was zero.)
/// </para>
///
/// Pure, no I/O, decimal-only — trivially testable and shared by Registrations (non-nullable
/// columns) and Teams (nullable columns; callers coalesce with <c>?? 0m</c> before calling).
/// </summary>
public static class FeeMath
{
    /// <summary>
    /// FeeTotal from its components:
    /// <c>FeeBase + FeeProcessing − FeeDiscount − FeeDiscountMp + FeeDonation + FeeLatefee</c>.
    /// Both discount buckets are subtracted — see the type remarks.
    /// </summary>
    public static decimal ComputeFeeTotal(
        decimal feeBase,
        decimal feeProcessing,
        decimal feeDiscount,
        decimal feeDiscountMp,
        decimal feeDonation,
        decimal feeLatefee)
        => feeBase + feeProcessing - feeDiscount - feeDiscountMp + feeDonation + feeLatefee;

    /// <summary>OwedTotal = FeeTotal − PaidTotal. Signed: a negative result means overpayment.</summary>
    public static decimal ComputeOwed(decimal feeTotal, decimal paidTotal)
        => feeTotal - paidTotal;
}
