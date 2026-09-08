/* =====================================================================
   PROD DIAGNOSTIC — Customer Job Revenue / "Teams-Players to Customer"
   returns in ~0.5s on dev, slowly on prod, with IDENTICAL data.

   READ-ONLY. Nothing here changes data or schema.
   Run on TSIC-PHOENIX .SS2016, database TSICV5.

   THE QUESTION THIS ANSWERS: is prod's time spent in SQL at all?
     - TOTAL_ms comes back ~500ms  -> SQL is fine; the time is in the API
                                      (EF materialization / serialization)
                                      or in IIS / the network. Stop looking
                                      at the database.
     - TOTAL_ms comes back seconds -> it IS SQL. Then compare CPU vs elapsed
                                      in the Messages tab: elapsed >> CPU
                                      means waiting on IO or locks, not work.

   Dev baseline (TSIC-SEDONA, restored 9/7 14:36 from the PHOENIX 13:59
   backup, so the same rows):
     Q1 54ms/420 rows · Q2 98ms/228 · Q3 133ms/579 · Q4 280ms/578 = 565ms

   Scope reproduced exactly: American Select Lacrosse, ALL JOBS, start
   8/1/2026, as-of today.
   ===================================================================== */
SET NOCOUNT ON;

/* ---- 0. Sanity: same index set as dev? (dev has all five) --------- */
SELECT i.name AS IndexOnRegistrations, i.type_desc
FROM sys.indexes i
WHERE i.object_id = OBJECT_ID('Jobs.Registrations') AND i.type > 0;

/* ---- 1. Time the four queries the endpoint actually runs ---------- */
DECLARE @cust  uniqueidentifier = '76586DE3-ACB3-42EE-91EB-48597CA06802'; -- American Select Lacrosse
DECLARE @start datetime = '2026-08-01';
DECLARE @endEx datetime = DATEADD(day, 1, CAST(GETDATE() AS date));       -- as-of = today
DECLARE @ids TABLE (CustomerId uniqueidentifier);
DECLARE @g int = (SELECT TOP 1 CustomerGroupId FROM Jobs.CustomerGroupCustomers WHERE CustomerId = @cust);
IF @g IS NULL INSERT @ids VALUES (@cust);
ELSE INSERT @ids SELECT CustomerId FROM Jobs.CustomerGroupCustomers WHERE CustomerGroupId = @g;

DECLARE @t0 datetime2, @q1 int, @q2 int, @q3 int, @q4 int;
DECLARE @n1 int, @n2 int, @n3 int, @n4 int;

SET STATISTICS TIME ON;   /* CPU vs elapsed per query -> Messages tab */

/* Q1 team identity + team charge */
SET @t0 = SYSDATETIME();
SELECT @n1 = COUNT(*) FROM Leagues.teams t
  JOIN Jobs.Jobs j ON t.jobID = j.jobID
  JOIN Leagues.agegroups a ON t.agegroupID = a.agegroupID
 WHERE j.CustomerId IN (SELECT CustomerId FROM @ids)
   AND j.expiryUsers >= @start AND t.active = 1;
SET @q1 = DATEDIFF(ms, @t0, SYSDATETIME());

/* Q2 roster headcount -- joins Registrations on assigned_teamID */
SET @t0 = SYSDATETIME();
SELECT @n2 = COUNT(*) FROM (
  SELECT t.teamID FROM Jobs.Registrations r
    JOIN Leagues.teams t ON r.assigned_teamID = t.teamID
    JOIN Jobs.Jobs j ON t.jobID = j.jobID
   WHERE j.CustomerId IN (SELECT CustomerId FROM @ids)
     AND j.expiryUsers >= @start AND t.active = 1 AND r.bActive = 1
     AND r.RoleId = 'DAC0C570-94AA-4A88-8D73-6034F1F72F3A'
   GROUP BY t.teamID) x;
SET @q2 = DATEDIFF(ms, @t0, SYSDATETIME());

/* Q3 player charges -- joins Registrations on assigned_teamID */
SET @t0 = SYSDATETIME();
SELECT @n3 = COUNT(*) FROM (
  SELECT t.teamID, YEAR(r.registrationTS) y, MONTH(r.registrationTS) m
    FROM Jobs.Registrations r
    JOIN Leagues.teams t ON r.assigned_teamID = t.teamID
    JOIN Jobs.Jobs j ON t.jobID = j.jobID
   WHERE j.CustomerId IN (SELECT CustomerId FROM @ids)
     AND j.expiryUsers >= @start AND t.active = 1
     AND (r.fee_total <> 0 OR r.fee_discount <> 0)
     AND r.registrationTS < @endEx
   GROUP BY t.teamID, YEAR(r.registrationTS), MONTH(r.registrationTS)) z;
SET @q3 = DATEDIFF(ms, @t0, SYSDATETIME());

/* Q4 payments -- the ISNULL(ra.teamID, r.assigned_teamID) join, the one
   that cannot seek and is the slowest half of the endpoint on dev */
SET @t0 = SYSDATETIME();
SELECT @n4 = COUNT(*) FROM (
  SELECT t.teamID, YEAR(ra.createdate) y, MONTH(ra.createdate) m
    FROM Jobs.Registration_Accounting ra
    JOIN Jobs.Registrations r ON ra.RegistrationID = r.registrationID
    JOIN Leagues.teams t ON ISNULL(ra.teamID, r.assigned_teamID) = t.teamID
    JOIN Jobs.Jobs j ON t.jobID = j.jobID
   WHERE j.CustomerId IN (SELECT CustomerId FROM @ids)
     AND j.expiryUsers >= @start AND t.active = 1
     AND ra.active = 1 AND ra.createdate IS NOT NULL AND ra.createdate < @endEx
   GROUP BY t.teamID, YEAR(ra.createdate), MONTH(ra.createdate)) y;
SET @q4 = DATEDIFF(ms, @t0, SYSDATETIME());

SET STATISTICS TIME OFF;

SELECT Q1_teams_ms = @q1, Q1_rows = @n1,
       Q2_roster_ms = @q2, Q2_rows = @n2,
       Q3_charges_ms = @q3, Q3_rows = @n3,
       Q4_payments_ms = @q4, Q4_rows = @n4,
       TOTAL_ms = @q1 + @q2 + @q3 + @q4,
       DEV_TOTAL_ms = 565;
