/* =====================================================================
   CJR Teams/Players — Q4, REWRITTEN. Run on TSIC-PHOENIX, DB TSICV5.
   READ-ONLY. Contains only the NEW query. Nothing slow runs here.
   Dev: 94ms, 578 rows.  (Old query on prod was 15,608ms.)
   ===================================================================== */
SET NOCOUNT ON;

DECLARE @cust  uniqueidentifier = '76586DE3-ACB3-42EE-91EB-48597CA06802'; -- American Select Lacrosse
DECLARE @start datetime = '2026-08-01';
DECLARE @endEx datetime = DATEADD(day, 1, CAST(GETDATE() AS date));
DECLARE @t0 datetime2, @a int, @b int, @nA int, @nB int;

/* Part A — ledger rows that CARRY a teamID. Seeks straight to the team;
   no Registrations join at all. */
SET @t0 = SYSDATETIME();
SELECT @nA = COUNT(*) FROM (
  SELECT ra.teamID, YEAR(ra.createdate) y, MONTH(ra.createdate) m
    FROM Jobs.Registration_Accounting ra
    JOIN Leagues.teams t ON ra.teamID = t.teamID
    JOIN Jobs.Jobs   j ON t.jobID = j.jobID
   WHERE j.CustomerId = @cust AND j.expiryUsers >= @start AND t.active = 1
     AND ra.active = 1 AND ra.teamID IS NOT NULL
     AND ra.createdate IS NOT NULL AND ra.createdate < @endEx
   GROUP BY ra.teamID, YEAR(ra.createdate), MONTH(ra.createdate)) x;
SET @a = DATEDIFF(ms, @t0, SYSDATETIME());

/* Part B — ledger rows with NO teamID, routed through the registration.
   Plain indexed equality on assigned_teamID. */
SET @t0 = SYSDATETIME();
SELECT @nB = COUNT(*) FROM (
  SELECT r.assigned_teamID AS teamID, YEAR(ra.createdate) y, MONTH(ra.createdate) m
    FROM Jobs.Registration_Accounting ra
    JOIN Jobs.Registrations r ON ra.RegistrationID = r.registrationID
    JOIN Leagues.teams     t ON r.assigned_teamID = t.teamID
    JOIN Jobs.Jobs         j ON t.jobID = j.jobID
   WHERE j.CustomerId = @cust AND j.expiryUsers >= @start AND t.active = 1
     AND ra.active = 1 AND ra.teamID IS NULL
     AND ra.createdate IS NOT NULL AND ra.createdate < @endEx
   GROUP BY r.assigned_teamID, YEAR(ra.createdate), MONTH(ra.createdate)) x;
SET @b = DATEDIFF(ms, @t0, SYSDATETIME());

SELECT PartA_ms = @a, PartB_ms = @b,
       NEW_TOTAL_ms = @a + @b,
       Rows = @nA + @nB,
       ExpectedRows = 578,
       DEV_NEW_ms = 94, OLD_PROD_ms = 15608;
