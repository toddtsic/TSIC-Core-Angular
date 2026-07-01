/* =============================================================================
   adn.JobsAtCurrentCCRate
   -----------------------------------------------------------------------------
   Chelsea's CC-rate bonus, pivoted by settlement year/month.

   Seed set = every job whose EFFECTIVE processing rate is the current rate
   (3.8%). Because that set is tiny we drive the whole query from it and only
   ever touch those jobs' transactions -- no full scan of adn.Txs.

   For each seed job we sum ABS(Settlement Amount) over its settled CC
   transactions per settlement month (payments AND refunds both count -- the
   processor charges the fee on gross throughput in either direction), then:

       Bonus = (0.038 - 0.035) * CcDollars / 3

   Output: one row per job, one column per YYYY-MM period, cell = that month's
   bonus, plus a per-job TotalBonus column and a grand-total row. Chelsea's
   payout for any month is the grand-total row's column for that month.

   Business rules (confirmed with Todd, 2026-07-01):
     * Qualifying job = effective rate == 3.8% exactly. Effective rate is
       Math.Clamp(Jobs.ProcessingFeePercent ?? 3.5, 3.5, 4.0) -- see
       IFeeResolutionService.GetEffectiveProcessingRateAsync. 3.8 is inside the
       clamp band, so this reduces to ProcessingFeePercent = 3.80; NULL (=> 3.5)
       and 3.9/4.0 overrides are excluded.
     * Job attribution mirrors adn.rpt_invoice / ReportingRepository:
       adn.Txs.[Transaction ID] = Jobs.Registration_Accounting.adnTransactionID.
     * Settlement month parsed from the fixed-layout "DD-Mon-YYYY" text.

   Params:
     @CurrentRate  0.0380  rate the seed jobs were charged (also defines the seed)
     @BaselineRate 0.0350  prior rate the bonus is measured against
     @BonusDivisor 3.0     Chelsea's share = 1 / @BonusDivisor of the delta
     @MinYear/@MaxYear     optional inclusive settlement-year bounds (NULL = all)

   Usage:  EXEC adn.JobsAtCurrentCCRate;                       -- all history
           EXEC adn.JobsAtCurrentCCRate @MinYear = 2026;       -- 2026 onward
   ============================================================================= */
