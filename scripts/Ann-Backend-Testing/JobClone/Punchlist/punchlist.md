# Job Clone - Punch List

**Tester:** Ann
**Date Started:** 2026-04-25
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

- [ ] **Source job selection** -- Step 1 picker, current-job auto-select, clone vs blank flows
- [ ] **Identity & uniqueness** -- Step 2 jobPath/jobName/displayName, year-token bumping, live uniqueness check
- [ ] **Dates & shifts** -- Step 3 year-delta preview, date adjustments
- [ ] **LADT scope** -- Step 4 league/agegroup/division/team selection for cloning
- [ ] **Fee defaults** -- Step 5 player/team/clubrep fee carry-forward and overrides
- [ ] **People & options** -- Step 6 admin Registrations, configuration carry-forward toggles
- [ ] **Review & submit** -- Step 7 final summary + submit
- [ ] **Activation** -- Post-clone "Turn the new job on" steps (suspend release, director access)
- [ ] **Permissions** -- SuperUser-only gating verified at frontend tab + backend endpoints
- [ ] **Edge cases** -- Same-year clone, cross-customer guard, identity collision handling

---

## Punch List Items

### PL-004: Step 7 "Create job" button does nothing — affirmation checkbox is too easy to miss
- **Refs**: PL-003 (wizard restructure — Step 7 becomes Step 2 in the new shape; same affirmation gate would carry forward)
- **Area**: Wizard navigation / Review & submit
- **What I did**: Reached Step 7, clicked "Create job" — nothing happened
- **What I expected**: Button to fire the create-job action
- **What happened**: Button is `disabled` until the affirmation checkbox above it is ticked. Clicks on a disabled button fire no event, so the button visibly does nothing without explanation
- **Severity**: Bug (UX) — major blocker if missed
- **Status**: Open
- **Note**: Cause traced:
  - **Code path**: Button `[disabled]="!canAdvance() || isSubmitting()"` ([job-clone.component.html:418](TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/configure/job-clone/job-clone.component.html#L418)). For step 7, `canAdvance()` returns `this.affirmationChecked` ([job-clone.component.ts:353](TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/configure/job-clone/job-clone.component.ts#L353)).
  - **The mandatory checkbox**: lives at [job-clone.component.html:384-387](TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/configure/job-clone/job-clone.component.html#L384-L387) labeled *"I've reviewed the above and want to create the job."*
  - **Why it feels broken**: a `disabled` HTML button doesn't fire click events at all — the user sees zero feedback. They don't know the button is gated by a checkbox above it.
  - **Verification step for Ann**: confirm the checkbox tick fixes the disable. If checking it doesn't enable the button, there's a real OnPush change-detection bug — `affirmationChecked` is a plain property (line 93) on a component using `ChangeDetectionStrategy.OnPush` ([line 36](TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/configure/job-clone/job-clone.component.ts#L36)). `[(ngModel)]` should still trigger change detection, but if Zoneless or some custom CD setup interferes, a fix is to convert `affirmationChecked` to a `signal(false)` and use `[ngModel]` + `(ngModelChange)`.
  - **UX fix options regardless** (since "missed checkbox + silent button" is a major UX failure):
    - **A. Show a visible hint when button is disabled** — e.g., a tooltip or small italic line beneath the button: *"Tick the confirmation checkbox above to enable."*
    - **B. Make the checkbox more prominent** — wrap in an attention-getting card with a callout color (warning yellow / accent border).
    - **C. Auto-scroll to the checkbox** when user clicks the disabled button area (using a `pointer-events: auto` overlay that captures the click and scrolls).
    - **D. Drop the affirmation entirely** — replace with a confirm-modal that fires from the button. Single click → "Are you sure?" dialog → confirm. Removes the disabled-button trap.
  - **Recommendation**: D — moves the friction to a moment after the user has clearly indicated intent (the click), instead of pre-blocking with a checkbox they might not see. Same safety, better UX.

### PL-003: Consolidate wizard from 7 steps to 2 steps with 4 cards in Step 1
- **Refs**: PL-002 (Step 3 expiry-date defaults — would land in Step 1's Dates card under the new shape); PL-008 (Age Ranges menu hidden when `teamEligibilityByAge` flag is off — same reason DOB windows can be dropped)
- **Area**: Wizard navigation / IA
- **What I did**: Reviewed the 7-step Job Clone wizard and proposed a 2-step shape with all editable fields surfaced as cards on Step 1
- **What I expected**: A wizard with the minimum number of steps that still surfaces the decisions SuperUsers need to make
- **What happened**: Currently 7 steps (Source / Identity / Dates & shifts / LADT scope / Fee defaults / People & options / Review & submit) — more screen-by-screen navigation than the decisions warrant
- **Severity**: UX / Feature
- **Status**: Open — Todd discussion
- **Note**: Concrete proposed shape — **2-step wizard with 4 cards in Step 1**:

  | Step | Contents |
  |---|---|
  | **Step 1 — Setup** | 4 cards (parallel): |
  | | • **Identity card** — read-only "You're cloning this job" + editable jobPath / jobName / year / season / displayName + optional "Registration from email" (moved in from current Step 6) |
  | | • **Dates card** — Admin Expiry, User Expiry (current Step 3; with PL-002's source-based prefill applied) |
  | | • **LADT scope card** — current Step 4 LADT scope + **"Advance agegroup grad years" toggle** (moved in from Step 6, with the "DOB windows" half dropped) + **"Remove parallax slide 1" toggle** (moved in from Step 6) |
  | | • **Fees card** — current Step 5 fee defaults |
  | **Step 2 — Review & submit** | Final confirmation + the *"Every cloned Director + SuperDirector lands inactive..."* tip (informational, moves to this step where it's most useful) |

  - **Field redistribution from the eliminated Step 6**:
    | Step 6 element | New home in 2-step shape |
    |---|---|
    | Tip: "Every cloned Director + SuperDirector lands inactive..." | Step 2 Review screen as a confirmation reminder |
    | "Advance agegroup grad years + DOB windows" toggle | Split — keep "advance grad years" in LADT scope card; drop "DOB windows" half (PL-008: Age Ranges feature unused) |
    | "Remove parallax slide 1" toggle | LADT scope card |
    | "Registration from email" input | Identity card (per-job header metadata fits there) |
  - **Net effect**: 5 screens dropped (7 → 2). Navigation goes from chip-stepper-clicking through 7 screens to a single vertical-scan setup screen + a review confirmation.
  - **Decision points for Todd**:
    1. Confirm the 4-cards-in-Step-1 shape — Identity / Dates / LADT scope / Fees as parallel cards.
    2. Confirm leftover Step 6 fields land where suggested (reg-from-email in Identity, parallax in LADT, grad-year in LADT).
    3. Confirm "DOB windows" can be dropped from the agegroup-advance toggle (matches PL-008 — Age Ranges feature is flag-gated to virtually no current customers).
    4. Are there per-step backend validations that would be hard to consolidate into one Step 1 submit? Step 2's identity uniqueness check (`jobIdentityExists`) currently fires before advancing — would need to fire on Step 1 → Step 2 transition instead.
    5. Visual choice — should the 4 cards stack vertically or render in a 2x2 grid on wide viewports?

### PL-002: Job Clone Step 3 Admin Expiry / User Expiry default to today+1yr instead of source+1yr
- **Refs**: PL-001 (same wizard); Legacy parity is the standard expectation here
- **Area**: Dates & shifts (Step 3)
- **What I did**: Compared Legacy's pre-fill behavior for Admin Expiry / User Expiry against the new system on Step 3
- **What I expected**: Each expiry to default to the source job's existing expiry **shifted forward by one year** — preserves the seasonal cadence (e.g., if Director always closes registration April 1, the clone's expiry pre-fills as April 1 of the next year)
- **What happened**: Both fields default to `new Date()` + 1 year, regardless of the source job's existing expiry dates ([job-clone.component.ts:149-152, 252-255](TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/configure/job-clone/job-clone.component.ts#L149-L152)). If the SuperUser clones a 2025 season job in April 2026, the prefill is April 2027 — which is six months off the actual seasonal cadence (likely should be August 2027 or whatever was set for the 2025 season + 1).
- **Severity**: Bug
- **Status**: Open
- **Note**: Fix requires a backend change too — `JobCloneSourceDto` does not currently carry `expiryAdmin` / `expiryUsers` (grep returns no match in the model file). Two-part fix:
  1. **Backend**: extend `JobCloneSourceDto` and the source-projection in `JobCloneRepository` (or wherever sources are loaded) to include `ExpiryAdmin` and `ExpiryUsers` from the source job.
  2. **Frontend**: replace the `new Date() + 1 year` defaults with `(source.expiryAdmin ?? today) + 1 year` and `(source.expiryUsers ?? today) + 1 year`. Fall back to today+1 only when source expiry is null (true for very old jobs migrated without expiry dates).
  - **Affected code**: the `today + 1` logic appears twice ([line 149-152](TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/configure/job-clone/job-clone.component.ts#L149-L152) for the blank flavor and [line 252-255](TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/configure/job-clone/job-clone.component.ts#L252-L255) for clone-flavor source-load). Only the second one needs the source-based logic; the blank flow can keep today+1 since there's no source.

### PL-001: Step 2 Reset and Back buttons appear redundant — both go to step 1
- **Area**: Source job selection / Identity & uniqueness (wizard nav)
- **What I did**: Reached Step 2, saw both "Reset" and "Back" buttons rendered side by side
- **What I expected**: One way to go back to step 1, not two visually identical paths
- **What happened**: Both buttons exist on step 2; they actually behave differently but the difference isn't visible to the user on this step
- **Severity**: UX
- **Status**: Open
- **Note**: The two buttons are genuinely different — but indistinguishable on step 2:
  - **Reset** (`cancelWizard`, [job-clone.component.ts:156-160](TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/configure/job-clone/job-clone.component.ts#L156-L160)): wipes all entered fields, returns to step 1, preserves the chosen flavor (clone vs blank). Destructive.
  - **Back** (`wizardBack`, [line 317-321](TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/configure/job-clone/job-clone.component.ts#L317-L321)): decrements step by 1, preserves all fields. Non-destructive.
  - On step 2 both go to step 1, so the user sees no visible difference. On step 5 the difference is obvious (Back → step 4, Reset → step 1).
  - **Options**:
    - **A. Hide Reset on step 2** — only render Reset from step 3 onwards (where the destination genuinely differs from Back).
    - **B. Rename Reset → "Start Over" with confirm dialog** — make destructive nature explicit; signals it's not the same as Back.
    - **C. Both A and B** — start-over wording + hidden on step 2.
    - **D. Drop Reset entirely** — repeated Back-Back-Back returns to step 1 with data intact; users wanting a clean slate navigate away and reopen.
  - **Recommendation**: B. Reset is genuinely useful on later steps; the fix is making its destructive nature explicit rather than removing it. A confirm dialog ("Start over and clear all entered data?") prevents accidental clicks.
