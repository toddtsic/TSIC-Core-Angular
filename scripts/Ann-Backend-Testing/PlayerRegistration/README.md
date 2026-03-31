# Backend Player Registration Tests

These automated tests validate the player registration wizard flow: form validation, team placement, fee calculation, waitlist handling, and the PreSubmit pipeline.

**No database or server needs to be running** — tests use an in-memory database.

## How to Run

In VS Code, navigate to `scripts/Ann-Backend-Testing/PlayerRegistration/` in the file explorer.

1. **Right-click** the test file you want to run (e.g., `PreSubmit-Tests.ps1`)
2. Select **"Run Code"** from the menu
3. Results appear in the output panel at the bottom

### Available test files:
- `EarlyBird-LateFee-Tests.ps1` — **NEW** Early bird discounts & late fees (time-windowed modifiers) ✅ IMPLEMENTED
- `PreSubmit-Tests.ps1` — Core PreSubmit pipeline (team assignment, mode handling, persistence) *(planned)*
- `FormValidation-Tests.ps1` — Server-side metadata-driven field validation *(planned)*
- `TeamPlacement-Tests.ps1` — Roster capacity checks and waitlist redirection *(planned)*
- `FeeApplication-Tests.ps1` — Initial fee resolution and application to new registrations *(planned)*

---

## Test Plan

### Early Bird & Late Fee Tests (25 tests) ✅ IMPLEMENTED

These test the **new fee modifier system** (not in legacy). Modifiers are time-windowed: an EarlyBird discount is active between StartDate→EndDate, a LateFee kicks in between its own StartDate→EndDate. This is the new concept you're most interested in validating.

#### How Modifiers Work

Admins configure time-windowed modifiers on fee rows:
- **EarlyBird** (e.g., Jan 1 – Feb 15): reduces FeeDiscount — register early, save money
- **LateFee** (e.g., Apr 1 – Jun 30): increases FeeLatefee — register late, pay a surcharge
- **Normal window** (Feb 16 – Mar 31): no modifiers active — player pays base fee only
- Modifiers stack across cascade levels (job + agegroup + team)
- On team swap, modifiers are **frozen** — early bird stays even if you swap during late fee window

#### Early Bird Discount (5 tests)

| Test | What It Checks |
|------|---------------|
| **During window: discount applied** | Registration on Jan 20 gets $25 off |
| **After window: no discount** | Registration on Mar 1 gets $0 off |
| **On exact start date** | Jan 1 boundary — discount applies |
| **On exact end date** | Feb 15 boundary — discount applies |
| **Day after end** | Feb 16 — discount gone |

#### Late Fee (3 tests)

| Test | What It Checks |
|------|---------------|
| **During window: fee applied** | Registration on May 1 incurs $30 late fee |
| **Before window: no fee** | Registration on Mar 1 — no late fee |
| **On exact start date** | Apr 1 boundary — late fee applies |

#### Combined Windows (3 tests)

| Test | What It Checks |
|------|---------------|
| **Normal window** | Neither early bird nor late fee applies |
| **Early bird window** | Discount yes, late fee no |
| **Late fee window** | Late fee yes, discount no |

#### Cascade Stacking (4 tests)

| Test | What It Checks |
|------|---------------|
| **Job + agegroup early bird** | $10 + $15 = $25 total discount |
| **All three levels** | Job $10 + agegroup $5 + team $3 = $18 total |
| **Late fees from different levels** | Job $20 + agegroup $10 = $30 total late fee |
| **Mixed types at different levels** | Early bird at job+agegroup, late fee at job — each applies in its window |

#### Discount + EarlyBird Stacking (1 test)

| Test | What It Checks |
|------|---------------|
| **Always-on Discount + windowed EarlyBird** | During early bird: $10 + $25 = $35. After: just $10 Discount. Both go to TotalDiscount. |

#### Null Date Boundaries (3 tests)

| Test | What It Checks |
|------|---------------|
| **Null StartDate** | Active from beginning of time until EndDate |
| **Null EndDate** | Active from StartDate until end of time |
| **Both null** | Always active (permanent modifier) |

#### Overlapping Windows (1 test)

| Test | What It Checks |
|------|---------------|
| **Two early birds with overlapping dates** | Both stack during overlap, only one applies after first expires |

#### Full Financial Snapshot (3 tests)

