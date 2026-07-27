# Go-Live Risk Assessment — 5am ADN Sweep

*Reviewed 2026-07-27. Context: legacy 4am sweep (in-process in the legacy worker) retires at cutover; the new 5am sweep (`AdnSweepBackgroundService`, warmed by the 4:55 Windows task) becomes sole booker.*

## Bottom line: GO. Low risk, one scheduled verification, no blockers.

## Known (verified)

- **Current code is live and firing 7/7 mornings** at 05:00:01, 15–25s runtime, hundreds of real txs walked, zero errors (`echeck.SweepLog`). Scheduling chain, prod ADN fetch, and digest delivery are proven.
- **Dedup is proven daily against real data** — both stacks guard on the same key (`RegistrationAccounting.AdnTransactionId`), so the handoff is gap-free and double-book-free in both directions, including rollback.
- **The cutover procedure retires the 4am sweep** — `__GoLiveProcedure.md` Steps 1+5 (stop pool+site, Start Automatically=False). Proof it worked: the first post-cutover morning shows nonzero RecordsSettled/ArbImported.
- **Month-end close is tested end-to-end** (07-12, real prod data, zip diffed identical) via the same method the Aug 1 trigger calls. Cutover timing vs Aug 1 is convenience, not safety.
- **No legacy eCheck exposure** — no clients ever drafted by bank account, so the "return with no `echeck.Settlement` watcher → log-only skip" branch has an empty input set. (Optional hardening someday: promote that branch from log-only to a digest line.)

## Unproven (not wrong — never executed)

- **The booking branch** (RA insert, fee adjustment, watcher mint, status stamp) has zero prod executions — legacy always booked first. First real run = first legacy-less morning.
- Failure is **loud** (digest sends on failure with `— SWEEP FAILED` subject; `SweepLog.ErrorMessage`) and **recoverable** (manual `POST /api/admin/adn-sweep/run` re-runs safely under the dedup guard; 2-day trailing window self-heals a missed day). Worst case = one day's bookings arrive **late, not wrong** — ADN already charged either way; the sweep records, it doesn't charge.
- The un-alarmed residual is plausible-but-wrong rows. That is exactly what the verification below covers.

## The one action: first solo morning (~10 min, SSMS on prod)

**1. Sibling diff** — every row the new code books has a legacy-booked prior installment on the same registration; expect MATCH:

```sql
DECLARE @solo date = '2026-08-02';  -- first legacy-less morning

SELECT  n.AId              AS newRow,
        n.RegistrationId,
        n.Payamt           AS newPay,     p.Payamt          AS priorPay,
        n.PaymentMethodId  AS newMethod,  p.PaymentMethodId AS priorMethod,
        n.Comment          AS newComment, p.Comment         AS priorComment,
        CASE WHEN p.AId IS NULL THEN 'NO SIBLING (new sub?)'
             WHEN n.Payamt = p.Payamt
              AND n.PaymentMethodId = p.PaymentMethodId THEN 'MATCH'
             ELSE 'DIFF — REVIEW' END AS verdict
FROM    Jobs.Registration_Accounting n
OUTER APPLY (
        SELECT TOP 1 *
        FROM   Jobs.Registration_Accounting p
        WHERE  p.RegistrationId = n.RegistrationId
          AND  p.AdnTransactionId IS NOT NULL
          AND  p.Createdate < DATEADD(DAY, -20, @solo)
        ORDER BY p.Createdate DESC) p
WHERE   CAST(n.Createdate AS date) = @solo
  AND   n.AdnTransactionId IS NOT NULL;
```

DIFF is not automatically a bug (final installments, late-fee months) — but every DIFF gets eyeballed. NO SIBLING = first-ever draft post-cutover; verify one against the ADN portal.

**2. Watcher coverage** — every ACH row the new code books must have minted its return-watcher; expect zero rows:

```sql
SELECT n.AId, n.AdnTransactionId
FROM   Jobs.Registration_Accounting n
LEFT   JOIN echeck.Settlement s ON s.AdnTransactionId = n.AdnTransactionId
WHERE  CAST(n.Createdate AS date) = @solo
  AND  n.PaymentMethodId = '2EECA575-A268-E111-9D56-F04DA202060D'  -- E-Check Payment
  AND  s.AdnTransactionId IS NULL;
```

Repeat both the second morning (first run where the trailing window re-covers rows the new code itself booked = self-dedup proof). **Two clean mornings = fully proven system.**

## Risk ledger

| Risk | Verdict |
|------|---------|
| Go-live | **Low** — bounded to one day's bookings arriving late, with a tested re-run path |
| Delay | None technically — every day legacy runs first is another day the booking branch stays unproven |
| All-zeros morning after cutover (with ARB drafts due) | Not a quiet day — check the legacy app pool is actually stopped |
