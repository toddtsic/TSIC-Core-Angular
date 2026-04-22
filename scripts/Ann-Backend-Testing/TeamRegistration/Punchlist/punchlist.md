# Team Registration - Punch List

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

- [ ] **Club Rep Login** -- Log in as club rep, verify correct teams shown
- [ ] **Team Selection** -- Select teams to register, check eligibility and capacity
- [ ] **Team Forms** -- Fill out required team-level fields, check validation
- [ ] **Roster Management** -- Add/remove players from team roster
- [ ] **Waivers** -- Team-level waivers and acceptance flow
- [ ] **Review Summary** -- Verify selections, fees, and roster before payment
- [ ] **Payment** -- Team registration payment, discount codes, receipts
- [ ] **Confirmation** -- Confirmation page and email for team registration

---

## Punch List Items

### PL-026: Add Reg Date column to all Review and Payment tables
- **Area**: Review Summary
- **What I did**: Looked at the Review and Payment tables
- **What I expected**: A Registration Date column showing when each team was registered
- **What happened**: No Reg Date column — add it to any Review or Payment tables
- **Severity**: UX
- **Status**: Fixed — shared `registered-teams-grid` (used on both Teams and Payment steps) has the Reg Date column sourced from `RegisteredTeamDto.registrationTs`.

### PL-025: Payment Status table missing columns — needs Paid, Base Fee, Processing Fee, CC Owed Total, Ck Owed Total
- **Area**: Review Summary
- **What I did**: Looked at the Payment Status table
- **What I expected**: Columns matching Legacy: Paid, Owed (base fee), Processing Fee, CC Owed Total, Ck Owed Total
- **What happened**: Data columns are missing. Should show: Paid ok, Owed (base fee), Processing Fee column, CC Owed Total, Ck Owed Total — see Legacy for reference
- **Severity**: Bug
- **Status**: Won't Fix — old review screen removed. The consolidated registered-teams-grid (used on both Teams and Payment steps) already has all five columns: Fee, Paid, Proc Fee, CC Owed, Check Owed. CC/Check Owed visibility is conditional based on job payment method config.

### PL-024: Age Group font too small in review table — match other column text size
- **Area**: Review Summary
- **What I did**: Looked at the review table on the "You're All Set" screen
- **What I expected**: Age Group text the same size as other columns
- **What happened**: Age Group font is noticeably smaller than the other columns — make it consistent
- **Severity**: UX
- **Status**: Won't Fix — old multi-step review screen removed; teams step now uses Syncfusion grid with consistent column sizing

### PL-023: Back buttons on Teams and Payment screens skip too many screens — should go back one at a time
- **Area**: Team Selection
- **What I did**: Clicked Back buttons on the Teams and Payment screens
- **What I expected**: Each click to go back exactly one screen
- **What happened**: Back buttons jump back too many screens instead of going one step at a time
- **Severity**: Bug
- **Status**: Won't Fix — old multi-step flow consolidated into single Teams step; navigation simplified

### PL-022: Select Your Teams screen shows entire amount paid when only deposit was paid — wrong
- **Area**: Team Selection
- **What I did**: Set a balance due scenario where only a deposit had been paid, then went to Select Your Teams screen
- **What I expected**: Amount shown to reflect only the deposit paid, with balance still owed
- **What happened**: Select Your Teams screen shows the entire amount as paid — this is wrong! The next screen ("You're All Set!") shows the correct amounts.
- **Severity**: Bug
- **Status**: Won't Fix — old Select Teams screen removed in step consolidation. Payment write path verified: `PaidTotal += perTeamAmount` correctly increments by actual amount charged (deposit or full), not total fee. Re-test with deposit scenario if concern persists.

### PL-021: "Pay balance due" login — after selecting a tournament, no options to proceed
- **Area**: Club Rep Login
- **What I did**: Used the "Login to pay balance due" option in the upper right, then selected a tournament
- **What I expected**: Options to view balance, make a payment, or take further action
- **What happened**: After selecting a tournament, there's nothing to do — dead end with no further options
- **Severity**: Bug
- **Status**: Future — requires Club Rep nav default items (currently zero menu items for that role) and nav self-healing pattern. The team wizard already handles returning reps with balances via the normal flow; this needs a dedicated nav entry point.

