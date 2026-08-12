/* ============================================================================
   BLAST-RADIUS SWEEP — phantom Balance Due / Deposit Due from a COLLAPSED
   proc-free deposit slice.   READ-ONLY (SELECT only).

   Scope (WIDENED 2026-08-11): every team on ANY job with bAddProcessingFees = 1,
          both deposit classes. [JobClass] column distinguishes
          'no-fee-on-deposit' (applyToDeposit=0) from 'fees-on-everything' (=1).
          The proc-free slice only exists for the former, mirroring the app.

   The defect: PaymentStateService.ResolveProcFreeBasesAsync computes
       ProcFreeBase = MAX(0, deposit - discount + lateFee + donation)
   so a discount >= the deposit floors it to 0.  The slice-aware decomposition
   is then skipped and EVERY cc/eCheck dollar is inverted at the job rate —
   including the dollars that paid the proc-free deposit.  That invents
   principal-still-owed (gross - gross/(1+rate)) which shows in the grid's
   Balance Due / Deposit Due while the stored owed_total correctly reads 0.

   Result set 1 = affected teams (detail).
   Result set 2 = rollup by job.
   ============================================================================ */

DECLARE @ClubRep nvarchar(450) = '6A26171F-4D94-4928-94FA-2FEFD42C3C3E';  -- RoleConstants.ClubRep

IF OBJECT_ID('tempdb..#hits') IS NOT NULL DROP TABLE #hits;

