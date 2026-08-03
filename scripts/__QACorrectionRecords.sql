/* ============================================================================
   __QACorrectionRecords.sql
   ----------------------------------------------------------------------------
   Purpose: identify CLUB REPS whose team balance was paid IN FULL in Legacy via
   a Correction Record that STRIPPED the processing fees, and who therefore now
   show as OWING those processing fees in the new system (the fee was correctly
   re-applied, but they already settled the balance without it).

   Canonical case: Heather Printz (hprintz), Top Threat Tournaments:Fall Draw 2026
     active team: Base $2000 / ProcFee $35 / Paid $2000 / Owed $35  <-- owes only the proc fee.

   Signature: an ACTIVE team where owed_total ~= fee_processing AND the base is
   paid in full (paid_total ~= fee_total - fee_processing) -- i.e. the ONLY thing
   outstanding is the processing fee. Aggregated per club rep per job.

   Key joins (verified 2026-08-02, dev TSICV5):
     Leagues.teams.clubrep_registrationid (guid) -> Jobs.Registrations.RegistrationID
     Jobs.Registrations.UserId (nvarchar)        -> dbo.AspNetUsers.Id  (name/UserName)
     (registrationFormName is NULL for these reps -- get the name from AspNetUsers.)

   Scope: defaults to ACTIVE jobs only (ExpiryUsers >= today) -- the go-live set.
     - For ALL years, comment out the ExpiryUsers line in the CTE.
     - At GO-LIVE, run against the PROD DB; optionally restrict to the migrating
       JobIds (AND j.JobId IN (...)) or a season filter (AND j.JobName LIKE '%2026%').

   ** REFINEMENT before writing anything off **
   The signature is the fee-state OUTCOME, not proof the payment was a Correction.
     - A CHECK payment carries no processing fee by design; a pure check payer owes
       $0 (not owed=proc) so should not appear -- but payments are per-registration,
       not per-team, so confirm the affected TEAM's paying method / that a Correction
       record exists (see Query C) before zeroing anything.
   Payment method IDs (reference.Accounting_PaymentMethods):
     30ECA575-... Credit Card Payment | 32ECA575-... Check Payment By Client
     33ECA575-... Online Correction By Client | 34ECA575-... Online Correction By TSIC
     2EECA575-... E-Check Payment
   ============================================================================ */

SET NOCOUNT ON;
DECLARE @today date = CAST(GETDATE() AS date);


/* ---------------------------------------------------------------------------
   QUERY A -- CLUB REP SUMMARY (one row per affected club rep per job)
   --------------------------------------------------------------------------- */
;WITH ts AS (
    SELECT c.customerName, j.JobName, j.JobId, u.Id AS repId,
           u.FirstName + ' ' + u.LastName AS ClubRep, u.UserName,
           t.fee_processing AS proc_, t.owed_total AS owed_
    FROM Leagues.teams t
    JOIN Jobs.Jobs j          ON j.JobId = t.jobID
    JOIN Jobs.Customers c     ON c.customerID = j.CustomerId
    JOIN Jobs.Registrations r ON r.RegistrationID = t.clubrep_registrationid
    JOIN dbo.AspNetUsers u    ON u.Id = r.UserId
    WHERE t.active = 1
      AND ISNULL(j.ExpiryUsers, '2099-01-01') >= @today   -- ACTIVE jobs only; comment out for all years
), affected AS (
    SELECT JobId, repId
    FROM ts
    GROUP BY JobId, repId
    HAVING SUM(CASE WHEN owed_ > 0 THEN owed_ ELSE 0 END) > 0.01           -- owes something
       AND SUM(CASE WHEN owed_ > 0 THEN owed_ - proc_ ELSE 0 END) < 0.50   -- but nothing beyond proc fees (base paid in full)
       AND SUM(proc_) > 0
)
SELECT ts.customerName AS Customer, ts.JobName AS Job, ts.ClubRep, ts.UserName,
       SUM(CASE WHEN ts.owed_ > 0.01 THEN 1 ELSE 0 END)                    AS AffectedTeams,
       CAST(SUM(CASE WHEN ts.owed_ > 0.01 THEN ts.owed_ ELSE 0 END) AS decimal(9,2)) AS TotalProcFeeOwed