### PL-020: Consider merging Team Library and Select Teams into one step, then Review
- **Area**: Team Selection
- **What I did**: Walked through the Team Library → Select Teams → Review flow
- **What I expected**: Possibly a simpler flow with fewer steps
- **What happened**: Library and Select Teams feel like they could be combined into a single screen, followed by a Review screen. Worth discussing whether merging would streamline the process.
- **Severity**: Question
- **Status**: Won't Fix — already done; Library and Select merged into single Teams step

### PL-019: How do I remove a team from my Team Library?
- **Area**: Team Selection
- **What I did**: Looked for a way to remove a team from the Team Library
- **What I expected**: A delete or remove option for teams in the library
- **What happened**: No visible way to remove a team — is this possible? Should it be?
- **Severity**: Question
- **Status**: Fixed — teams registered for the current event can be removed via the trash icon in the Registered grid (only if unpaid). Library teams themselves cannot be deleted — historic team data must be preserved for future work on team rankings and cross-event history.

### PL-018: Club Payments need to be hooked up to Sandbox to test Confirmation screen
- **Area**: Payment
- **What I did**: Tried to complete a test payment during Team Registration
- **What I expected**: Payment to process through Sandbox so I can review the Confirmation screen
- **What happened**: Can't proceed past payment — Club Payments aren't connected to Sandbox yet. Blocking further testing of the Confirmation screen.
- **Severity**: Bug
- **Status**: Fixed — Sandbox credentials configured

### PL-017: Auto-populate credit card info with Club Rep details to save time — needs discussion
- **Area**: Payment
- **What I did**: Reached the credit card entry screen during Team Registration
- **What I expected**: Name/address fields pre-filled from Club Rep account info to speed up payment
- **What happened**: All fields are blank — during time-pressured registration windows, auto-populating with Club Rep info (with a warning about verifying Zip Code matches the card) would save significant time. Let's discuss this one.
- **Severity**: Question
- **Status**: Fixed — CC form now pre-fills from Club Rep contact info

### PL-016: Review screen — should amount owed by check (without processing fees) be shown?
- **Area**: Review Summary
- **What I did**: Registered two teams with full payment plus processing fees due
- **What I expected**: Possibly a separate line showing what would be owed if paying by check (no processing fees)
- **What happened**: Only the credit card total (with processing fees) is shown — should the check payment amount be included here too?
- **Severity**: Question
- **Status**: Fixed — Payment step now shows check amount (without processing fees) when user selects Pay by Check, with a savings callout showing the difference

### PL-015: Where is the Refund Policy waiver in Team Registration?
- **Area**: Waivers
- **What I did**: Went through the entire Team Registration flow
- **What I expected**: A Refund Policy waiver to appear somewhere in the process
- **What happened**: No Refund Policy waiver shown — where should it be?
- **Severity**: Question
- **Status**: Fixed — new Waivers step added between Login and Teams (Login → Waivers → Teams → Payment → Review). "Before getting started" language so the rep agrees to terms before registering any teams. Shows refund policy HTML in a scrollable box, requires checkbox acceptance, calls accept-refund-policy endpoint to record BWaiverSigned3. Step is conditionally visible — skipped entirely when the job has no refund policy configured.

### PL-014: Info icon next to total amount owed does nothing — remove it
- **Area**: Review Summary
- **What I did**: Clicked the info icon to the right of the total amount owed on the review screen
- **What I expected**: A tooltip or popup with details about the amount
- **What happened**: Nothing happens when clicked — icon does nothing. Remove it?
- **Severity**: UX
- **Status**: Won't Fix — old review screen removed in step consolidation

### PL-013: Duplicate "Proceed to Payment" and "Back" buttons on review screen — review all Team screen navigation for consistency
- **Area**: Review Summary
- **What I did**: Looked at the "You're All Set" (review) screen
- **What I expected**: One set of navigation buttons, consistent across all Team Registration screens
- **What happened**: "Proceed to Payment" appears in two places on this screen, and so does "Back." Need to review navigation across all Team Registration screens and make it consistent.
- **Severity**: UX
- **Status**: Won't Fix — old multi-step review screen removed; single Proceed to Payment button now on Teams step