;WITH t AS (
    SELECT  tm.teamID, tm.teamName, teamActive = tm.active,
            tm.jobID, j.jobName, j.jobPath,
            tm.agegroupID, ag.leagueID, ag.agegroupName,
            feeBase       = ISNULL(tm.fee_base, 0),
            discount      = ISNULL(tm.fee_discount, 0) + ISNULL(tm.fee_discount_mp, 0),
            lateFee       = ISNULL(tm.fee_latefee, 0),
            donation      = ISNULL(tm.fee_donation, 0),
            feeProcessing = ISNULL(tm.fee_processing, 0),
            feeTotal      = ISNULL(tm.fee_total, 0),
            paidTotal     = ISNULL(tm.paid_total, 0),
            owedTotal     = ISNULL(tm.owed_total, 0),
            applyToDeposit = CONVERT(bit, ISNULL(j.bApplyProcessingFeesToTeamDeposit, 0)),
            ccRate = CONVERT(decimal(19,6),
                        (CASE WHEN ISNULL(j.ProcessingFeePercent, 3.5) < 3.5 THEN 3.5
                              WHEN ISNULL(j.ProcessingFeePercent, 3.5) > 4.0 THEN 4.0
                              ELSE ISNULL(j.ProcessingFeePercent, 3.5) END) / 100.0),
            ecRate = CONVERT(decimal(19,6),
                        (CASE WHEN ISNULL(j.ECProcessingFeePercent, 1.5) < 1.5 THEN 1.5
                              WHEN ISNULL(j.ECProcessingFeePercent, 1.5) > 2.0 THEN 2.0
                              ELSE ISNULL(j.ECProcessingFeePercent, 1.5) END) / 100.0)
    FROM Leagues.teams     tm
    JOIN Jobs.Jobs         j  ON j.jobID        = tm.jobID
    JOIN Leagues.agegroups ag ON ag.agegroupID  = tm.agegroupID
    WHERE ISNULL(j.bAddProcessingFees, 0) = 1     -- ALL proc-fee jobs, both deposit classes
),
fee AS (
    SELECT t.*,
           resDeposit = COALESCE(tf.Deposit,    af.Deposit,    lf.Deposit),
           resBalance = COALESCE(tf.BalanceDue, af.BalanceDue, lf.BalanceDue),
           resFullPay = COALESCE(tf.bFullPaymentRequired, af.bFullPaymentRequired, lf.bFullPaymentRequired)
    FROM t
    OUTER APPLY (SELECT TOP (1) f.* FROM fees.JobFees f
                 WHERE f.JobId = t.jobID AND f.RoleId = @ClubRep AND f.TeamId = t.teamID) tf
    OUTER APPLY (SELECT TOP (1) f.* FROM fees.JobFees f
                 WHERE f.JobId = t.jobID AND f.RoleId = @ClubRep
                   AND f.TeamId IS NULL AND f.AgegroupId = t.agegroupID) af
    OUTER APPLY (SELECT TOP (1) f.* FROM fees.JobFees f
                 WHERE f.JobId = t.jobID AND f.RoleId = @ClubRep
                   AND f.TeamId IS NULL AND f.AgegroupId IS NULL AND f.LeagueId = t.leagueID) lf
),
pf AS (
    SELECT fee.*,
           deposit     = ISNULL(fee.resDeposit, 0),
           balanceDue  = ISNULL(fee.resBalance, 0),
           effDeposit  = COALESCE(fee.resDeposit, fee.resBalance, 0),
           fullPayment = CONVERT(bit, ISNULL(fee.resFullPay, 0)),
           /* proc-free slice exists ONLY on proc-on-balance-only jobs (applyToDeposit=0),
              mirroring PaymentStateService.BuildBatchAsync's gate */
           procFreeBase = CASE WHEN fee.applyToDeposit = 0
                                AND COALESCE(fee.resDeposit, fee.resBalance, 0)
                                    - fee.discount + fee.lateFee + fee.donation > 0
                               THEN COALESCE(fee.resDeposit, fee.resBalance, 0)
                                    - fee.discount + fee.lateFee + fee.donation
                               ELSE 0 END,
           /* what the slice WOULD be if the discount were not front-loaded onto it */
           procFreeBaseRaw = CASE WHEN fee.applyToDeposit = 0
                               THEN COALESCE(fee.resDeposit, fee.resBalance, 0) + fee.lateFee + fee.donation
                               ELSE 0 END
    FROM fee
),
led AS (
    SELECT ra.teamID, ra.aID, ra.createdate,
           amt = ISNULL(ra.payamt, 0),
           bucket = CASE
             WHEN ra.paymentMethodID IN ('30ECA575-A268-E111-9D56-F04DA202060D',
                                         '5C46057C-69DE-4A22-B20F-D2BBDFE3A43A',
                                         '0CF0E4C2-5853-4A45-A7A5-A0D632BE8870',
                                         '31ECA575-A268-E111-9D56-F04DA202060D') THEN 'CC'
             WHEN ra.paymentMethodID IN ('2EECA575-A268-E111-9D56-F04DA202060D',
                                         '2FECA575-A268-E111-9D56-F04DA202060D') THEN 'EK'
             WHEN ra.paymentMethodID IN ('32ECA575-A268-E111-9D56-F04DA202060D',
                                         '37ECA575-A268-E111-9D56-F04DA202060D') THEN 'CK'
             WHEN ra.paymentMethodID  = '2DECA575-A268-E111-9D56-F04DA202060D'    THEN 'CASH'
             WHEN ra.paymentMethodID IN ('33ECA575-A268-E111-9D56-F04DA202060D',
                                         '34ECA575-A268-E111-9D56-F04DA202060D',
                                         '2CECA575-A268-E111-9D56-F04DA202060D') THEN 'CORR'
             END
    FROM Jobs.Registration_Accounting ra
    WHERE ra.active = 1
      AND ra.teamID IS NOT NULL
      AND EXISTS (SELECT 1 FROM pf WHERE pf.teamID = ra.teamID)
),
walk AS (
    SELECT l.*,
           pos = CASE WHEN l.amt > 0 THEN l.amt ELSE 0 END,
           cumPosBefore = ISNULL(SUM(CASE WHEN l.amt > 0 THEN l.amt ELSE 0 END) OVER
                (PARTITION BY l.teamID ORDER BY l.createdate, l.aID
                 ROWS BETWEEN UNBOUNDED PRECEDING AND 1 PRECEDING), 0)
    FROM led l
    WHERE l.bucket IS NOT NULL
),
taken AS (   -- take under the CURRENT (collapsing) base, and under the raw base
    SELECT w.*,
           take    = CASE WHEN pf.procFreeBase    - w.cumPosBefore <= 0    THEN 0
                          WHEN pf.procFreeBase    - w.cumPosBefore < w.pos THEN pf.procFreeBase    - w.cumPosBefore
                          ELSE w.pos END,
           takeRaw = CASE WHEN pf.procFreeBaseRaw - w.cumPosBefore <= 0    THEN 0
                          WHEN pf.procFreeBaseRaw - w.cumPosBefore < w.pos THEN pf.procFreeBaseRaw - w.cumPosBefore
                          ELSE w.pos END
    FROM walk w
    JOIN pf ON pf.teamID = w.teamID
),
agg AS (
    SELECT teamID,
           ccGross      = SUM(CASE WHEN bucket = 'CC'   THEN amt     ELSE 0 END),
           ekGross      = SUM(CASE WHEN bucket = 'EK'   THEN amt     ELSE 0 END),
           ckPaid       = SUM(CASE WHEN bucket = 'CK'   THEN amt     ELSE 0 END),
           cashPaid     = SUM(CASE WHEN bucket = 'CASH' THEN amt     ELSE 0 END),
           corr         = SUM(CASE WHEN bucket = 'CORR' THEN amt     ELSE 0 END),
           ccFreeRaw    = SUM(CASE WHEN bucket = 'CC'   THEN take    ELSE 0 END),
           ekFreeRaw    = SUM(CASE WHEN bucket = 'EK'   THEN take    ELSE 0 END),
           ccFreeRawAlt = SUM(CASE WHEN bucket = 'CC'   THEN takeRaw ELSE 0 END),
           ekFreeRawAlt = SUM(CASE WHEN bucket = 'EK'   THEN takeRaw ELSE 0 END),
           rowCnt       = COUNT(*)
    FROM taken
    GROUP BY teamID
)
SELECT  pf.jobName, pf.jobPath, pf.teamName, pf.agegroupName, pf.teamActive,
        [JobClass]      = CASE WHEN pf.applyToDeposit = 1 THEN 'fees-on-everything' ELSE 'no-fee-on-deposit' END,
        [Phase]         = CASE WHEN pf.fullPayment = 1 THEN 'FULL PAYMENT' ELSE 'deposit' END,
        [Deposit]       = pf.deposit,
        [Balance]       = pf.balanceDue,
        [Discount]      = pf.discount,
        [SliceCollapsed]= CASE WHEN pf.procFreeBase = 0 AND pf.procFreeBaseRaw > 0 THEN 1 ELSE 0 END,
        [ProcFreeBase]  = pf.procFreeBase,
        [ProcFreeBaseRaw] = pf.procFreeBaseRaw,
        [Owed_stored]   = pf.owedTotal,
        [ProcFee_stored]= pf.feeProcessing,
        [PaidTotal]     = pf.paidTotal,
        [PrincipalPaid_now] = pr.principalPaid,
        [PrincipalPaid_ifNotCollapsed] = pr.principalPaidAlt,
        [DepositDue_shown] = due.depositDue,
        [BalanceDue_shown] = due.balanceDue,
        [Phantom]       = due.depositDue + due.balanceDue,          -- what the grid shows over and above owed
        /* ── does the ENTITY still reconcile with the LEDGER + FeeMath? ──────────
           Anything non-zero here means the columns were written by something other
           than the recalc path (hand edit / partial update), so this row is NOT
           evidence of the proc-free-slice collapse — it's a stale-column row. */
        [d_PaidVsLedger] = pf.paidTotal - (g.ccGross + g.ekGross + g.ckPaid + g.cashPaid + g.corr),
        [d_FeeTotal]     = pf.feeTotal  - (pf.feeBase + pf.feeProcessing - pf.discount + pf.donation + pf.lateFee),
        [d_Owed]         = pf.owedTotal - (pf.feeTotal - pf.paidTotal),
        [Reconciles]     = CASE WHEN pf.paidTotal = (g.ccGross + g.ekGross + g.ckPaid + g.cashPaid + g.corr)
                                 AND pf.feeTotal  = (pf.feeBase + pf.feeProcessing - pf.discount + pf.donation + pf.lateFee)
                                 AND pf.owedTotal = (pf.feeTotal - pf.paidTotal)
                                THEN 1 ELSE 0 END,
        [FeeTotal_stored] = pf.feeTotal,
        [CcGross] = g.ccGross, [EcheckGross] = g.ekGross,
        [CheckPaid] = g.ckPaid, [CashPaid] = g.cashPaid, [Correction] = g.corr,
        pf.teamID, pf.jobID
