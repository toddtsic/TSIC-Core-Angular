# Refund cap does not consult canonical `PaymentState`

**Filed:** 2026-05-29
**Severity:** Medium — bounded over-refund possible via repeated partial refunds against the same charge row
**Out of scope for:** Director check/correction gating work (2026-05-29)

## What's happening today

Both refund entry points use the same per-row cap:

- `RegistrationSearchService.ProcessRefundAsync` (line ~220)
- `TeamSearchService.ProcessRefundAsync` (line ~267)

```csharp
var original = await _accountingRepo.GetByAIdAsync(request.AccountingRecordId, ct);
// ...
var originalPay = original.Payamt ?? 0;
if (request.RefundAmount <= 0 || request.RefundAmount > originalPay)
    return ... "Refund amount must be between $0.01 and ${originalPay:F2}.";
```

Cap is the **original RA row's `Payamt`**. The flow never calls
`IPaymentStateService.ForRegistrationAsync` / `ForTeamAsync`, and never sums prior
refunds applied against the same row.

## Why it's loose

A refund creates a **new** CC-Credit accounting row — it does not mutate
`original.Payamt`. So subsequent refunds against the same charge row repeatedly
see the original full charge amount as the ceiling.

### Concrete over-refund scenario

| Step | Action | What the cap sees | Result |
|---|---|---|---|
| 1 | $100 CC charge → RA row A, `Payamt = $100` | — | — |
| 2 | Partial refund $30 → new CC-Credit row, A unchanged | $30 ≤ $100 ✓ | $70 actually remaining |
| 3 | Partial refund $80 → checks against row A | $80 ≤ $100 ✓ | **$10 over-refund** |

The system has now refunded $110 against a $100 charge.

## What the right answer looks like

A `PaymentState`-anchored cap, conceptually:

```
remaining_refundable(row A) = A.Payamt − sum(active refund rows that reference A)
```

This is exactly the kind of cross-row math `PaymentState` exists to centralize.
The check/correction gating shipped 2026-05-29 follows this pattern
(`state.ResolveOwed(...).Check`); the refund flow should too.

## Suggested approach when picked up

1. Add a repository method (or extend `IPaymentStateService`) that returns the
   refund-remaining for a given accounting record ID — summing prior refund rows
   that reference it (likely via `OrigAId` or a similar back-pointer; verify the
   schema first).
2. Replace the per-row `originalPay` cap in both `ProcessRefundAsync` methods
   with the resolver result.
3. Add a test for the partial-refund-then-refund-again scenario above.
4. FE (`accounting-ledger.component.ts:267`) — `refund` validation currently
   uses `refundRecord()?.paidAmount` from the row. After backend exposes
   remaining-refundable, propagate it to the FE so the modal shows the true
   remaining amount, not the original payment.

## Why this was deferred

Surfaced during the 2026-05-29 check/correction gating discussion. The user
chose to ship the check + correction work cleanly and file this separately
rather than expand scope. The check/correction principle
(`-PaidTotal ≤ correction ≤ OwedTotal`) does **not** depend on this fix; they
are independent gaps.