### PL-012: Change "You're All Set!" heading to "Review Your Teams"
- **Area**: Review Summary
- **What I did**: Reached the review screen before payment
- **What I expected**: A heading that tells you to review your selections
- **What happened**: Heading says "You're All Set!" — should say "Review Your Teams" instead
- **Severity**: UX
- **Status**: Won't Fix — old review screen removed in step consolidation

### PL-011: "Proceed to Payment" button showing on Team Library and Select Teams — should only be on Review screen
- **Area**: Team Selection
- **What I did**: Noticed "Proceed to Payment" button on both the Team Library and Select Teams screens
- **What I expected**: That button to only appear on the Review screen, not earlier in the flow
- **What happened**: "Proceed to Payment" shows up too early — shouldn't it only be on the Review screen?
- **Severity**: Question
- **Status**: Won't Fix — Library and Select merged into single Teams step; Proceed to Payment appears once on that step

### PL-010: Add Level of Play (LOP) button after teams at all levels in Library and Select Teams
- **Area**: Team Selection
- **What I did**: Looked at the Teams Library and Select Teams screens
- **What I expected**: A Level of Play (LOP) button displayed after teams at every level
- **What happened**: No LOP button shown — add it at all levels in both the Teams Library and the Select Teams screen
- **Severity**: UX
- **Status**: Won't Fix — not necessary in the library list; club reps know their teams. LOP is shown in the registered teams grid after assignment.

### PL-009: Select Teams — use checkboxes, remove tap/popup, remove unnecessary info
- **Area**: Team Selection
- **What I did**: Looked at the Select Teams screen
- **What I expected**: Simple checkbox next to each team name to select it
- **What happened**: Uses a tap interaction with an info popup. Recommend: (1) Add a checkbox to the left of each team name instead of the tap, (2) Remove the tap/popup, (3) Remove grad year (already selected), money, and spots remaining from the display — not needed here
- **Severity**: UX
- **Status**: Won't Fix — old Select Teams screen removed; teams now register directly from library via Register button

### PL-008: Teams Library — indent team rows and compress row height
- **Area**: Team Selection
- **What I did**: Looked at the Teams Library list
- **What I expected**: Compact, indented team rows so more teams are visible on screen
- **What happened**: Team rows are not indented and take up too much vertical space — indent the team lines and compress the row height
- **Severity**: UX
- **Status**: Fixed — already addressed in step consolidation

### PL-007: Add cell phone carrier field to Create Club Rep Account for text messaging
- **Area**: Club Rep Login
- **What I did**: Looked at the Create Club Rep Account form fields
- **What I expected**: A cell phone carrier/provider dropdown (for text messaging), like Legacy and Player Registration have
- **What happened**: No cell phone carrier field on the Create Club Rep Account form
- **Severity**: UX
- **Status**: Won't Fix — carriers no longer allow sending SMS via email gateway (nnnnnnnnnn@carrierdomain). The field is obsolete in Legacy too.

### PL-006: Change team library confirmation text
- **Area**: Club Rep Login
- **What I did**: Read the text above the team list
- **What I expected**: Clear instruction about what to do on this screen vs the next
- **What happened**: Text needs to say: "Confirm every team you plan to register is listed below (you will select which ones to register on the next screen)"
- **Severity**: UX
- **Status**: Fixed — coach card text updated to reinforce library value prop and instruct

### PL-005: Remove or change the word "permanently" in the team library description
- **Area**: Club Rep Login
- **What I did**: Read the team library description text on the Create Club Rep Account screen
- **What I expected**: Wording that doesn't imply something can never be changed
- **What happened**: Text uses the word "permanently" — should be removed or reworded
- **Severity**: UX
- **Status**: Fixed — "permanently" removed; replaced with "carries across all TeamSportsInfo events"