FROM ts
JOIN affected a ON a.JobId = ts.JobId AND a.repId = ts.repId
GROUP BY ts.customerName, ts.JobName, ts.ClubRep, ts.UserName
ORDER BY TotalProcFeeOwed DESC, ts.ClubRep;


/* ---------------------------------------------------------------------------
   QUERY B -- TEAM DETAIL (one row per affected team)
   --------------------------------------------------------------------------- */
;WITH ts AS (
    SELECT c.customerName, j.JobName, j.JobId, u.Id AS repId,
           u.FirstName + ' ' + u.LastName AS ClubRep, u.UserName,
           t.teamName, t.fee_processing AS proc_, t.owed_total AS owed_
    FROM Leagues.teams t
    JOIN Jobs.Jobs j          ON j.JobId = t.jobID
    JOIN Jobs.Customers c     ON c.customerID = j.CustomerId
    JOIN Jobs.Registrations r ON r.RegistrationID = t.clubrep_registrationid
    JOIN dbo.AspNetUsers u    ON u.Id = r.UserId
    WHERE t.active = 1
      AND ISNULL(j.ExpiryUsers, '2099-01-01') >= @today   -- ACTIVE jobs only; comment out for all years
), affected AS (
    SELECT JobId, repId
    FROM ts
    GROUP BY JobId, repId
    HAVING SUM(CASE WHEN owed_ > 0 THEN owed_ ELSE 0 END) > 0.01
       AND SUM(CASE WHEN owed_ > 0 THEN owed_ - proc_ ELSE 0 END) < 0.50
       AND SUM(proc_) > 0
)
SELECT ts.customerName AS Customer, ts.JobName AS Job, ts.ClubRep, ts.UserName,
       ts.teamName AS Team, CAST(ts.owed_ AS decimal(9,2)) AS ProcFeeOwed
FROM ts
JOIN affected a ON a.JobId = ts.JobId AND a.repId = ts.repId
WHERE ts.owed_ > 0.01
ORDER BY ts.customerName, ts.JobName, ts.ClubRep, ts.teamName;


/* ---------------------------------------------------------------------------
   QUERY C -- REFINEMENT: payment methods used by the affected reps
   (sanity-check for check-payers / that a Correction record exists).
   NOTE: payments are per club-rep REGISTRATION, not per team -- a rep may mix
   methods across teams, so this is context, not per-team proof.
   --------------------------------------------------------------------------- */
;WITH ts AS (
    SELECT t.clubrep_registrationid AS regid, t.fee_processing AS proc_, t.owed_total AS owed_,
           j.ExpiryUsers
    FROM Leagues.teams t
    JOIN Jobs.Jobs j ON j.JobId = t.jobID
    WHERE t.active = 1
      AND ISNULL(j.ExpiryUsers, '2099-01-01') >= @today   -- comment out for all years
), affectedRegs AS (
    SELECT DISTINCT regid
    FROM ts
    WHERE owed_ > 0.01 AND ABS(owed_ - proc_) < 0.50 AND proc_ > 0
)
SELECT pm.paymentMethod AS Method,
       COUNT(*)                          AS AcctRows,
       COUNT(DISTINCT ra.RegistrationID) AS Regs
FROM affectedRegs a
JOIN Jobs.Registration_Accounting ra          ON ra.RegistrationID = a.regid
JOIN reference.Accounting_PaymentMethods pm   ON pm.paymentMethodID = ra.paymentMethodID
GROUP BY pm.paymentMethod
ORDER BY AcctRows DESC;
