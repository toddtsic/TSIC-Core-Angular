namespace TSIC.Contracts.Payments;

/// <summary>
/// Pure rate-math helpers shared by per-payment write handlers and the
/// PaymentState read primitive. Co-locating ensures writers and readers can't
/// drift on what a "credit" is worth for a given payment method.
/// </summary>
public static class PaymentRateMath
{
    /// <summary>
    /// Full CC-rate credit applied when a payment method collects no proc fee
    /// at write time (check, cash, correction). Per-payment handler subtracts
    /// this from FeeProcessing so the remaining target reflects principal
    /// still potentially CC-billable.
    /// </summary>
    public static decimal NonProcCheckCredit(decimal principal, decimal ccRate) =>
        principal * ccRate;

    /// <summary>
    /// Partial credit when a payment collects proc at a lower rate than CC
    /// (eCheck). Credit = principal × (ccRate − echeckRate) — the "rate
    /// difference" the customer didn't pay because they used eCheck.
    /// Returns 0m if echeckRate ≥ ccRate (no credit owed).
    /// </summary>
    public static decimal EcheckPartialCredit(decimal principal, decimal ccRate, decimal echeckRate)
    {
        var diff = ccRate - echeckRate;
        return diff > 0m ? principal * diff : 0m;
    }
}
