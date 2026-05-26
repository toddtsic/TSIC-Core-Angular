namespace TSIC.Contracts.Payments;

/// <summary>
/// Canonical "current payment state" for a single registration or team.
///
/// Captures actual splits across the five payment methods and the job-rate
/// context needed to interpret them. All consumer-facing math (recalc target,
/// principal-still-owed, proc-fee-due) lives here as derived properties so
/// recalc paths and display paths cannot drift.
///
/// Why each method matters:
///   CC      — gross stored in Payamt; principal recovered via /(1+ccRate) when
///             proc fees are enabled. Proc was collected at swipe.
///   eCheck  — gross stored in Payamt; principal recovered via /(1+echeckRate)
///             when proc fees are enabled (symmetric with CC). Proc collected at
///             swipe at echeckRate. Per-payment handler decrements FeeProcessing
///             by principal × (ccRate − echeckRate).
///   Check, Cash, Correction — principal stored in Payamt; no proc collected.
///             Per-payment handler decrements FeeProcessing by principal × ccRate.
/// </summary>
public record PaymentState
{
    public required decimal CcGrossPaid { get; init; }
    public required decimal EcheckGrossPaid { get; init; }
    public required decimal CheckPaid { get; init; }
    public required decimal CashPaid { get; init; }
    public required decimal CorrectionApplied { get; init; }

    public required bool BAddProcessingFees { get; init; }
    public required decimal CcRate { get; init; }
    public required decimal EcheckRate { get; init; }

    // ── Derived: principal vs proc splits per method ──

    public decimal CcPrincipalPaid =>
        BAddProcessingFees && CcRate > 0m ? CcGrossPaid / (1m + CcRate) : CcGrossPaid;

    public decimal CcProcCollected => CcGrossPaid - CcPrincipalPaid;

    // eCheck mirrors CC: gross stored in Payamt, principal reversed out at echeckRate.
    public decimal EcheckPrincipalPaid =>
        BAddProcessingFees && EcheckRate > 0m ? EcheckGrossPaid / (1m + EcheckRate) : EcheckGrossPaid;

    public decimal EcheckProcCollected => EcheckGrossPaid - EcheckPrincipalPaid;

    // ── Derived: aggregates ──

    public decimal NonProcCarryingPaid => CheckPaid + CashPaid + CorrectionApplied;

    public decimal PrincipalPaid =>
        CcPrincipalPaid + EcheckPrincipalPaid + NonProcCarryingPaid;

    public decimal ProcCollected => CcProcCollected + EcheckProcCollected;

    // GrossPaid mirrors what gets summed into entity.PaidTotal at write time:
    // gross for CC & eCheck (Payamt = gross), principal for the non-proc methods.
    public decimal GrossPaid =>
        CcGrossPaid + EcheckGrossPaid + NonProcCarryingPaid;

    // ── Canonical consumer-facing helpers ──

    /// <summary>
    /// FeeProcessing target invariant: total proc collected so far + proc that
    /// would be collected if remaining principal were paid by CC. Matches the
    /// entity-level state per-payment handlers maintain incrementally.
    /// </summary>
    public decimal FeeProcessingTarget(decimal feeBase, decimal discount, decimal lateFee)
    {
        if (!BAddProcessingFees) return 0m;
        var principalBase = feeBase - discount + lateFee;
        var remainingCcBillable = System.Math.Max(0m, principalBase - PrincipalPaid);
        return ProcCollected + remainingCcBillable * CcRate;
    }

    /// <summary>
    /// Principal still owed if the remainder is paid by check (no further proc).
    /// </summary>
    public decimal PrincipalRemaining(decimal feeBase, decimal discount, decimal lateFee) =>
        System.Math.Max(0m, (feeBase - discount + lateFee) - PrincipalPaid);

    /// <summary>
    /// Proc-fee component currently owed (display "ProcFee Due") — what would
    /// still be charged as proc if the remaining principal is CC-billed.
    /// </summary>
    public decimal ProcFeeDue(decimal feeBase, decimal discount, decimal lateFee) =>
        BAddProcessingFees
            ? PrincipalRemaining(feeBase, discount, lateFee) * CcRate
            : 0m;