INTO #hits
FROM pf
LEFT JOIN agg ON agg.teamID = pf.teamID
CROSS APPLY (SELECT ccGross = ISNULL(agg.ccGross,0), ekGross = ISNULL(agg.ekGross,0),
                    ckPaid  = ISNULL(agg.ckPaid,0),  cashPaid = ISNULL(agg.cashPaid,0),
                    corr    = ISNULL(agg.corr,0),
                    ccFreeRaw = ISNULL(agg.ccFreeRaw,0), ekFreeRaw = ISNULL(agg.ekFreeRaw,0),
                    ccFreeRawAlt = ISNULL(agg.ccFreeRawAlt,0), ekFreeRawAlt = ISNULL(agg.ekFreeRawAlt,0)) g
CROSS APPLY (SELECT ccFree    = CASE WHEN g.ccFreeRaw    > CASE WHEN g.ccGross > 0 THEN g.ccGross ELSE 0 END
                                     THEN CASE WHEN g.ccGross > 0 THEN g.ccGross ELSE 0 END ELSE g.ccFreeRaw END,
                    ekFree    = CASE WHEN g.ekFreeRaw    > CASE WHEN g.ekGross > 0 THEN g.ekGross ELSE 0 END
                                     THEN CASE WHEN g.ekGross > 0 THEN g.ekGross ELSE 0 END ELSE g.ekFreeRaw END,
                    ccFreeAlt = CASE WHEN g.ccFreeRawAlt > CASE WHEN g.ccGross > 0 THEN g.ccGross ELSE 0 END
                                     THEN CASE WHEN g.ccGross > 0 THEN g.ccGross ELSE 0 END ELSE g.ccFreeRawAlt END,
                    ekFreeAlt = CASE WHEN g.ekFreeRawAlt > CASE WHEN g.ekGross > 0 THEN g.ekGross ELSE 0 END
                                     THEN CASE WHEN g.ekGross > 0 THEN g.ekGross ELSE 0 END ELSE g.ekFreeRawAlt END) fr
