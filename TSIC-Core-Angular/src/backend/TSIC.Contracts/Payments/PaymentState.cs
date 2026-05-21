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
