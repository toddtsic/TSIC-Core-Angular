# Player Registration — Frontend Tests

## What These Tests Cover

These tests validate the **business logic** inside the Angular player registration wizard:

| Test Suite | What It Checks |
|---|---|
| **PaymentV2Service** | Payment option detection (PIF/Deposit/ARB), total calculations, discount math, line item generation |
| **PlayerFormsService** | Required field validation, type validation (email, number, date), field visibility rules, form value seeding |
| **EligibilityService** | Grad year / age group / club constraint detection, unified eligibility values |
| **TeamService** | Team filtering by eligibility (grad year, age group, age range, club name) |
| **FormSchemaService** | JSON metadata parsing, field type mapping, US Lacrosse detection, option set resolution |
| **WaiverStateService** | Waiver acceptance tracking, waiver field detection, metadata extraction, read-only seeding |

## How to Run

```powershell
.\Run-PlayerRegistration-Tests.ps1
```

## What to Expect

- Tests run in the terminal (no browser window)
- Each test shows PASS or FAIL
- Summary at the end: "ALL X TESTS PASSED" or "X PASSED, Y FAILED"
- If tests fail, error details are shown below the summary

## When to Run

- After changes to the player registration wizard
- After changes to payment logic, form validation, or eligibility rules
- Before committing registration-related frontend changes
