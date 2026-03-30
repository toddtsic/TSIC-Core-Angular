# Backend Accounting Tests

These automated tests verify that payments, fees, and financial calculations work correctly. Each test simulates a real scenario and checks the math.

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

## What the Tests Validate

### Player Accounting
- **Check payments**: Recording a check updates Paid and Balance correctly
- **Processing fee removal**: When paying by check, CC processing fees are removed proportionally
- **Partial payments**: Paying less than the full amount reduces fees proportionally
- **Corrections**: Manual corrections (scholarships, adjustments) update the balance
- **Validation**: $0 payments are rejected

### Team Accounting — *coming soon*
- Single-team check, CC charge, correction
- Processing fee handling at team level
- Club rep financial sync after payment

### Club Allocation — *coming soon*
- Check distributed across multiple teams (highest balance first)
- Processing fee reductions per team
- CC charges per team

## Reading Test Results

```
PASSED:
  [PASS] Check: $100 payment against $100 owed → balance $0
  [PASS] Check: $100 payment removes $3.50 processing fee → balance $0
  [PASS] Check: $50 partial payment reduces processing fee by $1.75 → balance $51.75
  [PASS] Correction: +$50 scholarship against $100 owed → balance $50
  [PASS] Validation: $0 check is rejected
  [PASS] Validation: $0 correction is rejected

ALL 6 TESTS PASSED
```

If a test **fails**, it means the system calculated something incorrectly. The error message will explain what was expected vs. what actually happened.

**Need help?** Ask Claude Code: *"explain the accounting test results"* or *"why did this test fail?"*
