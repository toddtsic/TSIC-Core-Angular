/* =============================================================================
   22 - Two covering indexes on Jobs.Registration_Accounting

   WHY
   ---
   The table has exactly ONE index:

       PK_Jobs.Registration_Accounting   CLUSTERED   aID

   aID is a surrogate nobody joins on. Every real question -- "what did this
   team pay", "what did this registration pay" -- scans all 292,721 rows.
   Both join columns are unindexed:

       teamID           the club-rep route
       RegistrationID   the player route

   SQL Server's own missing-index DMV ranks the teamID shape at 99% impact,
   the highest on this database after Leagues.teams.

   This is also what made the CJR Teams/Players tab a 15.6-second hang on prod
   and 0.14s on dev, on identical data (fixed in 4fba64493 by removing the
   coalesce join). The rewrite no longer NEEDS these indexes to be correct --
   but without them the optimizer is still choosing between bad options on a
   table it cannot seek, which is how the two boxes diverged in the first
   place. Measured per-pin cost of the two queries these serve, dev, one
   customer, one year: playerOwing 265ms, teamOwing 247ms, and the CJR
   Year-over-Year tab pays both once per year column.

   SHAPE
   -----
   Keyed join column first, then the two filters that ride along on
   essentially every site (active, createdate), then the payload as INCLUDE.
   That serves a seek for one entity AND a narrow covering scan for the
   aggregate sweeps, without touching the base table either way.

   The DMV suggested leading with `active`. Deliberately not followed: active
   is a bit with two values, so leading on it wastes the B-tree. Leading on
   the join column serves both access patterns.

   SIZE / COST
   -----------
   ~60 bytes/row x 292,721 ~= 18 MB each, ~36 MB for both. Small.
   Registration_Accounting is written on every payment, so two more indexes
   is a real write cost -- but both keys are immutable once a ledger row is
   written, so neither index churns.

   RUNNING IT
   ----------
   Same edition trap as script 14. PROD IS STANDARD: ONLINE = ON fails
   outright with Msg 1712 rather than degrading, and DATA_COMPRESSION is
   Enterprise-only before 2016 SP1. The script detects both and builds the
   statement to match.

   On Standard the build is OFFLINE and takes a schema-modification lock on
   Registration_Accounting for its duration -- that blocks PAYMENT WRITES.
   At 292,721 rows expect a few seconds, but run it in a quiet window.

   The two indexes are independent. Run one, measure, run the other if you
   want -- each is separately guarded.

   Idempotent -- safe to re-run, does nothing if the index already exists.
   ============================================================================= */

USE TSICV5;
GO

SET NOCOUNT ON;
GO

/* --- BEFORE: baseline reads. Run this first and keep the number. --------- */
PRINT '--- BEFORE ---';
SET STATISTICS IO ON;
DECLARE @n1 int;
SELECT @n1 = COUNT(*) FROM Jobs.Registration_Accounting ra
 WHERE ra.teamID IS NOT NULL AND ra.active = 1 AND ra.createdate < GETDATE();
SET STATISTICS IO OFF;
GO

/* --- INDEX 1: the club-rep route (teamID). DMV: 99% impact. -------------- */
IF EXISTS (SELECT 1 FROM sys.indexes
           WHERE object_id = OBJECT_ID('Jobs.Registration_Accounting')
             AND name = 'IX_RegistrationAccounting_TeamId')
BEGIN
    PRINT 'IX_RegistrationAccounting_TeamId already exists - nothing to do.';
