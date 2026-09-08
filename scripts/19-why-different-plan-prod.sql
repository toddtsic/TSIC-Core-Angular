/* =====================================================================
   PROD vs DEV — Q4 does 7,610,826 logical reads on prod and 33,402 on
   dev, on the SAME ROWS (dev is a 9/7 13:59 restore of prod). Zero
   physical reads both sides, both parallel. So it is the PLAN, and the
   plan comes from what the optimizer BELIEVES about the data.

   READ-ONLY. Run on TSIC-PHOENIX .SS2016, database TSICV5.
   Then run the same file on dev and diff.
   ===================================================================== */
SET NOCOUNT ON;

/* ---- 1. Optimizer model. A different CE version explains everything. */
SELECT DbName            = DB_NAME(),
       CompatLevel       = compatibility_level,   -- dev: see footer
       AutoCreateStats   = is_auto_create_stats_on,
       AutoUpdateStats   = is_auto_update_stats_on,
       AutoUpdateAsync   = is_auto_update_stats_async_on,
       ParamSniffing     = is_parameterization_forced,
       ReadCommittedSI   = is_read_committed_snapshot_on
FROM sys.databases WHERE database_id = DB_ID();

/* Database-scoped settings override the compat level and do NOT always
   travel with a backup the way people assume. LEGACY_CARDINALITY_ESTIMATION
   is the one that turns a hash join into nested loops. */
SELECT name, value, value_for_secondary
FROM sys.database_scoped_configurations
WHERE name IN ('LEGACY_CARDINALITY_ESTIMATION','PARAMETER_SNIFFING',
               'QUERY_OPTIMIZER_HOTFIXES','MAXDOP','CE_FEEDBACK',
               'OPTIMIZE_FOR_AD_HOC_WORKLOADS');

/* ---- 2. Statistics freshness, newest first. Dev and prod should MATCH
       (dev is a restore), so any divergence here is itself the finding. */
SELECT TOP 12
       TableName  = OBJECT_SCHEMA_NAME(s.object_id)+'.'+OBJECT_NAME(s.object_id),
       StatName   = s.name,
       LastUpdated= sp.last_updated,
       [Rows]     = sp.rows,
       SamplePct  = CASE WHEN sp.rows = 0 THEN 0
                         ELSE CAST(100.0 * sp.rows_sampled / sp.rows AS decimal(5,1)) END,
       ModsSince  = sp.modification_counter
FROM sys.stats s
CROSS APPLY sys.dm_db_stats_properties(s.object_id, s.stats_id) sp
WHERE s.object_id IN (OBJECT_ID('Jobs.Registration_Accounting'),
                      OBJECT_ID('Jobs.Registrations'),
                      OBJECT_ID('Leagues.teams'),
                      OBJECT_ID('Jobs.Jobs'))
  AND s.name NOT LIKE '_WA_Sys%'
ORDER BY sp.last_updated DESC;

/* ---- 3. Server knobs that change plan choice. --------------------- */
SELECT name, value_in_use FROM sys.configurations
WHERE name IN ('max degree of parallelism','cost threshold for parallelism',
               'max server memory (MB)','min server memory (MB)',
               'optimize for ad hoc workloads');

SELECT Edition        = CAST(SERVERPROPERTY('Edition') AS varchar(60)),
       ProductVersion = CAST(SERVERPROPERTY('ProductVersion') AS varchar(30)),
       CPUs           = (SELECT cpu_count FROM sys.dm_os_sys_info),
       PhysMemMB      = (SELECT physical_memory_kb/1024 FROM sys.dm_os_sys_info),
       Schedulers     = (SELECT COUNT(*) FROM sys.dm_os_schedulers
                         WHERE status='VISIBLE ONLINE' AND is_online=1);

/* Any global trace flags on? 9481 forces the legacy CE server-wide.
   Empty result = none, which is the normal case. */
BEGIN TRY DBCC TRACESTATUS(-1) WITH NO_INFOMSGS; END TRY BEGIN CATCH END CATCH;

/* =====================================================================
   DEV BASELINE (TSIC-SEDONA, measured 2026-09-08). Diff every line.

   CompatLevel 150 · AutoCreateStats 1 · AutoUpdateStats 1
   AutoUpdateAsync 0 · ForcedParam 0 · RCSI 1

   Scoped: MAXDOP 0 · LEGACY_CARDINALITY_ESTIMATION 0 · PARAMETER_SNIFFING 1
           QUERY_OPTIMIZER_HOTFIXES 0 · OPTIMIZE_FOR_AD_HOC_WORKLOADS 0

   Server: cost threshold for parallelism 5 · max degree of parallelism 12
           min server memory 16 · max server memory unlimited
           Developer Edition 15.0.2180.2 · 24 CPUs · 32,672 MB · 24 schedulers

   Stats, newest first:
     Registrations/IX_UserId            2026-09-04  669,325  100%
     Registrations/IX_AssignedTeamId    2026-09-02  668,911  100%
     Registrations/IX_Family_UserId     2026-08-30  667,679  100%
     Registrations/UI_Ai                2026-08-23  666,671    4.7%
     teams/PK                           2026-08-20   51,447  100%
     Registrations/PK                   2026-07-25  664,437  100%
     Registration_Accounting/PK         2026-05-08  276,626   10.1%  <-- stalest
     Jobs/PK                            2026-02-18    1,004  100%

   WHAT TO LOOK FOR, in order of how much it would explain:
     1. CompatLevel or LEGACY_CARDINALITY_ESTIMATION differ -> different CE
        model, different join strategy. Explains 228x reads outright.
     2. Stats dates NEWER on prod than the list above -> prod auto-updated
        after the 9/7 backup and landed on a WORSE sampled estimate. Note
        Registration_Accounting is already only a 10% sample here.
     3. CPUs / MAXDOP / cost threshold differ -> different cost model.
     4. A global trace flag on prod that is not on dev.
   ===================================================================== */
