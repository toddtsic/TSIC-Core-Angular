/* =====================================================================
   PROD DIAGNOSTIC #2 — CJR Teams/Players: Q4 is 15,616ms on prod vs
   280ms on dev, on identical data with identical indexes.

   READ-ONLY. Nothing here changes data or schema.
   Run on TSIC-PHOENIX .SS2016, database TSICV5.

   WHAT IT SEPARATES:
     Run 1 slow, runs 2/3 fast          -> cold buffer pool. The first read
                                           pulled pages off disk; prod just
                                           doesn't hold them resident.
     All three runs slow, CPU ~ elapsed -> prod picked a worse PLAN.
                                           Compare DOP + reads below to dev.
     All three slow, CPU << elapsed     -> waiting on IO or locks, every time.
                                           Storage or contention, not the query.

   Dev baseline: 280ms, 578 rows.
   ===================================================================== */
SET NOCOUNT ON;

DECLARE @cust  uniqueidentifier = '76586DE3-ACB3-42EE-91EB-48597CA06802'; -- American Select Lacrosse
DECLARE @start datetime = '2026-08-01';
DECLARE @endEx datetime = DATEADD(day, 1, CAST(GETDATE() AS date));
DECLARE @ids TABLE (CustomerId uniqueidentifier);
DECLARE @g int = (SELECT TOP 1 CustomerGroupId FROM Jobs.CustomerGroupCustomers WHERE CustomerId = @cust);
IF @g IS NULL INSERT @ids VALUES (@cust);
ELSE INSERT @ids SELECT CustomerId FROM Jobs.CustomerGroupCustomers WHERE CustomerGroupId = @g;

DECLARE @t0 datetime2, @n int, @r1 int, @r2 int, @r3 int;

SET STATISTICS IO ON;    /* logical vs PHYSICAL/read-ahead reads -> Messages */
SET STATISTICS TIME ON;  /* CPU vs elapsed per run              -> Messages */

/* ---- Q4, run 1 (cold) -------------------------------------------- */
SET @t0 = SYSDATETIME();
SELECT @n = COUNT(*) FROM (
  SELECT t.teamID, YEAR(ra.createdate) y, MONTH(ra.createdate) m
    FROM Jobs.Registration_Accounting ra
    JOIN Jobs.Registrations r ON ra.RegistrationID = r.registrationID
    JOIN Leagues.teams t ON ISNULL(ra.teamID, r.assigned_teamID) = t.teamID
    JOIN Jobs.Jobs j ON t.jobID = j.jobID
   WHERE j.CustomerId IN (SELECT CustomerId FROM @ids)
     AND j.expiryUsers >= @start AND t.active = 1
     AND ra.active = 1 AND ra.createdate IS NOT NULL AND ra.createdate < @endEx
   GROUP BY t.teamID, YEAR(ra.createdate), MONTH(ra.createdate)) y;
SET @r1 = DATEDIFF(ms, @t0, SYSDATETIME());

/* ---- Q4, run 2 (warm) -------------------------------------------- */
SET @t0 = SYSDATETIME();
SELECT @n = COUNT(*) FROM (
  SELECT t.teamID, YEAR(ra.createdate) y, MONTH(ra.createdate) m
    FROM Jobs.Registration_Accounting ra
    JOIN Jobs.Registrations r ON ra.RegistrationID = r.registrationID
    JOIN Leagues.teams t ON ISNULL(ra.teamID, r.assigned_teamID) = t.teamID
    JOIN Jobs.Jobs j ON t.jobID = j.jobID
   WHERE j.CustomerId IN (SELECT CustomerId FROM @ids)
     AND j.expiryUsers >= @start AND t.active = 1
     AND ra.active = 1 AND ra.createdate IS NOT NULL AND ra.createdate < @endEx
   GROUP BY t.teamID, YEAR(ra.createdate), MONTH(ra.createdate)) y;
SET @r2 = DATEDIFF(ms, @t0, SYSDATETIME());

/* ---- Q4, run 3 (warm) -------------------------------------------- */
SET @t0 = SYSDATETIME();
SELECT @n = COUNT(*) FROM (
  SELECT t.teamID, YEAR(ra.createdate) y, MONTH(ra.createdate) m
    FROM Jobs.Registration_Accounting ra
    JOIN Jobs.Registrations r ON ra.RegistrationID = r.registrationID
    JOIN Leagues.teams t ON ISNULL(ra.teamID, r.assigned_teamID) = t.teamID
    JOIN Jobs.Jobs j ON t.jobID = j.jobID
   WHERE j.CustomerId IN (SELECT CustomerId FROM @ids)
     AND j.expiryUsers >= @start AND t.active = 1
     AND ra.active = 1 AND ra.createdate IS NOT NULL AND ra.createdate < @endEx
   GROUP BY t.teamID, YEAR(ra.createdate), MONTH(ra.createdate)) y;
SET @r3 = DATEDIFF(ms, @t0, SYSDATETIME());

SET STATISTICS TIME OFF;
SET STATISTICS IO OFF;

SELECT Run1_cold_ms = @r1, Run2_warm_ms = @r2, Run3_warm_ms = @r3,
       Rows = @n, DEV_ms = 280;

/* ---- What the server actually did, from the plan cache ------------ */
SELECT TOP 5
       Executions      = qs.execution_count,
       AvgCPU_ms       = qs.total_worker_time  / 1000 / qs.execution_count,
       AvgElapsed_ms   = qs.total_elapsed_time / 1000 / qs.execution_count,
       AvgLogicalReads = qs.total_logical_reads / qs.execution_count,
       AvgPhysReads    = qs.total_physical_reads / qs.execution_count,
       /* CPU divided by elapsed. >1 means the plan went PARALLEL and this
          is roughly the degree it achieved; ~1 means it ran on one thread. */
       CpuVsElapsed    = CASE WHEN qs.total_elapsed_time = 0 THEN 0.0
                              ELSE ROUND(1.0 * qs.total_worker_time
                                             / qs.total_elapsed_time, 1) END,
       QueryText       = SUBSTRING(st.text, (qs.statement_start_offset/2)+1,
                           ((CASE qs.statement_end_offset WHEN -1
                             THEN DATALENGTH(st.text) ELSE qs.statement_end_offset END
                             - qs.statement_start_offset)/2)+1)
FROM sys.dm_exec_query_stats qs
CROSS APPLY sys.dm_exec_sql_text(qs.sql_handle) st
WHERE st.text LIKE '%Registration_Accounting ra%'
  AND st.text LIKE '%ISNULL(ra.teamID%'
ORDER BY qs.last_execution_time DESC;

/* DEV BASELINE for the row whose QueryText starts "SELECT @n = COUNT(*)":
     AvgCPU_ms 1530 · AvgElapsed_ms 142 · AvgLogicalReads 33,402 ·
     AvgPhysReads 0 · CpuVsElapsed 10.8  (i.e. dev runs it on ~11 threads)

   Diff prod against that:
     Reads ~33K, CpuVsElapsed ~1.0  -> same plan, running SERIAL on prod.
     Reads far above 33K            -> DIFFERENT plan. That is the bug.
     AvgPhysReads high              -> reading from disk, not memory.       */
