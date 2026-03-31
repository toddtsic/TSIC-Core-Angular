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
- Compute base owed per team: `baseOwed = OwedTotal / (1 + processingRate)` — strips the processing fee from the total
- Each team's allocation = `min(baseOwed, remaining check balance)`
- Fee reduction = `allocation × processingRate` — same formula as player path, no special cases
- Check balance carries to next team until exhausted
- Dropped/waitlisted teams are excluded

| Test | What It Checks |
|------|---------------|
| **$1500 across 3 teams ($500 each)** | 3 separate Check records created (one per team, $500 each). All teams fully paid. Same check number on all records. |
| **$900 partial across 3 teams** | Distributed highest-OwedTotal-first. Top 2 teams get funded. Third team gets $0 (check exhausted). Only funded teams get records. |
| **$1000 across 2 teams with processing fees** | Each team: baseOwed=$517.50/1.035=$500, allocation=$500, feeReduction=$500×3.5%=$17.50. Both teams end at $0. |
| **$1300 partial with processing fees** | 3 teams with different OwedTotals. Alpha gets $600 (base), Bravo gets $500, Charlie gets $200 (remaining). Fee reduction = allocation × 3.5% for each. Charlie: $200×3.5%=$7.00, NOT $14.00. |
| **Dropped team excluded** | Club has 2 active + 1 dropped team. Only the 2 active teams receive payment. Dropped team gets nothing. |

---

## Allocation Matrix Tests (18 tests)

Systematic coverage of all fee configuration × timeframe × payment type combinations. Each test uses 3 teams with Deposit=$500, BalanceDue=$1,500, processing rate=3.5%.

**Three fee configurations:**
- **A: No fees** — processing fees disabled. CC and check behave identically (baseline).
- **B: Fees on balance due only** — processing fees apply at balance due phase, not on deposits.
- **C: Fees on both deposit and balance due** — processing fees on the full amount.

**Two timeframes:**
- **Deposit phase** — nothing paid yet.
- **Balance due phase** — deposit already paid by CC.

**Three payment types:**
- **CC** — verifies OwedTotal with no fee reduction.
- **Check full** — verifies all teams fully paid with proportional fee reduction.
- **Check partial** — verifies highest-balance-first allocation with proportional fee reduction per team.

| Test | Config | Phase | Payment | Key Assertion |
|------|--------|-------|---------|---------------|
| A1 | No fees | Deposit | CC | $2,000/team, no processing |
| A2 | No fees | Deposit | Check full | $2,000/team, no reduction |
| A3 | No fees | Deposit | Check partial | $2,000 + $1,000, no reduction |
| A4 | No fees | Balance due | CC | $1,500/team |
| A5 | No fees | Balance due | Check full | $1,500/team, all paid |
| A6 | No fees | Balance due | Check partial | $1,500 + $500 |
| B1 | Balance only | Deposit | CC | No fees yet at deposit |
| B2 | Balance only | Deposit | Check full | No fees to reduce |
| B3 | Balance only | Deposit | Check partial | No fees to reduce |
| B4 | Balance only | Balance due | CC | $1,552.50/team |
| B5 | Balance only | Balance due | Check full | $1,500/team, $52.50 reduction each |
| B6 | Balance only | Balance due | Check partial | $1,500 + $500, reductions $52.50 + $17.50 |
| C1 | Both | Deposit | CC | $2,070/team |
| C2 | Both | Deposit | Check full | $2,000/team, $70 reduction each |
| C3 | Both | Deposit | Check partial | $2,000 + $1,000, reductions $70 + $35 |
| C4 | Both | Balance due | CC | $1,552.50/team |
| C5 | Both | Balance due | Check full | $1,500/team, $52.50 reduction (Ann's scenario) |
| C6 | Both | Balance due | Check partial | $1,500 + $500, reductions $52.50 + $17.50 |

---

## Key Accounting Principles

### Processing Fee Reduction (Check & Correction)
When a job has CC processing fees enabled (e.g., 3.5%), those fees are added to every registration/team total. But if payment is by **check** or **correction**, the CC surcharge is removed proportionally because no credit card was used.

**The formula is the same for players and teams:**
```
Fee reduction = check amount × processing rate

Example (player/single team):
  Director enters a $500 check
  Fee reduction: $500 × 3.5% = $17.50

Example (club allocation):
  Team owes $517.50 ($500 base + $17.50 processing)
  Base owed = $517.50 / 1.035 = $500  ← strip the processing from OwedTotal
  Allocation = $500 (the base amount)
  Fee reduction = $500 × 3.5% = $17.50
  New total: $500. Paid: $500. Balance: $0.
```

**Why `OwedTotal / (1 + rate)`?** OwedTotal already includes processing fees. Applying the rate directly to OwedTotal would "tax the tax." Dividing by `(1 + rate)` extracts the base amount, then `base × rate` gives the exact processing fee embedded in that total.

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

### Club Allocation + Matrix Test Results (23 tests)

```
  ========================================
    Club Payment Allocation Tests
    (search/teams, club scope)
  ========================================

  PASSED:
    [PASS] A1: No fees, deposit, CC → $500/team, no fee adjustment
    [PASS] A3: No fees, deposit, check partial → highest balance first
    [PASS] Club Check: $1000 across 2 teams removes $35 total processing fees
    [PASS] B6: Fees on balance only, balance due, check partial → proportional reduction
    [PASS] C5: Fees on both, balance due, check full → $1,500/team (Ann's scenario)
    [PASS] B4: Fees on balance only, balance due, CC → $1,552.50/team
    [PASS] A6: No fees, balance due, check partial → highest balance first
    [PASS] A4: No fees, balance due, CC → $1,500/team owed
    [PASS] Club Check: $900 partial across 3 teams → highest balance first
    [PASS] B3: Fees on balance only, deposit, check partial → no fees to reduce
    [PASS] C6: Fees on both, balance due, check partial → proportional reduction
    [PASS] B1: Fees on balance only, deposit, CC → no fees yet, $2,000/team
    [PASS] A2: No fees, deposit, check full → $500/team, no fee adjustment
    [PASS] C3: Fees on both, deposit, check partial → proportional reduction
    [PASS] Club Check: $1300 partial with processing fees → proportional reduction per team
    [PASS] B5: Fees on balance only, balance due, check full → $1,500/team
    [PASS] A5: No fees, balance due, check full → $1,500/team, all paid
    [PASS] C4: Fees on both, balance due, CC → $1,552.50/team
    [PASS] C1: Fees on both, deposit, CC → $2,070/team owed
    [PASS] B2: Fees on balance only, deposit, check full → no fees to reduce
    [PASS] Club Check: dropped team excluded from allocation
    [PASS] C2: Fees on both, deposit, check full → $2,000/team, $70 reduction each
    [PASS] Club Check: $1500 across 3 teams ($500 each) → 3 records, all paid

ALL 23 TESTS PASSED
```

**Need help?** Ask Claude Code: *"explain the accounting test results"* or *"why did this test fail?"*
