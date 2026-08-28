using TSIC.Domain.Constants;

namespace TSIC.Contracts.Payments;

/// <summary>
/// Pure rate-clamping for the configured store sales-tax rate. Sibling of
/// <see cref="ProcessingRateMath"/>, and the ONLY place a sales-tax percent becomes a
/// multiplier. Never divide a raw rate by 100 at a call site.
///
/// <para>
/// CONVENTION — <c>Jobs.StoreSalesTax</c> is stored in PERCENT form: <c>8.75</c> means 8.75%.
/// That matches its siblings <c>ProcessingFeePercent</c> (3.5) and <c>ECProcessingFeePercent</c>
/// (1.5), the config screen's own "(%)" label, and legacy's input bound of
/// <c>min 0 / max 12</c>, which is only meaningful as a percentage ceiling.
/// </para>
///
/// <para>
/// This is a DELIBERATE, documented divergence from legacy's arithmetic. Legacy multiplies the
/// raw column by the price with no division (IStoreService:1533, StoreSalesController:162),
/// i.e. it treats the column as a multiplier — which contradicts its own input widget and would
/// charge 875% on an entry of 8.75. That code has never executed against a non-zero rate: all
/// 654 StoreCartBatchSkus rows carry SalesTax = 0. There is no behaviour to preserve, so we
/// implement the convention the rest of the system uses rather than import a latent 100x defect.
/// Do not "restore" the legacy form.
/// </para>
///
/// <para>
/// Not to be confused with <c>Jobs.StoreTsicrate</c>, which IS a multiplier (0.10 = 10%) and is
/// internal remittance bookkeeping consumed by <c>adn.MonthyQBPExport_Automated_Merch</c>, never
/// charged to a buyer.
/// </para>
/// </summary>
public static class SalesTaxMath
{
    /// <summary>
    /// Clamp the raw sales-tax percent and convert to a decimal multiplier.
    /// A null or negative rate yields 0 — no tax, which is the current state of every job.
    /// </summary>
    public static decimal ToTaxMultiplier(decimal? rawPercent)
    {
        var raw = rawPercent ?? 0m;
        return System.Math.Clamp(raw, FeeConstants.MinSalesTaxPercent, FeeConstants.MaxSalesTaxPercent) / 100m;
    }

    /// <summary>
    /// The portion of a line that sales tax applies to.
    ///
    /// <para>
    /// Today this is the merchandise subtotal only: the credit-card convenience fee is a
    /// separate service charge and is not taxed. Several states DO tax delivery and service
    /// charges, so this is expressed as its own named concept rather than left implicit at the
    /// call site — when that day comes, the rule changes here and nowhere else.
    /// </para>
    /// </summary>
    public static decimal TaxableBase(decimal merchandiseSubtotal, decimal processingFee)
    {
        _ = processingFee; // not taxable under the current rules; named so the seam is visible
        return merchandiseSubtotal;
    }
}
