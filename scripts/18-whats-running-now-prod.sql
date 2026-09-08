/* =====================================================================
   PROD LIVE PROBE — run in a SECOND SSMS window on TSIC-PHOENIX WHILE a
   slow query is still executing. Tells you what it is WAITING on.
   READ-ONLY. Only meaningful while the slow statement is in flight.

   READING IT:
     blocking_session_id <> 0            -> blocked by another session.
                                            Not a query problem.
     wait_type CXPACKET / CXCONSUMER     -> parallelism skew.
     wait_type PAGEIOLATCH_*             -> reading from disk. Storage.
     wait_type NULL, cpu_time ~ elapsed  -> burning CPU = bad plan.
                                            Dev completes at 33,402 reads.
   ===================================================================== */
SELECT r.session_id, r.status, r.wait_type, r.last_wait_type,
       r.wait_time, r.blocking_session_id,
       CPU_ms = r.cpu_time, Elapsed_ms = r.total_elapsed_time,
       r.logical_reads, r.reads, r.granted_query_memory,
       DOP = r.dop, r.percent_complete,
       SUBSTRING(t.text, (r.statement_start_offset/2)+1,
         ((CASE r.statement_end_offset WHEN -1 THEN DATALENGTH(t.text)
           ELSE r.statement_end_offset END - r.statement_start_offset)/2)+1) AS RunningStatement
FROM sys.dm_exec_requests r
CROSS APPLY sys.dm_exec_sql_text(r.sql_handle) t
WHERE r.session_id <> @@SPID AND r.session_id > 50
ORDER BY r.total_elapsed_time DESC;

/* What its parallel threads are blocked on, if anything */
SELECT wt.session_id, wt.wait_type, wt.wait_duration_ms,
       wt.blocking_session_id, wt.resource_description
FROM sys.dm_os_waiting_tasks wt
WHERE wt.session_id > 50 AND wt.session_id <> @@SPID
ORDER BY wt.wait_duration_ms DESC;

/* Server-level config that differs between boxes and changes plan choice.
   Dev runs this query on ~11 threads; if prod's MAXDOP is 1 it cannot. */
SELECT name, value_in_use FROM sys.configurations
WHERE name IN ('max degree of parallelism','cost threshold for parallelism',
               'max server memory (MB)','min server memory (MB)');
SELECT Edition = SERVERPROPERTY('Edition'),
       CPUs = (SELECT cpu_count FROM sys.dm_os_sys_info),
       SchedulersOnline = (SELECT COUNT(*) FROM sys.dm_os_schedulers
                           WHERE status='VISIBLE ONLINE' AND is_online=1);
