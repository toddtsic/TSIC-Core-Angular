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
    /// The proc credit actually applied to a single payment: the CC-rate proc for the
    /// principal — capped at the proc actually embedded in the entity's balance
    /// (<paramref name="embeddedProc"/>) so we never credit phantom proc — MINUS the
    /// directly-rounded method fee. Composed as a difference of two independently
    /// rounded fees, NOT <c>round(principal × (ccRate − methodRate))</c>: the fee the
    /// charge actually collects (<c>embeddedProc − credit</c>) must land on exactly
    /// <c>round(principal × methodRate)</c>. Rounding the rate-difference instead left
    /// the collected fee at <c>round(p×cc) − round(p×(cc−ec))</c>, which at an exact
    /// half-cent midpoint is a penny SHORT of <c>round(p×ec)</c>; the recalc
    /// (PaymentState.FeeProcessingTarget) then re-derived the correct figure and minted
    /// a phantom $0.01 owed (the $75 @ 3.8%/1.5% eCheck penny). For methodRate = 0
    /// (check/cash/correction) the composition is unchanged: min(round(p×cc), embedded).
    /// This is the canonical figure the charge engine debits AND the display/quote
    /// path subtracts — co-located here so the two cannot drift (a drift would
    /// re-trip the team eCheck AMOUNT_MISMATCH tripwire). The method-correct charge
    /// is <c>owed − AppliedProcCredit(principalRemaining, embeddedProc, ccRate, methodRate)</c>.
    /// See go-live 002 (Issues 1 &amp; 5).
    /// </summary>
    public static decimal AppliedProcCredit(decimal principalRemaining, decimal embeddedProc, decimal ccRate, decimal methodRate)
    {
        if (methodRate >= ccRate) return 0m; // CC (or any method at/above the CC rate) pays its own proc
        var ccProc = Math.Round(principalRemaining * ccRate, 2, MidpointRounding.AwayFromZero);
        var methodProc = methodRate > 0m
            ? Math.Round(principalRemaining * methodRate, 2, MidpointRounding.AwayFromZero)
            : 0m;
        var credit = Math.Min(ccProc, Math.Max(0m, embeddedProc)) - methodProc;
        return credit > 0m ? credit : 0m;
    }
}
