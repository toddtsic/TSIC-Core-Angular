# Accounting - Punch List

**Tester:** Ann
**Date Started:** 2026-04-06
**Status:** In Progress

---

## How to Read Severity

| Label | Meaning |
|-------|---------|
| Bug | Something is broken or produces wrong results |
| UX | It works but is confusing, ugly, or hard to use |
| Question | Not sure if this is right -- need to ask Todd |

## How to Read Status

| Label | Meaning |
|-------|---------|
| Open | Not yet looked at |
| Fixed | Todd/Claude fixed it |
| Won't Fix | Intentional behavior, not changing |

---

## Test Areas

Use these as a guide for what to walk through. You don't have to go in order.

- [ ] **Player Accounting** -- Fee calculations, balances, payment records for individual players
- [ ] **Team Accounting** -- Team-level fees, club rep balances, team financial summaries
- [ ] **Club Allocations** -- Club rep fee distribution and allocation tracking
- [ ] **Discount Codes** -- Applying, validating, and managing discount codes
- [ ] **Early Bird / Late Fees** -- Time-windowed fee modifiers and correct application
- [ ] **Refunds & Adjustments** -- Processing refunds, manual balance adjustments
- [ ] **Payment Processing** -- Credit card payments, receipts, transaction history

---

## Punch List Items

### PL-002: LLL Summer 2027 Team Registration Payment screen — Check Owed Total double-subtracts Deposit Processing Fees
- **Area**: Team Accounting / Payment Processing
- **Where**: LLL Summer 2027 → Team Registration → Payment screen
- **What I did**: Reviewed the accounting columns on the Team Registration Payment screen for LLL Summer 2027
- **What I expected**: Check Owed Total = Owed (base fee) − Paid (with deposit processing fees already accounted for in Paid)
- **What happened**: Check Owed Total subtracts Deposit Processing Fees a **second time**, even though those fees are already included in the Paid/Owed totals. The deposit processing fees are being deducted twice — once via the Paid column, and again in the Check Owed calc.
- **Severity**: Bug
- **Status**: Open
- **Note**: Likely in the Check Owed formula on the registered-teams-grid (or whatever computes the Check Owed column). Verify whether the Paid column on this screen already nets out deposit processing fees — if it does, Check Owed should subtract Paid only, not Paid + DepositProcFee.

### PL-001: Club Rep fees missing on recently-built tournament sites — needs backfill
- **Area**: Team Accounting
- **What I did**: Reviewed Club Rep fee configuration on recently-built tournament sites (e.g., LADT Lax by the Sea Summer 2027) and other recent sites
- **What I expected**: Club Rep fees populated on every tournament site
- **What happened**: Club Rep fees are missing on LADT Lax by the Sea Summer 2027 and other recently-built sites. Need to identify all affected tournaments and populate the fees. Todd is aware and wants this tracked so it doesn't get forgotten.
- **Severity**: Bug
- **Status**: Fixed

