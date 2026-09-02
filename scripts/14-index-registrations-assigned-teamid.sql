/* =============================================================================
   14 - Covering index on Jobs.Registrations (assigned_teamID)

   WHY
   ---
   Jobs.Registrations has FOUR indexes and none of them is on a team:

       PK_Jobs.Registrations       CLUSTERED     RegistrationID
       UI_Registrations_Ai         NONCLUSTERED  RegistrationAI
       IX_Registrations_UserId     NONCLUSTERED  UserId
       IX_Registrations_Family_UserId            Family_UserId

   Every "who is on this team" question therefore scans the whole table --
   668,672 rows, 783 MB. Measured 2026-09-02 on the CJR year-over-year count,
   one pin, one customer:

       Registrations. logical reads 107,523      <- full clustered scan
       teams.         logical reads   5,770

   That is the cheap plan. The expensive one is what this index really guards
   against: phrased as a correlated EXISTS per team rather than a semi-join,
   the same question cost 3,994 ms against 71 ms and timed the endpoint out.
   The query was rewritten (0a5840ed9) so it no longer needs the index to be
   correct -- but it still reads 840 MB to answer a question about 37,362
   distinct teams, and `AssignedTeamId` appears in 309 places across the
   backend: rosters, swaps, self-roster, payments, search, clone, exports.

   SHAPE
   -----
   Key is assigned_teamID alone. It is the join column in nearly every one of
   those 309 sites, and leading with it serves both access patterns: a seek
   for "the roster of team X", and a narrow covering scan (~80 MB against
   783 MB) for the aggregate sweeps that filter by role and date instead.

   The three INCLUDE columns are the filters that ride along on essentially
   every one of those queries -- bActive, RoleId, RegistrationTS -- so the
   index answers them without touching the base table. RoleId is declared
   nvarchar(450) but holds 36-character GUID strings, 14 distinct values, so
   it costs ~72 bytes at the leaf and nothing in the B-tree.

   Not keyed (RoleId, bActive, RegistrationTS): that shape is marginally
   better for the CJR sweep alone and useless to every per-team lookup.

   SIZE / COST
   -----------
   ~120 bytes per row x 668,672 rows ~= 80 MB, about 10% of the table.
   Registrations is written on registration, payment and roster moves -- a
   fifth index is real write cost, but assigned_teamID changes only on a
   roster move, so the index is stable in normal operation.

   RUNNING IT
   ----------
   Dev (TSIC-SEDONA\SS2016) is SQL Server 2019 Developer Edition, so
   ONLINE = ON is available. CHECK THE EDITION ON PROD before running --
   ONLINE index builds are Enterprise/Developer only, and on Standard this
   statement fails outright rather than falling back. The offline build takes
   a schema-modification lock on Jobs.Registrations for its duration
   (seconds at this row count, but it blocks readers): run it in a window.

   Idempotent -- safe to re-run, does nothing if the index already exists.
   ============================================================================= */

USE TSICV5;
GO

SET NOCOUNT ON;
GO

IF EXISTS (SELECT 1 FROM sys.indexes
           WHERE object_id = OBJECT_ID('Jobs.Registrations')
             AND name = 'IX_Registrations_AssignedTeamId')
BEGIN
    PRINT 'IX_Registrations_AssignedTeamId already exists - nothing to do.';
END
ELSE
BEGIN
    PRINT 'Creating IX_Registrations_AssignedTeamId on Jobs.Registrations...';

    /* ---------------------------------------------------------------------
       ONLINE = ON requires Enterprise or Developer edition.
       On Standard, delete the WITH clause below (or set ONLINE = OFF) and
       run it in a maintenance window -- it will lock the table.
       --------------------------------------------------------------------- */
    CREATE NONCLUSTERED INDEX IX_Registrations_AssignedTeamId
        ON Jobs.Registrations (assigned_teamID)
        INCLUDE (bActive, RoleId, RegistrationTS)
        WITH (ONLINE = ON, DATA_COMPRESSION = PAGE, FILLFACTOR = 90);

    PRINT 'Created.';
END
GO

/* ---------------------------------------------------------------------------
   VERIFY -- run before and after, compare the Registrations read count.
   Expect ~107,000 logical reads before, roughly a tenth of that after.
   --------------------------------------------------------------------------- */
SET STATISTICS IO ON;
GO

DECLARE @player uniqueidentifier = 'DAC0C570-94AA-4A88-8D73-6034F1F72F3A';
DECLARE @pinEx  datetime         = '2026-09-01';
DECLARE @n int;

SELECT @n = COUNT(*)
FROM Leagues.teams t
WHERE t.active = 1
  AND t.createdate < @pinEx
  AND ISNULL(t.fee_total, 0) = 0
  AND t.teamID IN (SELECT r.assigned_teamID
                   FROM Jobs.Registrations r
                   WHERE r.bActive = 1
                     AND r.RoleId = @player
                     AND r.RegistrationTS < @pinEx);

PRINT 'free-but-populated teams: ' + CAST(@n AS varchar(20));
GO

SET STATISTICS IO OFF;
GO

/* Index size once built. Read from dm_db_partition_stats, NOT by joining
   sys.partitions to sys.allocation_units -- that join fans out one row per
   allocation unit and triples the row count. */
SELECT i.name,
       SUM(ps.used_page_count) * 8 / 1024 AS mb,
       SUM(ps.row_count)                  AS rows_
FROM sys.indexes i
JOIN sys.dm_db_partition_stats ps
     ON ps.object_id = i.object_id AND ps.index_id = i.index_id
WHERE i.object_id = OBJECT_ID('Jobs.Registrations')
  AND i.name = 'IX_Registrations_AssignedTeamId'
GROUP BY i.name;
GO

/* ---------------------------------------------------------------------------
   ROLLBACK
   --------------------------------------------------------------------------- */
-- DROP INDEX IX_Registrations_AssignedTeamId ON Jobs.Registrations;