### PL-004: Create Club Rep Account — move Username/Password to top and add Confirm Password field
- **Area**: Club Rep Login
- **What I did**: Looked at the Create Club Rep Account form field order
- **What I expected**: Username and Password at the top of the form, with a Confirm Password field
- **What happened**: Username and Password are not at the top, and there is no Confirm Password field
- **Severity**: UX
- **Status**: Fixed — credentials moved above personal info (Club Name → Credentials → Personal Info), confirm password field added with mismatch validation

### PL-003: "Similar clubs on file" shows current user as club rep for all options — should show actual club rep
- **Area**: Club Rep Login
- **What I did**: Searched for a club during Create Club Rep Account and saw the "Similar clubs on file or already registered" list
- **What I expected**: Each club option to show its actual club rep info
- **What happened**: All club options list me as the club rep instead of the real club rep for each club
- **Severity**: Bug
- **Status**: Fixed
- **Root Cause**: `ClubRepository.GetSearchCandidatesAsync()` was joining `Clubs.LebUser` (an auditing/last-edited-by field) to get RepName and RepEmail. `LebUserId` records who last modified a record — it is NOT the club rep. The actual club rep relationship is `Clubs → ClubReps → ClubReps.ClubRepUser → AspNetUsers`. Since the person creating the account was also the last editor, LebUserId pointed to themselves for every club they touched.
- **Fix**: Both overloads of `GetSearchCandidatesAsync()` in `ClubRepository.cs` now resolve RepName/RepEmail/State from `c.ClubReps.OrderBy(cr => cr.Aid).Select(cr => cr.ClubRepUser...)` instead of `c.LebUser`.
- **Audit Completed**: Full codebase audit of all `LebUser`/`LebUserId` reads across all repositories. The club search was the only place where `LebUserId` was misused as an ownership field. All other usages correctly treat it as the acting user (who sent, who modified) — which is appropriate for those entities (push notifications, bulletins, age ranges, etc.). The club case is unique because a club's rep is a *separate relationship* (`ClubReps` table), not the last editor.

### PL-002: Club lookup — reword "your club keeps a team library" text
- **Area**: Club Rep Login
- **What I did**: Started creating a Club Rep Account and looked at the club lookup text
- **What I expected**: Clear wording explaining what the team library is
- **What happened**: Text needs rewording — add "across all tournaments administered with TeamSportsInfo" after "your club keeps a team library", then keep the rest as-is
- **Severity**: UX
- **Status**: Fixed — added "across all tournaments administered with TeamSportsInfo"

### PL-001: Team Registration card — match Player Registration card style and update wording
- **Area**: Club Rep Login
- **What I did**: Compared the Team Registration card to the Player Registration card
- **What I expected**: Consistent look and feel between both registration cards
- **What happened**: Three changes needed: (1) Add "Let's Register Your Teams!" as the header on the Team Registration card, (2) On the Player Registration side, change "Family Account" to "Family Account Sign In", (3) Change the lowest button from "Create Club Rep Account" to "Create NEW Club Rep Account"
- **Severity**: UX
- **Status**: Fixed — all three changes applied; player tip restyled to match team wizard-tip

---

## Second Pass Items

*Started 2026-04-20. Numbered independently (SP-001, SP-002, ...).*

### SP-001: Team Library — need option to edit team names
- **Area**: Team Selection
- **What I did**: Looked at the combined library and registration screen (great improvement!)
- **What I expected**: Ability to edit team names directly in the library — teams may need name corrections or updates across tournaments
- **What happened**: No edit option available for team names in the library. Club reps need to be able to rename teams without deleting and re-adding them.
- **Severity**: UX
- **Status**: Fixed — commit `4b721667` added edit/delete/archive for unscheduled library teams. Pencil icon on each library row opens the form modal in edit mode, pre-filled with name/grad year/LOP; save calls PUT `updateClubTeam`. Backend gates edits on `bHasBeenScheduled=false`. Also covers the rename half of SP-005.