END
ELSE
BEGIN
    /* EngineEdition 3 is Enterprise -- and also Developer and Evaluation,
       which is why dev accepts ONLINE = ON and prod does not. */
    DECLARE @enterprise bit =
        CASE WHEN SERVERPROPERTY('EngineEdition') = 3 THEN 1 ELSE 0 END;
    DECLARE @major int = TRY_CAST(SERVERPROPERTY('ProductMajorVersion') AS int);
    DECLARE @build int = TRY_CAST(PARSENAME(CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(64)), 2) AS int);
    DECLARE @canCompress bit =
        CASE WHEN @enterprise = 1 OR @major >= 14
                  OR (@major = 13 AND @build >= 4001) THEN 1 ELSE 0 END;

    DECLARE @with nvarchar(200) =
        'FILLFACTOR = 90, SORT_IN_TEMPDB = ON'
        + CASE WHEN @enterprise  = 1 THEN ', ONLINE = ON'             ELSE '' END
        + CASE WHEN @canCompress = 1 THEN ', DATA_COMPRESSION = PAGE' ELSE '' END;

    DECLARE @sql nvarchar(max) =
        'CREATE NONCLUSTERED INDEX IX_RegistrationAccounting_TeamId'
        + ' ON Jobs.Registration_Accounting (teamID, active, createdate)'
        + ' INCLUDE (payamt, paymentMethodID, RegistrationID)'
        + ' WITH (' + @with + ');';

    PRINT 'Edition: ' + CAST(SERVERPROPERTY('Edition') AS nvarchar(128))
        + '  (' + CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(64)) + ')';
    PRINT CASE WHEN @enterprise = 1
               THEN 'ONLINE build - readers and writers continue.'
               ELSE 'OFFLINE build - Registration_Accounting is LOCKED (payment writes blocked) until it finishes.'
          END;
    PRINT @sql;
    EXEC sys.sp_executesql @sql;
    PRINT 'Created IX_RegistrationAccounting_TeamId.';
END
GO

/* --- INDEX 2: the player route (RegistrationID). ------------------------- */
IF EXISTS (SELECT 1 FROM sys.indexes
           WHERE object_id = OBJECT_ID('Jobs.Registration_Accounting')
             AND name = 'IX_RegistrationAccounting_RegistrationId')
BEGIN
    PRINT 'IX_RegistrationAccounting_RegistrationId already exists - nothing to do.';
END
ELSE
BEGIN
    DECLARE @enterprise2 bit =
        CASE WHEN SERVERPROPERTY('EngineEdition') = 3 THEN 1 ELSE 0 END;
    DECLARE @major2 int = TRY_CAST(SERVERPROPERTY('ProductMajorVersion') AS int);
    DECLARE @build2 int = TRY_CAST(PARSENAME(CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(64)), 2) AS int);
    DECLARE @canCompress2 bit =
        CASE WHEN @enterprise2 = 1 OR @major2 >= 14
                  OR (@major2 = 13 AND @build2 >= 4001) THEN 1 ELSE 0 END;

    DECLARE @with2 nvarchar(200) =
        'FILLFACTOR = 90, SORT_IN_TEMPDB = ON'
        + CASE WHEN @enterprise2  = 1 THEN ', ONLINE = ON'             ELSE '' END
        + CASE WHEN @canCompress2 = 1 THEN ', DATA_COMPRESSION = PAGE' ELSE '' END;

    DECLARE @sql2 nvarchar(max) =
        'CREATE NONCLUSTERED INDEX IX_RegistrationAccounting_RegistrationId'
        + ' ON Jobs.Registration_Accounting (RegistrationID, active, createdate)'
        + ' INCLUDE (payamt, paymentMethodID, teamID)'
        + ' WITH (' + @with2 + ');';

    PRINT @sql2;
    EXEC sys.sp_executesql @sql2;
    PRINT 'Created IX_RegistrationAccounting_RegistrationId.';
END
GO

/* --- AFTER: same probe. Compare the read count to the BEFORE number. ----- */
PRINT '--- AFTER ---';
SET STATISTICS IO ON;
DECLARE @n2 int;
SELECT @n2 = COUNT(*) FROM Jobs.Registration_Accounting ra
 WHERE ra.teamID IS NOT NULL AND ra.active = 1 AND ra.createdate < GETDATE();
SET STATISTICS IO OFF;
GO

SELECT i.name, i.type_desc,
       SizeMB = CAST(SUM(a.total_pages) * 8.0 / 1024 AS decimal(10,1))
FROM sys.indexes i
JOIN sys.partitions p ON p.object_id = i.object_id AND p.index_id = i.index_id
JOIN sys.allocation_units a ON a.container_id = p.partition_id
WHERE i.object_id = OBJECT_ID('Jobs.Registration_Accounting') AND i.type > 0
GROUP BY i.name, i.type_desc;
GO
