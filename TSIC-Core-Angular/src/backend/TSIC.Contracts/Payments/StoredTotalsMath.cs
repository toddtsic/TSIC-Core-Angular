namespace TSIC.Contracts.Payments;

/// <summary>
/// Entity-anchored principal/proc split — the replacement for decoding the ledger.
///
/// Model ruling (2026-08-11): <c>Registration_Accounting.Payamt</c> is money-in, nothing
/// more. The ledger has NEVER carried a per-row principal/proc split in either era; that
/// split lives only in the entity columns (<c>fee_processing</c> / <c>paid_total</c> /
/// <c>owed_total</c>), maintained at write time when it was actually known. Deriving
/// principal by re-decoding CC rows as gross (<c>payamt ÷ (1+rate)</c>) is only valid for
/// rows the current charge engine wrote itself — legacy club-lump allocation rows book
/// flat principal, and decoding them minted ~3.4% of phantom money into real
/// <c>owed_total</c> on every reprice (proven in <c>LegacyFlatCcLedgerRepriceTests</c>).
///
/// This split reads ONLY the stored columns. Derivation, from the invariant the
/// per-payment handlers maintain (<c>FeeProcessing = proc collected + proc projected on
/// the remaining CC-billable principal</c>) and the totals identity
/// (<c>OwedTotal = remaining principal + projected proc</c>):
///
///   embedded  = min(feeProcessing, round(owedTotal × rate ÷ (1+rate)))   — proc still
///               inside owed. Exact whenever any principal is CC-billable (the raw share
///               then equals rate × billable-remaining); when the remainder is larger
///               (proc-free deposit slice still open) the raw share overshoots and the
///               feeProcessing cap binds — also exact, because an open deposit slice
///               means nothing has been collected yet, so ALL of feeProcessing is
///               projection. Settled/credit entities ⇒ 0.
///   collected = min(feeProcessing − embedded, paidTotal)                  — proc taken at swipe.
///               Capped at paidTotal: collected proc IS part of the money in, so a STALE
///               feeProcessing (an outdated projection the reprice exists to correct —
///               e.g. proc stamped before a discount landed) can never read as collected
///               on an entity with too little paid to have collected it.
///   principal = paidTotal − collected                                     — money-in that paid the bill.
///
/// For entities the current engine wrote, this reproduces the ledger decode to the cent.
/// For legacy or hand-adjusted entities it follows the books — by ruling, the truth.
/// </summary>
public static class StoredTotalsMath
{
    /// <summary>
    /// Split the entity's <paramref name="paidTotal"/> into proc collected at swipe and
    /// principal paid, using only the stored totals. With processing disabled (or a zero
    /// rate) every dollar is principal — same convention as the ledger decode it replaces.
    /// </summary>
    public static (decimal ProcCollected, decimal PrincipalPaid) Split(
        decimal paidTotal, decimal owedTotal, decimal feeProcessing, decimal ccRate, bool bAddProcessingFees)
    {
        if (!bAddProcessingFees || ccRate <= 0m)
            return (0m, paidTotal);

        var embedded = System.Math.Min(
            System.Math.Max(0m, feeProcessing),
            System.Math.Max(0m, System.Math.Round(
                owedTotal * ccRate / (1m + ccRate), 2, System.MidpointRounding.AwayFromZero)));
        var collected = System.Math.Min(
            System.Math.Max(0m, feeProcessing - embedded),
            System.Math.Max(0m, paidTotal));
        return (collected, System.Math.Max(0m, paidTotal - collected));
    }
}