### SP-002: Add Team popup — remove Level of Play (LOP) field (redundant, asked again at registration)
- **Area**: Team Selection
- **What I did**: Opened the Add Team popup to add a team to the library
- **What I expected**: Only the fields needed at the library stage — LOP can wait until registration
- **What happened**: LOP is included in the Add Team popup, but it's also asked again when registering the team. Removing LOP here eliminates redundancy and makes the popup much smaller (one line's worth of space saved).
- **Severity**: UX
- **Status**: Fixed — LOP kept on library (enables cross-event team performance analysis), but a `wizard-tip` caption under the field now frames it as an *overall* assessment that can be adjusted per tournament. Also brought the whole modal up to project UI standards (`.field-label` / `.field-input` / `.field-select` / `.wizard-tip` / `.field-error`) and rewrote the Team Name guidance as a single fluent "Instead of entering X, enter Y" sentence (strikethrough + icons dropped).

### SP-003: Add Team and Register popups obscure the screen behind — users need to see that info
- **Area**: Team Selection
- **What I did**: Clicked Add Team and Register buttons during the registration process
- **What I expected**: Popups positioned so the information on the screen behind remains visible — club reps need to reference that data while making selections
- **What happened**: Popups block the view of the screens behind them. Consider: (1) making popups smaller/more compact, (2) positioning them to the side or bottom so the main content stays visible, or (3) using a slide-in panel instead of a centered modal.
- **Severity**: UX
- **Status**: Fixed

### SP-004: Consolidate two Register popups into one — only Grad Year and LOP needed
- **Area**: Team Selection
- **What I did**: Clicked Register on a team and had to go through two separate popups
- **What I expected**: A single popup collecting everything needed to register
- **What happened**: Registration requires two separate popups when only Grad Year and LOP need to be collected. Combine into one popup for a quicker process — fewer clicks, less time.
- **Severity**: UX
- **Status**: Fixed

### SP-011: New Club Rep first-time experience — "No teams registered yet" message assumes teams already exist in library
- **Area**: Team Selection
- **What I did**: Created a NEW Club Rep Account and landed on the Teams screen for the first time with an empty library
- **What I expected**: Guidance to add teams to the library first, then register them
- **What happened**: Screen shows "No teams registered yet" with "Tap Register next to a team below to get started" — but a brand new club rep has no teams in the library yet. The instruction is misleading since there are no teams to tap Register on. Should guide the user to Add Team first, or adjust the empty-state messaging to account for a fresh library.
- **Severity**: UX
- **Status**: Open
- **Note**: Also change the instruction text from "Tap Register next to a team below to get started" to "Tap Register next to a team below to register for this event."

### SP-013: "Proceed to Payment" button — clarify if this locks team spots; rename to "Submit Teams and Proceed to Payment"
- **Area**: Team Selection
- **What I did**: Looked at the "Proceed to Payment" button on the Teams screen
- **What I expected**: Clear indication of what happens when clicking — specifically, is this when teams claim a spot against max team capacity?
- **What happened**: Button just says "Proceed to Payment" — unclear whether clicking it submits/locks the team registrations. If this is the point where teams hold a spot in max teams, rename to "Submit Teams and Proceed to Payment" so club reps understand their teams are being committed, not just moving to a payment form.
- **Severity**: Question
- **Status**: Fixed — dynamic, pluralization-aware label driven by count of teams with `paidTotal === 0`. 0 new → "Proceed to Payment"; 1 new → "Submit the 1 new team and Proceed to Payment"; N new → "Submit the N new teams and Proceed to Payment". Spot-claim semantics clarified via investigation: team registrations claim capacity immediately on Register-click (pre-submit), waitlist is always in effect (`BUseWaitlists` flag is player-reg only), and the WAITLIST agegroup/division mirror is find-or-create on demand — no admin pre-config required.

### SP-012: Team Library section needs a heading — match "Registered" heading style
- **Area**: Team Selection
- **What I did**: Looked at the Teams screen with the library and registered sections
- **What I expected**: A clear heading above the library section so it's visually distinct from the Registered section
- **What happened**: The Registered section has a heading but the Library section has none. Add a "LIBRARY" (or similar) heading in blue caps, same font and style as the "REGISTERED" heading, so both sections are clearly labeled.
- **Severity**: UX
- **Status**: Fixed

