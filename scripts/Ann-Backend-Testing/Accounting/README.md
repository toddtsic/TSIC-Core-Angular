# Backend Accounting Tests

These automated tests simulate real payment scenarios and verify that the system creates the correct accounting records and calculates fees properly.

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

## What Each Test Verifies

Every test checks **two things**:

1. **The accounting record created** — Was the right type of record made?
   - Payment method (Check, Correction, Credit Card)
   - Amount recorded (Payamt)
   - Check number (if applicable)
   - Discount code link (DiscountCodeAi — null for manual corrections, set for discount codes)

2. **The registration/team financial state after** — Did the math come out right?
   - FeeProcessing (CC surcharge — should be reduced when paying by check)
   - FeeTotal (recalculated after fee adjustment)
   - PaidTotal (how much has been paid)
   - OwedTotal (balance remaining)

## Player Accounting Tests

| Test | What It Checks |
|------|---------------|
| **Check: $100 paid by check** | Creates a Check record (PaymentMethodId=Check), balance goes to $0 |
| **Check: $100 removes $3.50 processing fee** | When paying by check, the CC processing fee ($3.50 at 3.5%) is removed because no credit card was used. FeeProcessing drops from $3.50 to $0. |
| **Check: $50 partial payment** | Partial check only removes a proportional processing fee ($50 × 3.5% = $1.75 removed). Remaining balance = $51.75. |
| **Correction: +$50 manual adjustment** | Creates a Correction record (NOT a discount code — DiscountCodeAi is null). This is a director manually crediting the account. |
| **Correction: +$50 with processing fees** | Corrections reduce processing fees the same way checks do — the principle is "non-CC payments don't incur CC fees." |
| **Validation: $0 check rejected** | System rejects $0 checks and does NOT create an accounting record. |
| **Validation: $0 correction rejected** | System rejects $0 corrections and does NOT create an accounting record. |

### Key Accounting Principle: Processing Fee Reduction

When a job has CC processing fees enabled (e.g., 3.5%), those fees are added to the player's total. But if the player pays by **check** or receives a **correction**, they shouldn't pay the CC surcharge. The system automatically reduces the processing fee proportionally:

```
Fee reduction = payment amount × processing rate

Example:
  Player owes $103.50 ($100 base + $3.50 processing at 3.5%)
  Director records a $100 check
  Fee reduction: $100 × 3.5% = $3.50
  Processing fee: $3.50 → $0
  New total: $100. Paid: $100. Balance: $0.
```

## Team Accounting Tests — *coming soon*

- Single-team check, CC charge, correction
- Processing fee handling at team level
- Club rep financial totals recalculated after each payment

## Club Allocation Tests — *coming soon*

- How a single check is distributed across multiple teams (highest balance first)
- Processing fee reductions applied per team
- CC charges issued per team (each team gets its own Authorize.Net transaction)

## Reading Test Results

```
PASSED:
  [PASS] Check: $100 paid by check → Check record created, balance $0
  [PASS] Check: $100 check removes $3.50 processing fee → FeeProcessing=$0, balance $0
  [PASS] Check: $50 partial reduces FeeProcessing by $1.75 → balance $51.75
  [PASS] Correction: +$50 manual adjustment → Correction record (no DC), balance $50
  [PASS] Correction: +$50 with processing fees → FeeProcessing reduced by $1.75
  [PASS] Validation: $0 check rejected — no record created
  [PASS] Validation: $0 correction rejected — no record created

ALL 7 TESTS PASSED
```

If a test **fails**, it means the system calculated something incorrectly. The error will explain what was **expected** vs. what **actually happened**.

**Need help?** Ask Claude Code: *"explain the accounting test results"* or *"why did this test fail?"*
