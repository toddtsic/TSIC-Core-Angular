/* ============================================================================
   14 - Store sales tax: make tax charges auditable and item-aware
   ----------------------------------------------------------------------------
   STATUS: PROPOSED. NOT APPLIED. Review before running.

   WHY NOW, WITH NO TAX BEING CHARGED
   ----------------------------------
   Every store sale to date carries SalesTax = 0 (654 of 654 rows), so these
   columns can be added with zero backfill risk and zero effect on any existing
   figure. The day a nexus obligation appears, the alternative is altering a
   money table that holds live transactions under a filing deadline. This is the
   cheap moment; there will not be another.

   WHAT THIS DOES NOT DO
   ---------------------
   Nothing here turns tax on. Rates stay 0.0000 on every job. This only makes a
   future tax charge defensible and item-aware.
   ============================================================================ */

SET XACT_ABORT ON;
BEGIN TRANSACTION;

/* ---------------------------------------------------------------------------
   1. Capture the RATE on the transaction, not just the amount.

   stores.StoreCartBatchSkus records SalesTax (the dollars) but not the rate
   that produced it. Without the rate:
     - a historical row cannot be audited - you cannot show a state auditor what
       rate you charged on a given sale, only what you collected;
     - changing a job's rate silently rewrites every historical report that
       recomputes tax, because the only surviving rate is the current one.

   Tax rates change - by statute, by jurisdiction, mid-year. A tax figure that
   cannot name its own rate is not defensible. decimal(9,5) holds a percent-form
   rate to five places (8.75000), enough for any combined state+local rate.
   --------------------------------------------------------------------------- */
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID(N'stores.StoreCartBatchSkus') AND name = 'SalesTaxRate')
BEGIN
    ALTER TABLE stores.StoreCartBatchSkus
        ADD SalesTaxRate decimal(9,5) NOT NULL
            CONSTRAINT DF_StoreCartBatchSkus_SalesTaxRate DEFAULT (0);
END;
GO

/* ---------------------------------------------------------------------------
   2. Item-level taxability.

   TSIC merch is overwhelmingly apparel, and apparel is where US sales tax is
   least uniform:
     - PA, NJ, MN, VT: clothing generally EXEMPT
     - NY: exempt under $110 per item (state portion)
     - MA: exempt under $175 per item
     - most other states: fully taxable
   A single job-level rate applied to every item is therefore wrong the first
   time a job sits in one of those states. One flag now keeps that a data change
   instead of a schema change under deadline.

   Default 1 (taxable) is the safe default: over-collecting is a refund, under-
   collecting is a liability.
   --------------------------------------------------------------------------- */
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID(N'stores.StoreItems') AND name = 'Taxable')
BEGIN
    ALTER TABLE stores.StoreItems
        ADD Taxable bit NOT NULL
            CONSTRAINT DF_StoreItems_Taxable DEFAULT (1);
END;
GO

COMMIT TRANSACTION;
GO

/* ---------------------------------------------------------------------------
   VERIFY
   --------------------------------------------------------------------------- */
SELECT  'StoreCartBatchSkus.SalesTaxRate' AS column_added,
        COUNT(*)                          AS rows_defaulted_to_zero,
        SUM(CASE WHEN SalesTax <> 0 THEN 1 ELSE 0 END) AS rows_with_tax_charged
FROM    stores.StoreCartBatchSkus;

SELECT  'StoreItems.Taxable' AS column_added,
        COUNT(*)             AS rows_defaulted_to_taxable
FROM    stores.StoreItems;
GO

/* ============================================================================
   DELIBERATELY NOT IN THIS SCRIPT
   ----------------------------------------------------------------------------
   A stores.TaxJurisdictions table (jurisdiction name, composite rate,
   EffectiveFrom / EffectiveTo) with the job pointing at a jurisdiction rather
   than carrying a bare decimal.

   That is the correct end state - it handles rate changes over time and lets a
   filing be produced per jurisdiction per period - but it is not needed until
   there is an actual obligation, and it is a clean migration ONCE THE TWO
   COLUMNS ABOVE EXIST. Adding them now is what makes the later work a migration
   rather than a rewrite of live money rows.

   The filing deliverable itself - tax collected by jurisdiction by period -
   belongs in the Reports Library as a stored procedure, following the same
   pattern as adn.MonthyQBPExport_Automated_Merch.
   ============================================================================ */
