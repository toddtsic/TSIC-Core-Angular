/* ============================================================================
   TEAM ACCOUNTING READOUT — reproduces the CLUB TEAMS BREAKDOWN grid row
   for ONE team.  READ-ONLY (SELECT only).

   "Balance Due" in that grid == RegisteredTeamDto.AdditionalDue
     (RegisteredTeamShaper.cs L142-147):
        fullPaymentPhase ? PaymentState.PrincipalRemaining(feeBase, discount, lateFee, 0)
                         : (resolvedDeposit > 0 ? resolvedBalanceDue : 0)

   Sources reproduced here:
     • fee cascade  team -> agegroup -> league, PER FIELD  (FeeRepository.GetResolvedFeesByTeamIdsAsync)
     • payment state from Jobs.Registration_Accounting, bucketed by paymentMethodID
       (PaymentMethodIds), CC/eCheck gross inverted at the job rate to principal
     • proc-free deposit slice walk for bAddProcessingFees=1 + bApplyProcessingFeesToTeamDeposit=0
       (PaymentStateService.BuildBatchAsync)
   ============================================================================ */

DECLARE @TeamId  uniqueidentifier = '00000000-0000-0000-0000-000000000000';  -- <<< PASTE teamID
DECLARE @ClubRep nvarchar(450)    = '6A26171F-4D94-4928-94FA-2FEFD42C3C3E';  -- RoleConstants.ClubRep

