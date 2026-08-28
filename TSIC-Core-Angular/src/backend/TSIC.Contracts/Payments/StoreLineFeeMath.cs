using TSIC.Contracts.Repositories;
using TSIC.Domain.Entities;

namespace TSIC.Contracts.Payments;

/// <summary>
/// The ONE resolver for what a store line item costs.
///
/// <para>
/// Every path that creates or changes a line — add to cart, change quantity, walk-up sale, and the
/// admin SKU swap — routes through <see cref="Recalculate"/>. Legacy recomputed the same four
/// figures inline in each of those places, which is how the swap in
/// StoreSalesController.UpdateCartSku came to use a different sales-tax convention from the
/// shopper path. One resolver makes that class of drift impossible.
/// </para>
/// </summary>
public static class StoreLineFeeMath
{
    /// <summary>
    /// Set FeeProduct / FeeProcessing / SalesTax / FeeTotal from UnitPrice × Quantity.
    ///
    /// <para>
    /// LEGACY SEMANTICS (authoritative): FeeProduct is the merchandise subtotal and FeeTotal is
    /// the line GRAND total (product + processing + tax) — matching all 476 historical rows.
    /// FeeTotal is NOT fees-only; never add the subtotal to it again downstream.
    /// </para>
    ///
    /// <para>
    /// The CC processing fee comes from the job's Payment settings (ProcessingFeePercent), the
    /// same source as registration fees; Jobs.StoreTsicrate is TSIC commission bookkeeping and is
    /// never a customer-facing rate. Sales tax runs through <see cref="SalesTaxMath"/> so the
    /// percent-vs-multiplier convention is decided in exactly one place. Tax is NOT part of
    /// FeeProduct, so it never enters the TSIC commission base in
    /// adn.MonthyQBPExport_Automated_Merch.
    /// </para>
    /// </summary>
    public static void Recalculate(StoreCartBatchSkus lineItem, JobStoreConfig config)
    {
        var subtotal = lineItem.UnitPrice * lineItem.Quantity;

        lineItem.FeeProcessing = Math.Round(
            subtotal * ProcessingRateMath.ToCcMultiplier(config.ProcessingFeePercent),
            2, MidpointRounding.AwayFromZero);

        // Applied to an explicitly named taxable base rather than to `subtotal` by coincidence.
        // The base excludes the CC convenience fee today; states that tax service charges change
        // that rule in SalesTaxMath.TaxableBase and nowhere else.
        var taxableBase = SalesTaxMath.TaxableBase(subtotal, lineItem.FeeProcessing);
        lineItem.SalesTax = Math.Round(
            taxableBase * SalesTaxMath.ToTaxMultiplier(config.StoreSalesTax),
            2, MidpointRounding.AwayFromZero);

        lineItem.FeeProduct = subtotal;
        lineItem.FeeTotal = lineItem.FeeProduct + lineItem.FeeProcessing + lineItem.SalesTax;
    }
}
