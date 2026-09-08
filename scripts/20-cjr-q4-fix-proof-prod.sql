/* =====================================================================
   THE FIX, PROVEN SIDE BY SIDE. READ-ONLY.
   Run on TSIC-PHOENIX .SS2016, database TSICV5. Takes ~20 seconds.

   OLD joins on ISNULL(ra.teamID, r.assigned_teamID) = t.teamID, which no
   index can serve, so it is fast only if the optimizer guesses a hash
   join. Prod guesses wrong: 15,608ms / 7.6M reads vs dev's 142ms / 33K.

   NEW splits it into the two disjoint cases it always was -- ledger rows
   that carry a teamID, and ledger rows that route through the
   registration -- so both joins are plain indexed equality and there is
   no guess left to get wrong.

   Dev: OLD 280ms, NEW 217ms, identical 578 rows.
   ===================================================================== */
SET NOCOUNT ON;

/* American Select Lacrosse. Inlined rather than a table variable so the
   optimizer sees the same thing EF gives it. */
DECLARE @cust  uniqueidentifier = '76586DE3-ACB3-42EE-91EB-48597CA06802';
DECLARE @start datetime = '2026-08-01';
DECLARE @endEx datetime = DATEADD(day, 1, CAST(GETDATE() AS date));
DECLARE @t0 datetime2, @old int, @newA int, @newB int, @nOld int, @nA int, @nB int;

/* ---- OLD: the coalesce join ---------------------------------------- */
SET @t0 = SYSDATETIME();
SELECT @nOld = COUNT(*) FROM (
  SELECT t.teamID, YEAR(ra.createdate) y, MONTH(ra.createdate) m
    FROM Jobs.Registration_Accounting ra
    JOIN Jobs.Registrations r ON ra.RegistrationID = r.registrationID
    JOIN Leagues.teams t ON ISNULL(ra.teamID, r.assigned_teamID) = t.teamID
    JOIN Jobs.Jobs j ON t.jobID = j.jobID
   WHERE j.CustomerId = @cust AND j.expiryUsers >= @start AND t.active = 1
     AND ra.active = 1 AND ra.createdate IS NOT NULL AND ra.createdate < @endEx
   GROUP BY t.teamID, YEAR(ra.createdate), MONTH(ra.createdate)) x;
SET @old = DATEDIFF(ms, @t0, SYSDATETIME());

/* ---- NEW part A: ledger rows that CARRY a teamID.
        No Registrations join at all -- seeks straight to the team. ---- */
SET @t0 = SYSDATETIME();
SELECT @nA = COUNT(*) FROM (
  SELECT ra.teamID, YEAR(ra.createdate) y, MONTH(ra.createdate) m
    FROM Jobs.Registration_Accounting ra
    JOIN Leagues.teams t ON ra.teamID = t.teamID
    JOIN Jobs.Jobs j ON t.jobID = j.jobID
   WHERE j.CustomerId = @cust AND j.expiryUsers >= @start AND t.active = 1
     AND ra.active = 1 AND ra.teamID IS NOT NULL
     AND ra.createdate IS NOT NULL AND ra.createdate < @endEx
   GROUP BY ra.teamID, YEAR(ra.createdate), MONTH(ra.createdate)) x;
SET @newA = DATEDIFF(ms, @t0, SYSDATETIME());

/* ---- NEW part B: ledger rows with NO teamID, routed through the
        registration. Plain equality on assigned_teamID. -------------- */
SET @t0 = SYSDATETIME();
SELECT @nB = COUNT(*) FROM (
  SELECT r.assigned_teamID AS teamID, YEAR(ra.createdate) y, MONTH(ra.createdate) m
    FROM Jobs.Registration_Accounting ra
    JOIN Jobs.Registrations r ON ra.RegistrationID = r.registrationID
    JOIN Leagues.teams t ON r.assigned_teamID = t.teamID
    JOIN Jobs.Jobs j ON t.jobID = j.jobID
   WHERE j.CustomerId = @cust AND j.expiryUsers >= @start AND t.active = 1
     AND ra.active = 1 AND ra.teamID IS NULL
     AND ra.createdate IS NOT NULL AND ra.createdate < @endEx
   GROUP BY r.assigned_teamID, YEAR(ra.createdate), MONTH(ra.createdate)) x;
SET @newB = DATEDIFF(ms, @t0, SYSDATETIME());

SELECT OLD_ms      = @old,
       NEW_ms      = @newA + @newB,
       Speedup     = CASE WHEN @newA + @newB = 0 THEN NULL
                          ELSE @old / NULLIF(@newA + @newB, 0) END,
       OLD_rows    = @nOld,
       NEW_rows    = @nA + @nB,
       RowsMatch   = CASE WHEN @nOld = @nA + @nB THEN 'YES' ELSE '*** NO ***' END,
       DEV_OLD_ms  = 280,
       DEV_NEW_ms  = 217;