| Test | What It Checks |
|------|---------------|
| **Early bird: complete FeeTotal** | $200 base + $7 processing - $25 discount = $182 owed |
| **Late fee: complete FeeTotal** | $200 base + $7 processing + $30 late fee = $237 owed |
| **Normal window: no modifiers** | $200 base + $7 processing = $207 owed |

#### Swap Preservation (2 tests)

| Test | What It Checks |
|------|---------------|
| **Swap preserves early bird during late fee window** | Player registered with $25 early bird. Swaps teams in May. Early bird stays, no late fee added. |
| **Swap to different-fee team keeps modifiers** | Team A = $200, Team B = $300. Swap updates FeeBase to $300 but keeps $25 early bird discount. |

#### Edge Cases (4 tests)

| Test | What It Checks |
|------|---------------|
| **No modifiers configured** | Returns TotalDiscount=0, TotalLateFee=0 |
| **No fee row at all** | ResolveFee returns null |
| **All 3 modifier types on same fee** | Discount+EarlyBird → TotalDiscount; LateFee → TotalLateFee |
| **Agegroup modifier only (no job modifier)** | Still applies — doesn't require parent level |

---

### PreSubmit Tests (PlayerRegistrationService.PreSubmitAsync)

The PreSubmit endpoint is the heart of player registration. It processes player/team selections, validates form data, creates pending registrations, and determines the next wizard step.

#### PP Mode (Player+Parent) — Single Team Per Player

| Test | What It Checks |
|------|---------------|
| **New registration: single player, single team** | Creates pending registration (BActive=false) with correct JobId, UserId, FamilyUserId, AssignedTeamId. Fees applied. NextTab = "Payment". |
| **New registration: two players, one team each** | Creates 2 separate registrations. Both linked to same FamilyUserId. |
| **Form values mapped to registration** | FormValues dictionary entries (FirstName, LastName, DOB, etc.) written to registration entity columns via FormValueMapper reflection. |
| **Team change before payment** | Existing unpaid registration updated with new AssignedTeamId. No new registration created. |
| **Team change blocked after payment (different fee)** | Player has paid registration. New team has different FeeBase. Team change rejected — original assignment preserved. |
| **Team change allowed after payment (same fee)** | Player has paid registration. New team has identical FeeBase. Team swap succeeds. |
| **Multi-team rejected in PP mode** | Player submits 2+ team selections. Returns validation error "Multiple teams not allowed for this job". No registrations created. |

#### CAC Mode (Club+Affiliate+Camp) — Multiple Teams Per Player

| Test | What It Checks |
|------|---------------|
| **Multi-team: player registers for 3 teams** | Creates 3 separate registrations for same player, each with different AssignedTeamId. |
| **Multi-team: existing paid + new team** | Player has paid registration for Team A. Submits for Team A + Team B. Team A stays, new registration created for Team B. |
| **Multi-team: team reassignment without payment** | Unpaid registration for Team A. Player submits for Team B instead. Registration updated (not duplicated). |

#### Validation Integration

| Test | What It Checks |
|------|---------------|
| **Validation errors prevent persistence** | Required field missing. PreSubmit returns validationErrors[]. No registrations saved to DB. |
| **Validation passes: registrations saved** | All required fields present. Registrations persisted. validationErrors[] empty. |
| **Conditional field validation** | Field visible only when another field = specific value. If condition met and field empty, error returned. If condition not met, field skipped. |

#### NextTab Routing

| Test | What It Checks |
|------|---------------|
| **All teams have capacity** | NextTab = "Payment" |
| **Any team full (waitlisted)** | NextTab = "Team" (user needs to pick another team) |

---

### Form Validation Tests (PlayerFormValidationService)

Server-side validation runs against the job's metadata JSON schema. These tests verify each field type and edge case.