;WITH t AS (
    SELECT  tm.teamID, tm.teamName, tm.jobID, tm.agegroupID, ag.leagueID,
            ag.agegroupName,
            feeBase       = ISNULL(tm.fee_base, 0),
            discount      = ISNULL(tm.fee_discount, 0) + ISNULL(tm.fee_discount_mp, 0),  -- TotalDiscount(): BOTH buckets
            lateFee       = ISNULL(tm.fee_latefee, 0),
            donation      = ISNULL(tm.fee_donation, 0),
            feeProcessing = ISNULL(tm.fee_processing, 0),
            paidTotal     = ISNULL(tm.paid_total, 0),
            owedTotal     = ISNULL(tm.owed_total, 0),
            bAdd           = CONVERT(bit, ISNULL(j.bAddProcessingFees, 0)),
            applyToDeposit = CONVERT(bit, ISNULL(j.bApplyProcessingFeesToTeamDeposit, 0)),
            ccRate = CONVERT(decimal(19,6),                     -- clamped 3.5 .. 4.0 then /100
                        (CASE WHEN ISNULL(j.ProcessingFeePercent, 3.5) < 3.5 THEN 3.5
                              WHEN ISNULL(j.ProcessingFeePercent, 3.5) > 4.0 THEN 4.0
                              ELSE ISNULL(j.ProcessingFeePercent, 3.5) END) / 100.0),
            ecRate = CONVERT(decimal(19,6),                     -- clamped 1.5 .. 2.0 then /100
                        (CASE WHEN ISNULL(j.ECProcessingFeePercent, 1.5) < 1.5 THEN 1.5
                              WHEN ISNULL(j.ECProcessingFeePercent, 1.5) > 2.0 THEN 2.0
                              ELSE ISNULL(j.ECProcessingFeePercent, 1.5) END) / 100.0)
    FROM Leagues.teams tm
    JOIN Leagues.agegroups ag ON ag.agegroupID = tm.agegroupID
    JOIN Jobs.Jobs        j  ON j.jobID        = tm.jobID
    WHERE tm.teamID = @TeamId
),
/* fee cascade — most-specific NON-NULL wins PER FIELD (not per row) */
fee AS (
    SELECT t.*,
           resDeposit = COALESCE(tf.Deposit,    af.Deposit,    lf.Deposit),
           resBalance = COALESCE(tf.BalanceDue, af.BalanceDue, lf.BalanceDue),
           resFullPay = COALESCE(tf.bFullPaymentRequired, af.bFullPaymentRequired, lf.bFullPaymentRequired),
           feeSrc     = CASE WHEN tf.JobFeeId IS NOT NULL THEN 'team'
                             WHEN af.JobFeeId IS NOT NULL THEN 'agegroup'
                             WHEN lf.JobFeeId IS NOT NULL THEN 'league'
                             ELSE 'NOT CONFIGURED' END
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
ctx AS (
    SELECT fee.*,
           deposit     = ISNULL(fee.resDeposit, 0),                 -- shaper's `deposit`  (raw, NOT effective)
           balanceDue  = ISNULL(fee.resBalance, 0),                 -- shaper's `balanceDue`
           effDeposit  = COALESCE(fee.resDeposit, fee.resBalance, 0),-- ResolvedFee.EffectiveDeposit
           fullPayment = CONVERT(bit, ISNULL(fee.resFullPay, 0))    -- ResolveFullPaymentPhase
    FROM fee
),
pf AS (   -- proc-free (deposit) slice; 0 on every other job class
    SELECT ctx.*,
           procFreeBase = CASE WHEN ctx.bAdd = 1 AND ctx.applyToDeposit = 0
                                    AND ctx.effDeposit - ctx.discount + ctx.lateFee + ctx.donation > 0
                               THEN ctx.effDeposit - ctx.discount + ctx.lateFee + ctx.donation
                               ELSE 0 END
    FROM ctx
),
led AS (
    SELECT ra.aID, ra.createdate,
           amt = ISNULL(ra.payamt, 0),
           bucket = CASE
             WHEN ra.paymentMethodID IN ('30ECA575-A268-E111-9D56-F04DA202060D',   -- Credit Card Payment
                                         '5C46057C-69DE-4A22-B20F-D2BBDFE3A43A',   -- Credit Card Payment PIF
                                         '0CF0E4C2-5853-4A45-A7A5-A0D632BE8870',   -- Automated Recurrent Billing
                                         '31ECA575-A268-E111-9D56-F04DA202060D')   -- Credit Card Credit (negative)
                  THEN 'CC'
             WHEN ra.paymentMethodID IN ('2EECA575-A268-E111-9D56-F04DA202060D',   -- E-Check Payment
                                         '2FECA575-A268-E111-9D56-F04DA202060D')   -- Failed E-Check (NSF return, negative)
                  THEN 'EK'
             WHEN ra.paymentMethodID IN ('32ECA575-A268-E111-9D56-F04DA202060D',   -- Check By Client
                                         '37ECA575-A268-E111-9D56-F04DA202060D')   -- Check By TSIC
                  THEN 'CK'
             WHEN ra.paymentMethodID  = '2DECA575-A268-E111-9D56-F04DA202060D'     -- Cash By Client
                  THEN 'CASH'
             WHEN ra.paymentMethodID IN ('33ECA575-A268-E111-9D56-F04DA202060D',   -- Online Correction By Client
                                         '34ECA575-A268-E111-9D56-F04DA202060D',   -- Online Correction By TSIC
                                         '2CECA575-A268-E111-9D56-F04DA202060D')   -- Scholarship (legacy)
                  THEN 'CORR'
             END                                                  -- NULL = voids / failed CC / BALANCE DUE: excluded
    FROM Jobs.Registration_Accounting ra
    WHERE ra.teamID = @TeamId
      AND ra.active = 1
),
walk AS (   -- billing is deposit-first: oldest positive dollars fill the proc-free slice
    SELECT l.*,
           pos = CASE WHEN l.amt > 0 THEN l.amt ELSE 0 END,
           cumPosBefore = ISNULL(SUM(CASE WHEN l.amt > 0 THEN l.amt ELSE 0 END) OVER
                (ORDER BY l.createdate, l.aID ROWS BETWEEN UNBOUNDED PRECEDING AND 1 PRECEDING), 0)
    FROM led l
    WHERE l.bucket IS NOT NULL
),
taken AS (
    SELECT w.*,
           take = CASE WHEN pf.procFreeBase - w.cumPosBefore <= 0    THEN 0
                       WHEN pf.procFreeBase - w.cumPosBefore < w.pos THEN pf.procFreeBase - w.cumPosBefore
                       ELSE w.pos END
    FROM walk w CROSS JOIN pf
),
agg AS (
    SELECT ccGross   = ISNULL(SUM(CASE WHEN bucket = 'CC'   THEN amt  ELSE 0 END), 0),
           ekGross   = ISNULL(SUM(CASE WHEN bucket = 'EK'   THEN amt  ELSE 0 END), 0),
           ckPaid    = ISNULL(SUM(CASE WHEN bucket = 'CK'   THEN amt  ELSE 0 END), 0),
           cashPaid  = ISNULL(SUM(CASE WHEN bucket = 'CASH' THEN amt  ELSE 0 END), 0),
           corr      = ISNULL(SUM(CASE WHEN bucket = 'CORR' THEN amt  ELSE 0 END), 0),
           ccFreeRaw = ISNULL(SUM(CASE WHEN bucket = 'CC'   THEN take ELSE 0 END), 0),
           ekFreeRaw = ISNULL(SUM(CASE WHEN bucket = 'EK'   THEN take ELSE 0 END), 0)
    FROM taken
)
SELECT  pf.teamName,
        pf.agegroupName,
        /* ── the grid columns ───────────────────────────────────────────── */
        [Paid]        = agg.ccGross + agg.ekGross + agg.ckPaid + agg.cashPaid,   -- TenderPaid (corrections excluded)
        [DepositDue]  = due.depositDue,
        [BalanceDue]  = due.balanceDue,                                          -- <<< THE ANSWER
        [TotalFee]    = pf.deposit + pf.balanceDue,                              -- structural (Deposit + BalanceDue)
        [Owed]        = pf.owedTotal,                                            -- stored teams.owed_total
        [ProcFee]     = pf.feeProcessing,
        [FeeAdj]      = pf.lateFee - pf.discount - agg.corr,
        /* ── how it got there ───────────────────────────────────────────── */
        [Phase]           = CASE WHEN pf.fullPayment = 1 THEN 'FULL PAYMENT' ELSE 'deposit' END,
        [FeeRowSource]    = pf.feeSrc,
        [Resolved_Deposit]= pf.resDeposit,
        [Resolved_Balance]= pf.resBalance,
        [FeeBase]         = pf.feeBase,
        [Discount]        = pf.discount,
        [LateFee]         = pf.lateFee,
        [PrincipalPaid]   = pr.principalPaid,
        [ProcFreeBase]    = pf.procFreeBase,
        [CcGross]         = agg.ccGross,
        [EcheckGross]     = agg.ekGross,
        [CheckPaid]       = agg.ckPaid,
        [CashPaid]        = agg.cashPaid,
        [Correction]      = agg.corr,
        [PaidTotal_stored]= pf.paidTotal,
        pf.teamID, pf.jobID
FROM pf
CROSS JOIN agg
CROSS APPLY (SELECT ccFree = CASE WHEN agg.ccFreeRaw > CASE WHEN agg.ccGross > 0 THEN agg.ccGross ELSE 0 END
                                  THEN CASE WHEN agg.ccGross > 0 THEN agg.ccGross ELSE 0 END
                                  ELSE agg.ccFreeRaw END,
                    ekFree = CASE WHEN agg.ekFreeRaw > CASE WHEN agg.ekGross > 0 THEN agg.ekGross ELSE 0 END
                                  THEN CASE WHEN agg.ekGross > 0 THEN agg.ekGross ELSE 0 END
                                  ELSE agg.ekFreeRaw END) fr
CROSS APPLY (SELECT ccPrincipal = CASE WHEN pf.bAdd = 1 AND pf.ccRate > 0
                                       THEN fr.ccFree + ROUND((agg.ccGross - fr.ccFree) / (1 + pf.ccRate), 2)
                                       ELSE agg.ccGross END,
                    ekPrincipal = CASE WHEN pf.bAdd = 1 AND pf.ecRate > 0
                                       THEN fr.ekFree + ROUND((agg.ekGross - fr.ekFree) / (1 + pf.ecRate), 2)
                                       ELSE agg.ekGross END) pp
CROSS APPLY (SELECT principalPaid = pp.ccPrincipal + pp.ekPrincipal + agg.ckPaid + agg.cashPaid + agg.corr) pr
CROSS APPLY (SELECT depositNet = CASE WHEN pf.effDeposit - pf.discount + pf.lateFee > 0
                                      THEN pf.effDeposit - pf.discount + pf.lateFee ELSE 0 END,
                    fullNet    = pf.feeBase - pf.discount + pf.lateFee) nt   -- donation = 0 in the display path
CROSS APPLY (SELECT
        depositDue = CASE WHEN pf.fullPayment = 1 THEN 0                        -- deposit concept retired
                          WHEN nt.depositNet - pr.principalPaid > 0 THEN nt.depositNet - pr.principalPaid
                          ELSE 0 END,
        balanceDue = CASE WHEN pf.fullPayment = 1
                          THEN CASE WHEN nt.fullNet - pr.principalPaid > 0
                                    THEN nt.fullNet - pr.principalPaid ELSE 0 END  -- PrincipalRemaining
                          ELSE CASE WHEN pf.deposit > 0 THEN pf.balanceDue ELSE 0 END  -- forward-looking structural
                     END) due;


/* ---------------------------------------------------------------------------
   Optional: the ledger rows behind the number (oldest-first, same filter).
   --------------------------------------------------------------------------- */
SELECT ra.aID, ra.createdate, ra.payamt, ra.paymeth, ra.checkNo, ra.comment,
       ra.adnTransactionID, ra.active
FROM Jobs.Registration_Accounting ra
WHERE ra.teamID = @TeamId
ORDER BY ra.createdate, ra.aID;
