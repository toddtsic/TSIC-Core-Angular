namespace TSIC.Contracts.Payments;

/// <summary>
/// THE single definition of the registration/team fee-total and amount-owed formulas.
/// Every write path derives FeeTotal/OwedTotal from this — directly, via the
/// RecalcTotals entity helpers, or via the SaveChanges fee-totals interceptor — so the
/// arithmetic cannot drift across the codebase.
///
///   FeeTotal  = FeeBase + FeeProcessing − FeeDiscount − FeeDiscountMp + FeeDonation + FeeLatefee
///   OwedTotal = FeeTotal − PaidTotal
///
/// OwedTotal is signed: overpayment is negative and stays auditable. Clamping to ≥0 is a
/// display/charge concern owned by <see cref="PaymentState.ResolveOwed"/>, not this formula.
///
/// Pure, no I/O, decimal-only — trivially testable and shared by Registrations (non-nullable
/// columns) and Teams (nullable columns; callers coalesce with <c>?? 0m</c> before calling).
/// </summary>
public static class FeeMath
{
    /// <summary>FeeTotal from its components. See the type remarks for the formula.</summary>
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
