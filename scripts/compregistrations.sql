/* ============================================================================
   COMP REGISTRATIONS  —  zero out the balance on an existing family's regs
   ----------------------------------------------------------------------------
   Use when a coach registered a whole team online under ONE family account and
   they all show as OWING, but the team is comped / paid offline.

   For every registration under @AcctUser on @JobId, sets:
       fee_discount   = fee_base   (comp the base price)
       fee_processing = 0
       fee_total      = 0
       owed_total     = 0
   => nets to $0 owed.

   NOTE: NO re-run guard (by request) — it comps ALL matching rows every run.
   Running it a second time is harmless here (the values are absolute, not
   derived from owed_total), but if anyone has actually PAID, see the caveat
   below before re-running.

   This script COMMITS in a single transaction (all-or-nothing via XACT_ABORT)
   and prints before/after counts.

   RUN (local or prod — both are the .\SS2016 named instance, db TSICV5):
       sqlcmd -S "lpc:.\SS2016" -d TSICV5 -I -b -i "scripts\compregistrations.sql"
   ============================================================================ */
SET NOCOUNT ON;
SET XACT_ABORT ON;

/* ========================= PARAMETERS — EDIT THESE ========================= */
DECLARE @JobId    uniqueidentifier = '20ED45C5-7872-4F4B-9417-498C444B4C77';  -- target job
DECLARE @AcctUser nvarchar(256)    = 'auselitelacrosse';                       -- family account UserName
/* =========================================================================== */

DECLARE @Now      datetime2 = SYSDATETIME();
DECLARE @FamilyId nvarchar(450) = (SELECT Id FROM dbo.AspNetUsers WHERE UserName = @AcctUser);

IF @FamilyId IS NULL
    BEGIN RAISERROR('Account UserName not found.', 16, 1); RETURN; END;
IF NOT EXISTS (SELECT 1 FROM Jobs.Jobs WHERE JobId = @JobId)
    BEGIN RAISERROR('JobId not found.', 16, 1); RETURN; END;

PRINT '--- BEFORE ---';
SELECT COUNT(*) AS regs, SUM(owed_total) AS sum_owed, SUM(paid_total) AS sum_paid,
       SUM(fee_processing) AS sum_proc
FROM Jobs.Registrations WHERE Family_UserId = @FamilyId AND jobID = @JobId;

BEGIN TRAN;

UPDATE Jobs.Registrations
SET fee_discount   = fee_base,     -- comp the base; proc is zeroed separately
    fee_processing = 0,
    fee_total      = 0,
    owed_total     = 0,
    modified       = @Now
WHERE Family_UserId = @FamilyId AND jobID = @JobId;
PRINT CONCAT('Registrations comped: ', @@ROWCOUNT);

PRINT '--- AFTER ---';
SELECT fee_base, fee_discount, fee_processing, fee_total, owed_total, paid_total, COUNT(*) AS cnt
FROM Jobs.Registrations WHERE Family_UserId = @FamilyId AND jobID = @JobId
GROUP BY fee_base, fee_discount, fee_processing, fee_total, owed_total, paid_total;

COMMIT;
PRINT '*** COMMITTED ***';

/* ----------------------------------------------------------------------------
   CAVEAT — re-run safety: this sets values absolutely (fee_discount = fee_base),
   so re-running keeps owed at 0. BUT it also forces paid_total's counterpart
   fields to 0 regardless of payments. For auselitelacrosse all paid_total = 0,
   so it's clean. If you point this at an account where someone HAS paid, add:
       AND owed_total > 0
   to the UPDATE's WHERE so settled/paid rows are left alone.
   ---------------------------------------------------------------------------- */
