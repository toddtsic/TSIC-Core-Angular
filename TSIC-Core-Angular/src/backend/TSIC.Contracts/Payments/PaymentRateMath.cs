namespace TSIC.Contracts.Payments;

/// <summary>
/// Pure rate-math helpers shared by per-payment write handlers and the
/// PaymentState read primitive. Co-locating ensures writers and readers can't
/// drift on what a "credit" is worth for a given payment method.
/// </summary>
public static class PaymentRateMath
{
    /// <summary>
    /// Proc-fee credit that backs out the baked-in CC assumption when a payment
    /// is taken by a method whose proc rate is <paramref name="methodRate"/>.
    /// This is the single source for every method's credit:
    ///   CC      → methodRate = ccRate     → 0 (CC pays its own proc)
    ///   eCheck  → methodRate = echeckRate  → principal × (ccRate − echeckRate)
    ///   check / cash / correction → methodRate = 0 → principal × ccRate
    /// Never negative (clamps when methodRate ≥ ccRate).
    /// </summary>
    public static decimal ProcCredit(decimal principal, decimal ccRate, decimal methodRate)
    {
        var diff = ccRate - methodRate;
        return diff > 0m ? principal * diff : 0m;
    }

    /// <summary>
    /// Gross to charge the gateway and record in Payamt for a principal paid by a
    /// method whose proc rate is <paramref name="methodRate"/> (0 for non-proc
    /// methods). Symmetric with how CcPrincipalPaid reverses out: gross =
    /// principal × (1 + methodRate). PaidTotal accumulates this same gross.
    /// </summary>
    public static decimal GrossForPrincipal(decimal principal, decimal methodRate) =>
        principal * (1m + methodRate);

    /// <summary>
    /// Full CC-rate credit applied when a payment method collects no proc fee
    /// at write time (check, cash, correction). Per-payment handler subtracts
    /// this from FeeProcessing so the remaining target reflects principal
    /// still potentially CC-billable. Equivalent to <see cref="ProcCredit"/>
    /// with methodRate = 0.
    /// </summary>
    public static decimal NonProcCheckCredit(decimal principal, decimal ccRate) =>
        ProcCredit(principal, ccRate, 0m);

    /// <summary>
    /// Partial credit when a payment collects proc at a lower rate than CC
    /// (eCheck). Credit = principal × (ccRate − echeckRate) — the "rate
    /// difference" the customer didn't pay because they used eCheck.
    /// Returns 0m if echeckRate ≥ ccRate (no credit owed). Equivalent to
    /// <see cref="ProcCredit"/> with methodRate = echeckRate.
    /// </summary>
    public static decimal EcheckPartialCredit(decimal principal, decimal ccRate, decimal echeckRate) =>
        ProcCredit(principal, ccRate, echeckRate);

    /// <summary>
    /// The proc credit actually applied to a single payment: <see cref="ProcCredit"/>
    /// rounded to cents, then capped at the proc actually embedded in the entity's
    /// balance (<paramref name="embeddedProc"/>) so we never credit phantom proc.
    /// This is the canonical figure the charge engine debits AND the display/quote
    /// path subtracts — co-located here so the two cannot drift (a drift would
    /// re-trip the team eCheck AMOUNT_MISMATCH tripwire). The method-correct charge
    /// is <c>owed − AppliedProcCredit(principalRemaining, embeddedProc, ccRate, methodRate)</c>.
    /// See go-live 002 (Issues 1 &amp; 5).
    /// </summary>
    public static decimal AppliedProcCredit(decimal principalRemaining, decimal embeddedProc, decimal ccRate, decimal methodRate)
    {
        var credit = Math.Round(ProcCredit(principalRemaining, ccRate, methodRate), 2, MidpointRounding.AwayFromZero);
        var cap = Math.Max(0m, embeddedProc);
        return credit > cap ? cap : credit;
    }
}
