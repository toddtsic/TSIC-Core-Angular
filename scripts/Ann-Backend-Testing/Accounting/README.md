# Backend Accounting Tests

These automated tests simulate real payment scenarios and verify that the system creates the correct accounting records, calculates fees properly, and distributes payments across teams correctly.

**No database or server needs to be running** — tests use an in-memory database.

## How to Run

In VS Code, navigate to `scripts/Ann-Backend-Testing/Accounting/` in the file explorer (left sidebar).

1. **Right-click** the test file you want to run (e.g., `Player-Accounting-Tests.ps1`)
2. Select **"Run Code"** from the menu
3. Results appear in the output panel at the bottom

### Available test files:
- `Player-Accounting-Tests.ps1` — Player-level payments (search/registrations)
- `Team-Accounting-Tests.ps1` — Team-level payments (search/teams, single team)
- `Club-Allocation-Tests.ps1` — Club payment distribution (search/teams, club scope)

---

## What Each Test Verifies

Every test checks **two things**:

1. **The accounting record created** — Was the right type of record made?
   - RegistrationId (record linked to the correct player or club rep registration)
   - Payment method (Check, Correction, Credit Card)
   - Amount recorded (Payamt, Dueamt)
   - Check number (if applicable)
   - TeamId (which team the record belongs to — team tests only)
   - Discount code link (DiscountCodeAi — null for manual corrections, set for discount codes)

2. **The financial state after** — Did the math come out right?
   - FeeProcessing (CC surcharge — reduced when paying by check or correction)
   - FeeTotal (recalculated after fee adjustment)
   - PaidTotal (how much has been paid)
   - OwedTotal (balance remaining)

---

## Player Accounting Tests (9 tests)

These test what happens when a director records a payment in **search/registrations** against a single player.

| Test | Record Type | What It Checks |
|------|-------------|---------------|
| **Check: $100 paid by check** | Check | Creates a Check record (PaymentMethodId=Check). Player balance goes to $0. |
| **Check: $100 removes $3.50 processing fee** | Check | CC processing fee ($3.50 at 3.5%) is removed because no credit card was used. FeeProcessing drops from $3.50 to $0. Balance = $0. |
| **Check: $50 partial** | Check | Partial check only removes proportional processing fee ($50 x 3.5% = $1.75 removed). Balance = $51.75. |
| **Correction: +$50 manual adjustment** | Correction | Creates a Correction record (NOT a discount code — DiscountCodeAi is null). Balance reduced by $50. |
| **Correction: +$50 with processing fees** | Correction | Corrections reduce processing fees the same way checks do — "non-CC payments don't incur CC fees." FeeProcessing reduced by $1.75. |
| **Validation: check exceeding balance rejected** | *(none)* | Player owes $100, director enters $150 check. Rejected with error showing the actual balance owed. No record created. |
| **Validation: correction exceeding balance rejected** | *(none)* | Player owes $100, director enters +$150 correction. Rejected with error showing the actual balance owed. No record created. |
| **Validation: $0 check rejected** | *(none)* | System rejects $0 checks. No accounting record created. |
| **Validation: $0 correction rejected** | *(none)* | System rejects $0 corrections. No accounting record created. |

---

## Team Accounting Tests (4 tests)

These test what happens when a director records a payment in **search/teams** against a single team (team scope).

**Key difference from player tests:** Team payments are recorded against the **club rep's registration** with a `TeamId` linking to the specific team. After payment, the club rep's financial totals are recalculated from all their teams.

| Test | Record Type | What It Checks |
|------|-------------|---------------|
| **Team Check: $500 pays team in full** | Check | Creates Check record with both RegistrationId (club rep) and TeamId (the team). Team balance = $0. |
| **Team Check: $500 with $17.50 processing fee** | Check | Processing fee fully removed in full-pay-required mode. FeeProcessing drops from $17.50 to $0. Allocation reports the $17.50 reduction. |
| **Team Correction: +$200 against $500 owed** | Correction | Creates Correction record with TeamId set. Team balance = $300. |
| **Validation: Check exceeding owed total** | *(none)* | Cannot pay more than the club rep owes. Rejected with error. |