CROSS APPLY (SELECT principalPaid    = fr.ccFree    + ROUND((g.ccGross - fr.ccFree)    / (1 + pf.ccRate), 2)
                                     + fr.ekFree    + ROUND((g.ekGross - fr.ekFree)    / (1 + pf.ecRate), 2)
                                     + g.ckPaid + g.cashPaid + g.corr,
                    principalPaidAlt = fr.ccFreeAlt + ROUND((g.ccGross - fr.ccFreeAlt) / (1 + pf.ccRate), 2)
                                     + fr.ekFreeAlt + ROUND((g.ekGross - fr.ekFreeAlt) / (1 + pf.ecRate), 2)
                                     + g.ckPaid + g.cashPaid + g.corr) pr
CROSS APPLY (SELECT depositNet = CASE WHEN pf.effDeposit - pf.discount + pf.lateFee > 0
                                      THEN pf.effDeposit - pf.discount + pf.lateFee ELSE 0 END,
                    fullNet    = pf.feeBase - pf.discount + pf.lateFee) nt
CROSS APPLY (SELECT
        depositDue = CASE WHEN pf.fullPayment = 1 THEN 0
                          WHEN nt.depositNet - pr.principalPaid > 0 THEN nt.depositNet - pr.principalPaid
                          ELSE 0 END,
        balanceDue = CASE WHEN pf.fullPayment = 1
                          THEN CASE WHEN nt.fullNet - pr.principalPaid > 0
                                    THEN nt.fullNet - pr.principalPaid ELSE 0 END
                          ELSE CASE WHEN pf.deposit > 0 THEN pf.balanceDue ELSE 0 END END) due