    /// <summary>
    /// Per-payment proc credit for a CC-rate <paramref name="ccCharge"/> being settled
    /// at <paramref name="methodRate"/> (eCheck, or any rate &lt; <see cref="CcRate"/>).
    /// Returns the amount to back out of FeeProcessing / OwedTotal so the gateway
    /// debit is recomputed at the lower method rate.
    ///
    /// Two caps applied:
    ///   - <c>AppliedProcCredit</c> already caps at the entity's remaining
    ///     <paramref name="feeProcessing"/> (handles proc-disabled jobs → 0).
    ///   - Per-charge cap: <c>max(0, ccCharge − principalRemaining)</c>. A partial
    ///     payment (e.g. a Deposit smaller than the principal owed) doesn't carry
    ///     full CC proc — only credit what's actually embedded in THIS charge.
    ///
    /// Returns 0 for CC (<paramref name="methodRate"/> ≥ <see cref="CcRate"/>), for
    /// proc-disabled jobs, and for principal-only charges.
    ///
    /// For a full-pay (<paramref name="ccCharge"/> == OwedTotal) this is equivalent
    /// to <c>OwedTotal − ResolveOwed(...).Echeck</c>; the standalone form exists for
    /// partial-pay paths (the registration deposit flow) that <see cref="ResolveOwed"/>
    /// doesn't model.
    /// </summary>
    public decimal ProcCreditForCharge(
        decimal ccCharge, decimal feeBase, decimal discount, decimal lateFee,
        decimal feeProcessing, decimal methodRate)
    {
        if (!BAddProcessingFees || methodRate >= CcRate) return 0m;
        var principalRemaining = PrincipalRemaining(feeBase, discount, lateFee);
        var rawCredit = PaymentRateMath.AppliedProcCredit(principalRemaining, feeProcessing, CcRate, methodRate);
        var procEmbeddedInCharge = System.Math.Max(0m, ccCharge - principalRemaining);
        return System.Math.Min(rawCredit, procEmbeddedInCharge);
    }

    /// <summary>
    /// Amount still owed under each payment method, anchored to the entity's
    /// authoritative <paramref name="owedTotal"/> (which already carries proc,
    /// donation, late fee, and discount). The single canonical owed resolver:
    /// display, the charge engine, and admin quoting all call this, so a shown
    /// total can never disagree with what is actually charged or recorded.
    ///
    ///   Cc     → full owedTotal           (CC pays its own proc → credit 0)
    ///   Check  → owedTotal − full CC proc  (== Cash == Correction; methodRate 0)
    ///   Echeck → owedTotal − partial credit (CC rate − eCheck rate)
    ///
    /// One formula, three rates. Credits come from PaymentRateMath.AppliedProcCredit
    /// (rounded + capped at the proc embedded in the balance), so no method can
    /// over-credit phantom proc and proc-disabled jobs fall through to owedTotal.
    /// </summary>
    public OwedByMethod ResolveOwed(
        decimal owedTotal, decimal feeBase, decimal discount, decimal lateFee, decimal feeProcessing)
    {
        var principalRemaining = PrincipalRemaining(feeBase, discount, lateFee);
        decimal OwedFor(decimal methodRate) => System.Math.Max(
            0m,
            owedTotal - PaymentRateMath.AppliedProcCredit(principalRemaining, feeProcessing, CcRate, methodRate));

        return new OwedByMethod(
            Cc: OwedFor(CcRate),
            Check: OwedFor(0m),
            Echeck: OwedFor(EcheckRate));
    }

    public static PaymentState Empty(bool bAddProcessingFees, decimal ccRate, decimal echeckRate) =>
        new()
        {
            CcGrossPaid = 0m,
            EcheckGrossPaid = 0m,
            CheckPaid = 0m,
            CashPaid = 0m,
            CorrectionApplied = 0m,
            BAddProcessingFees = bAddProcessingFees,
            CcRate = ccRate,
            EcheckRate = echeckRate,
        };
}

/// <summary>
/// Amount owed under each payment method (Check == Cash == Correction — all proc-free).
/// Produced by <see cref="PaymentState.ResolveOwed"/>, the single per-method owed resolver.
/// </summary>
public readonly record struct OwedByMethod(decimal Cc, decimal Check, decimal Echeck);