| Test | What It Checks |
|------|---------------|
| **Required text field: empty** | Returns error with field name and "required" message |
| **Required text field: present** | No error returned |
| **Number field: valid** | "123" passes |
| **Number field: non-numeric** | "abc" returns format error |
| **Select field: valid option** | Value matches one of the allowed options |
| **Select field: invalid option** | Value not in options list — returns error |
| **Multiselect: all valid** | Array of values, all in options list |
| **Multiselect: one invalid** | One value not in options — returns error for that value |
| **Checkbox: truthy variants** | "true", "yes", "1", "y", "on", "checked" all pass |
| **Checkbox: falsy treated as unchecked** | "false", "no", "0" — not an error, just unchecked |
| **Conditional field: condition met, field empty** | Error returned (field is required and visible) |
| **Conditional field: condition not met** | Field skipped entirely — no validation |
| **Hidden field skipped** | Fields with visibility="hidden" are never validated |
| **AdminOnly field skipped** | Fields with visibility="adminOnly" are never validated |
| **Empty metadata JSON** | Returns empty error list (no fields to validate) |
| **Null metadata JSON** | Returns empty error list (graceful handling) |

---

### Team Placement Tests (ITeamPlacementService)

| Test | What It Checks |
|------|---------------|
| **Team has capacity** | Returns original teamId, isWaitlisted=false |
| **Team full, waitlists enabled** | Creates WAITLIST mirror agegroup/division/team. Returns waitlist teamId, isWaitlisted=true |
| **Team full, waitlists disabled** | Throws InvalidOperationException |
| **Waitlist mirror is idempotent** | Calling twice for same full team returns same waitlist IDs |
| **Waitlist naming convention** | Mirror agegroup = "WAITLIST - {OriginalName}", mirror division = "WAITLIST - {DivisionName}" |

---

### Fee Application Tests (FeeResolutionService)

| Test | What It Checks |
|------|---------------|
| **New registration: base fee applied** | FeeBase set from resolved fee cascade (Team > Agegroup > Job) |
| **New registration: processing fee calculated** | FeeProcessing = FeeBase * processingRate (when enabled) |
| **New registration: no processing fee when disabled** | FeeProcessing = 0 when job has BAddProcessingFees=false |
| **New registration: discount applied** | FeeDiscount set from modifier evaluation |
| **New registration: late fee applied** | FeeLatefee set from modifier evaluation |
| **New registration: FeeTotal computed** | FeeTotal = FeeBase - FeeDiscount + FeeLatefee + FeeProcessing |
| **New registration: OwedTotal = FeeTotal** | New registration has PaidTotal=0, so OwedTotal = FeeTotal |
| **Team swap: only FeeBase changes** | FeeDiscount and FeeLatefee frozen from original registration |
| **Team swap: same fee team** | All financials unchanged |
| **Fee cascade: team override wins** | Team-level fee overrides agegroup and job defaults |
| **Fee cascade: agegroup fallback** | No team override — uses agegroup fee |
| **Fee cascade: job default** | No team or agegroup override — uses job-level fee |
| **Processing fee floor: 3.5% minimum** | Job with 2% rate still uses 3.5% floor |
| **FeeBase=0 skips fee application** | If FeeBase already set (> 0), ApplyNewRegistrationFeesAsync is skipped |

---

## Key Testing Principles

### What Each Test Verifies

Every test checks **two things**:

1. **The registration record(s) created** — Were the right registrations made?
   - BActive (always false after PreSubmit — activated at payment)
   - AssignedTeamId (correct team assignment)
   - JobId, UserId, FamilyUserId (correct ownership)
   - Form field values mapped correctly
   - Count of registrations (no duplicates, no missing)

2. **The financial state** — Were fees calculated correctly?
   - FeeBase, FeeDiscount, FeeLatefee, FeeProcessing, FeeTotal, OwedTotal

### Architecture Under Test

```
PreSubmitAsync (orchestrator)
  ├─ PlayerFormValidationService  → validates form values against metadata
  ├─ FeeResolutionService         → resolves and applies fees
  ├─ TeamPlacementService         → checks capacity, redirects to waitlist
  ├─ FormValueMapper              → maps form values to entity properties
  └─ RegistrationRepository      → persists registrations
```

---

## Reading Test Results

```
PASSED:
  [PASS] PP Mode: new registration creates pending record with correct team
  [PASS] PP Mode: team change before payment swaps team
  [PASS] PP Mode: team change blocked after payment with different fee
  [PASS] CAC Mode: multi-team creates separate registrations
  [PASS] Validation: required field missing prevents persistence
  [PASS] Fee: base fee applied from cascade resolution

ALL 6 TESTS PASSED
```

If a test **fails**, it means the system calculated something incorrectly. The error will explain what was **expected** vs. what **actually happened**.

**Need help?** Ask Claude Code: *"explain the player registration test results"* or *"why did this test fail?"*