### SP-010: Terms of Service popup on Create NEW Club Rep Account — too narrow; standardize width across all users
- **Area**: Club Rep Login
- **What I did**: Created a NEW Club Rep Account and the Terms of Service popup appeared
- **What I expected**: A comfortably wide popup matching what other registration flows show
- **What happened**: The ToS popup seems narrower than what I've seen on other screens (e.g., Family Account creation). Make it wider so the text is easier to read, and standardize the popup width for ToS across all user types (Family, Club Rep, etc.).
- **Severity**: UX
- **Status**: Fixed

### SP-009: Review Team Registration confirmation screen — blocked until SP-008 payment error is resolved
- **Area**: Confirmation
- **What I did**: Attempted to complete a Team Registration payment to review the confirmation screen
- **What I expected**: To see the confirmation page and email after a successful payment
- **What happened**: Blocked by SP-008 ("One or more teams not found" payment error). Once SP-008 is fixed, need to review the confirmation screen for content, layout, and email — same level of scrutiny as PlayerRegistration confirmation.
- **Severity**: UX
- **Status**: Fixed
- **Note**: Dependent on SP-008. Cannot test until payment succeeds.

### SP-008: Payment Error "One or more teams not found" when submitting CC payment to Sandbox
- **Area**: Payment
- **What I did**: Registered 2 teams (2030 Test Blue and 2031 Blue), reached the Payment screen showing $828.00 Balance Due (2 × $400 fee + 2 × $14 processing fee). Filled in CC info and clicked "Pay $828.00 Now" to submit payment to Sandbox.
- **What I expected**: Payment to process successfully through Sandbox
- **What happened**: Payment Error displayed: "One or more teams not found". Payment could not be completed. Screenshot shows the error banner at top of the Payment screen with the summary table and CC form below it.
- **Severity**: Bug
- **Status**: Deferred
- **Note**: Both teams show correctly in the summary grid with fees, Proc Fee, CC Owed, and Reg Date. The error appears to be a backend lookup failure when the payment controller tries to resolve the team registrations.

### SP-007: Payment screen header — rename "Payment" to "Complete Payment", center and bold for consistency
- **Area**: Payment
- **What I did**: Compared the Team Registration Payment screen header to the Player Registration Payment screen
- **What I expected**: Consistent header styling across both registration flows
- **What happened**: Team Payment screen header says "Payment" — should say "Complete Payment" to match the Player Registration flow. Also needs to be centered and bolder for visual consistency.
- **Severity**: UX
- **Status**: Fixed — keeping "Payment" title (neutral in both paid and unpaid states; "Complete Payment" reads wrong when balance is already $0). Styling half (centered + bolder) can still apply if Player/Team diverge visually.

### SP-006: Accounting Tables — review columns across both screens; LOP needs its own column
- **Area**: Review Summary
- **What I did**: Compared the two Accounting Tables (Teams step and Payment step) during Team Registration
- **What I expected**: Consistent, informative columns across both tables — the first table has useful data that should carry forward to the second
- **What happened**: The first accounting table has a lot of the info that should also appear on the payment screen. Additionally, LOP needs its own dedicated column instead of being embedded elsewhere. Challenge: lots of data to fit in a narrow field — may need layout adjustments (wider table, smaller font, horizontal scroll, or abbreviated headers). Needs collaborative review with Todd.
- **Severity**: UX
- **Status**: Deferred
- **Note**: Related to PlayerRegistration SP-024 (similar accounting table column/label rework). Consider standardizing table layout approach across both Player and Team registration.

### SP-005: Team Library — what happens to aged-out grad year teams? + team name editing (follow-up to PL-019)
- **Area**: Team Selection
- **What I did**: Reviewed the Team Library and considered the lifecycle of teams whose grad year has passed
- **What I expected**: A plan for handling teams that are no longer active (e.g., grad year 2025 teams in 2026) — archive, hide, or allow deletion. Also, the ability to edit team names (reinforcing SP-001).
- **What happened**: No visible mechanism to handle aged-out teams. The library will grow indefinitely with stale teams that no longer play. Questions: (1) Should aged-out teams be automatically hidden or archived? (2) Should club reps be able to manually archive/remove them? (3) Team name editing is still needed (see SP-001) — club reps must be able to rename teams without deleting and re-creating them.
- **Severity**: Question
- **Status**: Fixed