WHERE pf.owedTotal <= 0                       -- stored ledger says settled ...
  AND (due.depositDue > 0 OR (pf.fullPayment = 1 AND due.balanceDue > 0))   -- ... but the grid shows money due
  AND (g.ccGross <> 0 OR g.ekGross <> 0);     -- only cc/eCheck money can produce the inversion residue

/* ── 1) REAL code-path victims: entity still reconciles with the ledger, yet the
        grid shows money due.  These are the ones the slice collapse explains. ── */
SELECT * FROM #hits WHERE Reconciles = 1 ORDER BY [Phantom] DESC;

/* ── 2) stale/hand-edited columns: entity no longer agrees with ledger+FeeMath.
        NOT evidence of the code defect — triage separately. ──────────────────── */
SELECT * FROM #hits WHERE Reconciles = 0 ORDER BY [Phantom] DESC;

/* ── 3) rollup by job, split by which class ─────────────────────────────── */
SELECT  jobName, jobPath, JobClass,
        Teams            = COUNT(*),
        Reconciling      = SUM(CONVERT(int, Reconciles)),
        HandEdited       = SUM(1 - CONVERT(int, Reconciles)),
        SliceCollapsed   = SUM(CONVERT(int, SliceCollapsed)),
        PhantomTotal     = SUM([Phantom]),
        PhantomReconcile = SUM(CASE WHEN Reconciles = 1 THEN [Phantom] ELSE 0 END),
        PhantomMax       = MAX([Phantom])
FROM #hits
GROUP BY jobName, jobPath, JobClass
ORDER BY PhantomTotal DESC;

/* ── 4) THE DECISIVE ONE: raw ledger rows behind the top reconciling hits.
        Question being answered: is a cc payamt stored GROSS (proc embedded) or
        PRINCIPAL (flat)?  A gross row is an odd number that divides cleanly by
        1.035; a principal row is the round configured amount.  If payamt is the
        round number, the row carries NO proc and the resolver's inversion is
        inventing the residue. ─────────────────────────────────────────────── */
;WITH top10 AS (
    SELECT TOP (10) * FROM #hits WHERE Reconciles = 1 ORDER BY [Phantom] DESC
)
SELECT  h.jobPath, h.teamName, h.[Phantom], h.[Deposit], h.[Balance],
        h.[ProcFee_stored], h.[Owed_stored], h.[PaidTotal],
        ra.aID, ra.createdate,
        ra.payamt,
        [ImpliedPrincipal] = ROUND(ra.payamt / 1.035, 2),          -- if this row WERE gross
        [PayamtIsRound]    = CASE WHEN ra.payamt = ROUND(ra.payamt, 0) THEN 1 ELSE 0 END,
        [LooksGrossed]     = CASE WHEN ABS(ra.payamt - ROUND(ROUND(ra.payamt / 1.035, 2) * 1.035, 2)) < 0.005
                                   AND ra.payamt <> ROUND(ra.payamt, 0)
                                  THEN 1 ELSE 0 END,
        ra.paymeth, ra.checkNo, ra.comment, ra.adnTransactionID, ra.paymentMethodID
FROM top10 h
JOIN Jobs.Registration_Accounting ra
      ON ra.teamID = h.teamID AND ra.active = 1
ORDER BY h.[Phantom] DESC, ra.createdate, ra.aID;

DROP TABLE #hits;