CREATE OR ALTER PROCEDURE adn.JobsAtCurrentCCRate
    @CurrentRate   decimal(6,4) = 0.0380,
    @BaselineRate  decimal(6,4) = 0.0350,
    @BonusDivisor  decimal(9,4) = 3.0,
    @MinYear       int          = NULL,
    @MaxYear       int          = NULL
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @CurrentPct decimal(4,2) = CAST(@CurrentRate * 100 AS decimal(4,2));  -- 0.0380 -> 3.80

    -- ── Distinct settled CC transaction per seed job, with parsed settlement period.
    --    DISTINCT on [Transaction ID]+job collapses the family-pays-many-registrations
    --    fan-out (one settlement -> several Registration_Accounting rows, same job).
    SELECT DISTINCT
        t.[Transaction ID]                             AS TransactionId,
        j.jobID                                        AS JobId,
        c.customerName                                 AS CustomerName,
        j.jobName                                      AS JobName,
        p.PeriodYear,
        p.PeriodMonth,
        ABS(TRY_CONVERT(money, t.[Settlement Amount])) AS SettledAbs
    INTO #Tx
    FROM [Jobs].[Jobs] AS j
    INNER JOIN [Jobs].[Customers] AS c
            ON c.customerID = j.customerID
    INNER JOIN [Jobs].[Registrations] AS r
            ON r.jobID = j.jobID
    INNER JOIN [Jobs].[Registration_Accounting] AS ra
            ON ra.RegistrationID = r.RegistrationID
           AND ra.active = 1
    INNER JOIN [reference].[Accounting_PaymentMethods] AS pm
            ON pm.paymentMethodID = ra.paymentMethodID
           AND pm.paymentMethod IN ('Credit Card Payment', 'Credit Card Credit')
    INNER JOIN adn.Txs AS t
            ON t.[Transaction ID] = ra.adnTransactionID
           AND t.[Transaction Status] NOT IN ('Declined', 'Voided')
    CROSS APPLY (VALUES (
        TRY_CONVERT(int, SUBSTRING(t.[Settlement Date Time], 8, 4)),
        CASE SUBSTRING(t.[Settlement Date Time], 4, 3)
            WHEN 'Jan' THEN 1  WHEN 'Feb' THEN 2  WHEN 'Mar' THEN 3
            WHEN 'Apr' THEN 4  WHEN 'May' THEN 5  WHEN 'Jun' THEN 6
            WHEN 'Jul' THEN 7  WHEN 'Aug' THEN 8  WHEN 'Sep' THEN 9
            WHEN 'Oct' THEN 10 WHEN 'Nov' THEN 11 WHEN 'Dec' THEN 12
        END
    )) AS p(PeriodYear, PeriodMonth)
    WHERE j.ProcessingFeePercent = @CurrentPct
      AND t.[Settlement Date Time] IS NOT NULL
      AND TRY_CONVERT(money, t.[Settlement Amount]) IS NOT NULL
      AND p.PeriodYear IS NOT NULL
      AND p.PeriodMonth IS NOT NULL
      -- CC-Payment lines only count once settled (carry an invoice #); credits are exempt.
      AND (pm.paymentMethod <> 'Credit Card Payment' OR t.[Invoice Number] IS NOT NULL)
      AND (@MinYear IS NULL OR p.PeriodYear >= @MinYear)
      AND (@MaxYear IS NULL OR p.PeriodYear <= @MaxYear);

    -- ── Result set 1: raw detail — one row per job per settlement month.
    SELECT
        CustomerName                                                           AS Customer,
        JobName                                                                AS Job,
        PeriodYear                                                             AS [Year],
        PeriodMonth                                                            AS [Month],
        CAST(SUM(SettledAbs) AS decimal(18,2))                                 AS CcDollars,      -- CC$
        CAST(@CurrentRate  * SUM(SettledAbs) AS decimal(18,2))                 AS FeeAt038,       -- CC$ x 3.8%
        CAST(@BaselineRate * SUM(SettledAbs) AS decimal(18,2))                 AS FeeAt035,       -- CC$ x 3.5%
        CAST((@CurrentRate - @BaselineRate) * SUM(SettledAbs) AS decimal(18,2)) AS RateDelta,     -- 3.8% - 3.5%
        CAST((@CurrentRate - @BaselineRate) * SUM(SettledAbs) / @BonusDivisor AS decimal(18,2)) AS Bonus  -- (3.8% - 3.5%) / 3
    FROM #Tx
    GROUP BY CustomerName, JobName, PeriodYear, PeriodMonth
    ORDER BY CustomerName, JobName, PeriodYear, PeriodMonth;

    -- ── Bonus per job per period (rank 0), plus a grand-total row (rank 1).
    CREATE TABLE #Detail
    (
        SortRank     tinyint      NOT NULL,
        CustomerName nvarchar(200) NULL,
        JobName      nvarchar(200) NULL,
        Period       char(7)      NOT NULL,   -- 'YYYY-MM'
        Bonus        decimal(18,2) NOT NULL
    );

    INSERT #Detail (SortRank, CustomerName, JobName, Period, Bonus)
    SELECT
        0,
        CustomerName,
        JobName,
        RIGHT('0000' + CONVERT(varchar(4), PeriodYear), 4) + '-' +
        RIGHT('00'   + CONVERT(varchar(2), PeriodMonth), 2),
        CAST((@CurrentRate - @BaselineRate) * SUM(SettledAbs) / @BonusDivisor AS decimal(18,2))
    FROM #Tx
    GROUP BY CustomerName, JobName, PeriodYear, PeriodMonth;

    INSERT #Detail (SortRank, CustomerName, JobName, Period, Bonus)
    SELECT 1, 'ALL 3.8% JOBS', '', Period, SUM(Bonus)
    FROM #Detail
    GROUP BY Period;

    -- ── Dynamic PIVOT: one column per YYYY-MM period present.
    DECLARE @cols nvarchar(max), @sumExpr nvarchar(max);
    SELECT
        @cols    = STRING_AGG(QUOTENAME(Period), ',')                       WITHIN GROUP (ORDER BY Period),
        @sumExpr = STRING_AGG('ISNULL(' + QUOTENAME(Period) + ',0)', '+')   WITHIN GROUP (ORDER BY Period)
    FROM (SELECT DISTINCT Period FROM #Detail) AS q;

    IF @cols IS NULL
    BEGIN
        SELECT CAST(NULL AS nvarchar(200)) AS CustomerName
        WHERE 1 = 0;   -- no qualifying activity: empty shape
        RETURN;
    END;

    DECLARE @sql nvarchar(max) = N'
        SELECT
            CustomerName,
            JobName,
            ' + @sumExpr + N' AS TotalBonus,
            ' + @cols + N'
        FROM (SELECT SortRank, CustomerName, JobName, Period, Bonus FROM #Detail) AS src
        PIVOT (SUM(Bonus) FOR Period IN (' + @cols + N')) AS pvt
        ORDER BY SortRank, CustomerName, JobName;';

    EXEC sys.sp_executesql @sql;
END
GO