---

## Club Allocation Tests (5 tests)

These test what happens when a director records a **club-level** check in **search/teams** (club scope). A single check is distributed across multiple teams.

**Distribution rules:**
- Teams sorted by OwedTotal (highest first) — OwedTotal is the single source of truth
- Each team's allocation = min(OwedTotal, remaining check balance)
- Processing fee reduced first: `allocationAmount × processingRate`, capped at team's FeeProcessing
- Final allocation recalculated after fee reduction (never overpays the reduced balance)
- Check balance carries to next team until exhausted
- Dropped/waitlisted teams are excluded

| Test | What It Checks |
|------|---------------|
| **$1500 across 3 teams ($500 each)** | 3 separate Check records created (one per team, $500 each). All teams fully paid. Same check number on all records. |
| **$900 partial across 3 teams** | Distributed highest-OwedTotal-first. Top 2 teams get funded. Third team gets $0 (check exhausted). Only funded teams get records. |
| **$1000 across 2 teams with processing fees** | Fee reduction happens first ($17.50 per team removed), then $500 allocated per team against the reduced $500 owed. Both teams end at $0. |
| **$1300 partial with processing fees** | 3 teams with different OwedTotals. Fee reduction first, then allocation on reduced balance. Third team gets $200 (remaining), fee reduced by $7.00 ($200 × 3.5%), NOT the full $14.00. Club rep totals reflect all reductions. |
| **Dropped team excluded** | Club has 2 active + 1 dropped team. Only the 2 active teams receive payment. Dropped team gets nothing. |

---

## Key Accounting Principles

### Processing Fee Reduction (Check & Correction)
When a job has CC processing fees enabled (e.g., 3.5%), those fees are added to every registration/team total. But if payment is by **check** or **correction**, the CC surcharge is removed because no credit card was used.

```
Fee reduction = payment amount x processing rate

Example:
  Team owes $517.50 ($500 base + $17.50 processing at 3.5%)
  Director records a $500 check
  Fee reduction: entire $17.50 processing fee removed (full-pay mode)
  New total: $500. Paid: $500. Balance: $0.
```

### Club Payment Distribution
When paying for the whole club by check, the system distributes the payment across teams starting with the **highest balance first**. Each team gets its own accounting record with the allocated amount. This means the ledger shows exactly how much of the check went to each team.

### Record Types
| Payment Method | When Used | DiscountCodeAi |
|---------------|-----------|----------------|
| Check Payment By Client | Director records a check | null |
| Correction | Director manually adjusts balance (scholarship, credit, etc.) | null |
| Credit Card Payment By Client | CC charge through Authorize.Net | null |
| Correction (with DC) | 100% discount code applied via team registration | set to discount code ID |

---

## Reading Test Results

```
PASSED:
  [PASS] Check: $100 paid by check → Check record created, balance $0
  [PASS] Check: $100 check removes $3.50 processing fee → FeeProcessing=$0, balance $0
  [PASS] Check: $50 partial reduces FeeProcessing by $1.75 → balance $51.75
  [PASS] Correction: +$50 manual adjustment → Correction record (no DC), balance $50
  [PASS] Correction: +$50 with processing fees → FeeProcessing reduced by $1.75
  [PASS] Validation: check exceeding balance rejected — no record created
  [PASS] Validation: correction exceeding balance rejected — no record created
  [PASS] Validation: $0 check rejected — no record created
  [PASS] Validation: $0 correction rejected — no record created

ALL 9 TESTS PASSED
```

If a test **fails**, it means the system calculated something incorrectly. The error will explain what was **expected** vs. what **actually happened**.

**Need help?** Ask Claude Code: *"explain the accounting test results"* or *"why did this test fail?"*
