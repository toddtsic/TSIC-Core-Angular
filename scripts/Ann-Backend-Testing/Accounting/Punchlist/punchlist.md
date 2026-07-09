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

### PL-033: Submit Registration success toast — extend duration, and add a pay-by-check variant ("Active" + send check to keep status)
- **Refs**: PlayerRegistration SP-042 (Submit Registration green success toast, Legacy wording, 10s), SP-049 (pay-by-check lands BActive=true at submit), SP-047/SP-048 (Pay by Check flow)
- **Area**: Player Registration / Submit Registration / Payment Processing
- **Where**: Player Registration → Submit Registration → green success toast ("…players not paid in full are marked INACTIVE…")
- **What I did**: Submitted a registration and read the green confirmation toast about unpaid players being Inactive until paid
- **What I expected**: (1) The toast to stay on screen longer so parents can actually read it. (2) On sites that allow **Pay by Check**, different wording — those players are **Active** on submit, and the toast should tell the parent to **send the check to keep their Active status**.
- **What happened**: (1) The toast dismisses too quickly. (2) The same "Inactive until paid" wording shows even for pay-by-check sites, which is incorrect — pay-by-check players are Active on submit (per SP-049).
- **Severity**: UX
- **Status**: Open
- **Note**: Toast lives in `player.component.ts` (SP-042 set it to 10s with Legacy verbatim copy). Two changes: bump the duration, and branch the message on whether the site/flow allows Pay by Check — pay-by-check variant should reflect Active-on-submit (SP-049) and instruct the parent to mail the check to retain Active status.
- **Note (ISP)**: For ISP, the pay-by-check "Active — mail your check to keep Active status" message should also appear as **RED text near the Pay by Check option on the payment screen** (not just in the toast), so the parent sees it at the point of choosing Pay by Check.

### PL-032: Cancel Subscription on a newly-created Sandbox subscription doesn't work from the Player Accounting menu
- **Refs**: PlayerRegistration PL-045 (ARB subscription per player), SP-033 (ARB installment details), ADN result normalization
- **Area**: Player Accounting / ARB (Automated Recurring Billing) / Payment Processing
- **Where**: Player Details popup → Player Accounting menu → **Cancel Subscription**, against a subscription just created in the ADN **Sandbox**
- **What I did**: Created a subscription in Sandbox, then tried to Cancel Subscription from the Player Accounting menu in the Player Details popup
- **What I expected**: Clarification first — should Cancel Subscription work against a just-created Sandbox subscription at all?
- **What happened**: Cancel Subscription doesn't work for the freshly-created Sandbox subscription
- **Severity**: Question
- **Status**: Fixed
- **Note**: Open question for Todd: is this a real defect or expected Sandbox/ADN timing behavior? ADN subscriptions may not be immediately cancelable right after creation (status/settlement timing), and Sandbox can behave differently from Production. Confirm the intended behavior, then decide whether the UI should (a) succeed, (b) show a clear "not yet cancelable" message, or (c) is fine as-is for Sandbox only.

### PL-031: Player Details — Fee-Adj column info (i) icon has no information attached
- **Refs**: PlayerRegistration SP-024 (Fee-Adj info tooltip on the payment screen accounting table), PL-020 (Late Fee in Fee-Adj should render red)
- **Area**: Player Accounting / Admin Player Details
- **Where**: Player Details → list of players card → **Fee-Adj** column header info (i) icon
- **What I did**: Hovered/clicked the info (i) icon on the Fee-Adj column in the Player Details players list
- **What I expected**: A tooltip/popover explaining the Fee-Adj column (e.g., early-bird discount as a negative value or late fee as a positive value, only one applies)
- **What happened**: The (i) icon shows no information — nothing is attached to it
- **Severity**: Bug
- **Status**: Fixed
- **Note**: The payment-screen Fee-Adj tooltip copy already exists from PlayerRegistration SP-024 ("Depending on when you registered, this may show an early-bird discount (negative value) or a late fee (positive value). Only one applies."). Wire the same message to the Player Details Fee-Adj icon, or remove the icon if no copy is intended here.

### PL-030: Payment screen — Payment Method choice (Credit Card vs Pay by Check) isn't obvious; surface as radio buttons above Registration Insurance
- **Refs**: SP-047 (UM Deposit vs PIF choice at payment), SP-048 (Pay by Check instructions/payee), PL-008 (Player vs Team Payment screen alignment)
- **Area**: Payment Processing / Payment Method selection
- **Where**: Payment screen — Payment Method options (Credit Card vs Pay by Check). Working in ISP:2026-2027.
- **What I did**: On the payment screen, looked for how to choose between paying by Credit Card and paying by Check
- **What I expected**: An obvious, up-front payment-method choice
- **What happened**: The Credit Card / Pay by Check options aren't obvious. Suggestion: place the choice **above Registration Insurance** as two radio buttons — **Pay by Credit Card** (selected by default) with **Pay by Check** below it — so the method selection is clear before the rest of the payment details.
- **Severity**: UX
- **Status**: Done — no longer an issue. No code fix.

### PL-029: Refund of base fee only (no processing fees) — re-verify Check Owed and CC Owed on Admin Player Accounting popup
- **Refs**: PL-021 (Payment Ledger void/refund row + ADN refund tx), PL-014 (ledger must indicate target registration), PL-002 (Check Owed double-subtracts deposit proc fees)
- **Area**: Player Accounting / Refunds & Adjustments / Payment Processing
- **Where**: Admin view → Player Details popup → Accounting menu → **Check Owed** and **CC Owed** columns, after a refund of the base fee amount (processing fees NOT included in the refund)
- **What I did**: Worked YJS Camps and Clinics — issued a refund for the base fee amount only (excluding processing fees) and reviewed the Check Owed / CC Owed figures on the Admin Player Accounting popup
- **What I expected**: Check Owed and CC Owed to recompute correctly after a base-fee-only refund (processing fees handled separately/retained)
- **What happened**: Suspected an error in the resulting Check/CC owed amounts — needs re-review. Re-verify the accounting math on the Admin Player Accounting popup for a base-fee-only refund and confirm Check Owed and CC Owed are correct.
- **Severity**: Bug
- **Status**: Open
- **Note**: Ann flagged a possible error worked through on YJS Camps and Clinics. Confirm how a base-fee-only refund (proc fees excluded) should flow into Check Owed vs CC Owed before changing anything.

### PL-028: STEPS Girls — 50% discount code produces fractional cents in the confirmation email "total amount due"
- **Refs**: SP-025 (Girls100 splits across players), PL-025 (UM -$0.88 DC/PF rounding), PL-017 (player-side DC success wording)
- **Area**: Discount Codes / Payment Processing / Confirmation Email
- **Where**: STEPS Girls — confirmation email, "total amount due" line, after applying a 50% discount code to a player
- **What I did**: Applied a 50% discount code to a STEPS Girls player and reviewed the confirmation email
- **What I expected**: The total amount due to display as a clean currency value (two decimal places)
- **What happened**: Fractions of a cent appeared in the "total amount due" on the confirmation email (the 50% split produced a sub-cent remainder). Look at rounding the displayed total to whole cents for this purpose.
- **Severity**: Bug
- **Status**: Won't Fix — could not reproduce, and a rare occurrence. Closed without a code change.
- **Note**: Confirm whether this is a display-only formatting issue on the email token or an actual stored sub-cent amount. If the underlying value carries fractional cents, decide where to round (at calc time vs. at render time) so the ledger and the email agree.

### PL-027: UM:Maryland Lacrosse Camps Summer 2026 — team-option amount in balance-due phase should show the camp TOTAL, not the deposit-phase figure
- **Refs**: PL-024 (UM Balance Due Payment Phase "Job Default"), PlayerRegistration SP-045 (UM Summer Camps Assign Teams dropdown amount wrong), SP-047 (UM Deposit vs PIF choice)
- **Area**: Payment Processing / Assign Teams / Deposit vs Balance-Due
- **Where**: UM:Maryland Lacrosse Camps Summer 2026 — team options dropdown (the "(${amount})" suffix on each camp option)
- **What I did**: Looked at the amount shown in parentheses on the team/camp options for this deposit + balance-due site, in both payment phases
- **What I expected**: Deposit phase — current behavior is fine, leave as is. Balance-due phase — the parenthetical amount should show only the **total amount for the camp**.
- **What happened**: In balance-due phase the option amount isn't showing the camp total. When the site is in balance-due phase, the "(${amount})" should reflect the full camp total rather than the deposit-phase figure.
- **Severity**: UX
- **Status**: Fixed

### PL-026: Payment screen — Discount Code entry field should be wider / more visible
- **Refs**: PL-013 (auto-scroll to top after DC apply), PL-005 (admin-side DC dollar-spinner), PlayerRegistration PL-034 (DC section emphasis + white input), SP-052 (player-side DC wording)
- **Area**: Payment Processing / Discount Codes
- **Where**: Payment screen (Player and Team flows) — Discount Code "Enter Code" input field
- **What I did**: Looked at the Discount Code entry field on the payment screen
- **What I expected**: The input to be prominent and easy to find — clearly a data-entry field the parent/rep is meant to use
- **What happened**: The field is too small / not visible enough. Make it wider or otherwise more visible so it stands out on the payment screen.
- **Severity**: UX
- **Status**: Fixed — Todd directed the sizing. Input and Apply button now share one box formula (`padding-block: var(--space-2)`, `--font-size-base`, `line-height: 1.5`), taking both from ~31px to ~42px. Applied to **both** Player and Team payment screens (`registration/player/steps/payment-step.component.ts`, `registration/team/steps/payment-step.component.ts`). Apply button dropped `btn-sm`.
- **Note**: Pairs with PL-013 — once the code is applied, the screen should also auto-scroll back to the top of the Accounting table (already tracked separately).

### PL-025: UM Summer Camp 2026 — players showing -$0.88 balance due; legacy DC included PF, new DC auto-accounts for PF; decide whether to zero-out + brief Chelsea on next year's DC value
- **Refs**: PL-005 (Team Discount Codes dollar-spinner), PL-011 (Early Bird Discount label), PL-013 (auto-scroll on DC apply), SP-025 (Girls100 splits across players)
- **Area**: Discount Codes / Payment Processing / Data Cleanup
- **Where**: UM Summer Camp 2026 — Player accounts with **-$0.88** balance due (credit) after the migrated Discount Code apply
- **What I did**: Reviewed UM Summer Camp 2026 player balances and found a number of players sitting at -$0.88
- **What I expected**: Zero balance due for those accounts
- **What happened**: Root cause confirmed — the **Legacy** Discount Code was authored as **$25.90** to manually fold in the processing fee (`$25 + $0.90 PF`). The **new** system computes the PF automatically on top of the discount, so the migrated $25.90 DC ends up over-discounting by the PF portion, leaving each affected player at **-$0.88** credit. Two open items:
  1. **Data cleanup decision** — do we **zero-out** these credits even though they're not currently in use? Cathy will see the data and is likely to flag it. Recommend zeroing so the ledger is clean and Cathy doesn't have to ask later, but Todd's call.
  2. **Forward-looking fix** — **brief Chelsea** to author next year's DC as a flat **$25** (the new engine handles the PF automatically). If she copies the existing $25.90 value forward, the same -$0.88 credit shows up on every 2027 player.
- **Severity**: Bug
- **Status**: Done — the -$0.88 credits are no longer visible. No code fix; the data issue resolved itself.
- **Note**: This is a one-shot data issue tied to the Legacy→new DC migration, not a code defect in the current engine. Once the zero-out call is made and Chelsea has the new-year guidance, this should not recur.

### PL-024: UM:Maryland Lacrosse Camps Summer 2026 — Balance Due Payment Phase shows "Job Default"; explain what this means and where it's set (for discussion with Todd)
- **Refs**: PL-006 (Late Fee scope/single date), PL-019 (ARB Payment Plan split), TeamRegistration FP-005 (Deposit Phase confirmation copy)
- **Area**: Payment Processing / Payment Phase configuration
- **Where**: UM:Maryland Lacrosse Camps Summer 2026 → Balance Due **Payment Phase** indicator/label reading **"Job Default"**
- **What I did**: Looked at the Payment Phase shown on the Balance Due flow for the UM:Maryland Lacrosse Camps Summer 2026 site
- **What I expected**: A self-explanatory phase label (e.g., Deposit Only, PIF, ARB) and a clear pointer to where the value comes from
- **What happened**: Phase reads as **"Job Default"** — not obvious what that means without poking around, and not obvious where in Job Settings it's configured. Need to (a) explain the semantics of "Job Default" as a phase value, (b) document where it's set so a Director can audit/change it, and (c) decide whether the user-facing label should read something more descriptive than "Job Default".
- **Severity**: Question
- **Status**: Fixed
- **Note**: Discussion item for Todd. If "Job Default" is the fallback phase used when no phase override is active, the label should probably reflect the resolved phase (e.g., "Default — PIF") rather than the literal config key.

### PL-023: Age Group Details popup — full text/copy pass across Player Fees and Club Rep / Team Fees sections
- **Refs**: PL-004 (EBD / Late Fee "apply to all age groups" checkbox), PL-006 (Late Fee behavior), PL-011 (Early Bird → "Early Bird Discount" label), PL-015 (cross-level guidance), PL-016 ("Most-specific wins" wording)
- **Area**: Job Settings / LADT Editor — Age Group Details
- **Where**: LADT Editor → **Age Group Details** popup, both the **Player Fees** section and the **Club Rep / Team Fees** section
- **What I did**: Read the labels, helper text, and inline notes on both sections of the Age Group Details popup
- **What I expected**: Wording the Director can read once and act on confidently
- **What happened**: Several lines are unclear — either jargon-heavy, ambiguous, or written in a way that won't land with non-technical Directors. Needs a full copy pass across both sections together so the wording is consistent and the precedence/scope behavior is obvious. Let's review this together with the popup open and edit line-by-line — pair with PL-016 (replace "Most-specific wins") and PL-015 (cross-level guidance text) so the final copy is one coherent voice.
- **Severity**: UX
- **Status**: Fixed — closed on Todd's call. The paired copy work landed under PL-015 (cross-level guidance) and PL-016 (removed the "Most-specific wins (never stacked)" sentence from the fee-card hints). No separate line-by-line pass over the Age Group Details popup was performed.

### PL-022: Self-rostered players with $0 owed — hide the $0 row and skip the Payment screen straight to Confirmation
- **Refs**: PL-008 (Player vs Team Payment screen alignment), PL-019 (ARB Payment Plan split), PlayerRegistration SP-024 (Accounting Table on Complete Payment)
- **Area**: Player Accounting / Tournament Player Registration flow
- **Where**: Self-rostered Player Registration on a tournament — Submit Registrations → Payment screen → Confirmation
- **What I did**: Walked a self-rostered player through Tournament Player Registration on a site where the self-rostered player owes nothing
- **What I expected**: Parity with Legacy — Legacy doesn't surface the self-rostered $0 row at all, and a $0 balance shouldn't require a parent to land on a Payment screen just to click through to Confirmation
- **What happened**: Two related cleanups needed:
  1. **Hide the $0 accounting row** for self-rostered players — Legacy doesn't display it; removing it matches Legacy and avoids a confusing "$0 owed" line that adds no value.
  2. **Skip the Payment screen entirely** when a self-rostered player's registration nets $0 — the flow should go **Submit Registrations → Confirmation directly**, no Payment-step interstitial. Pointless step today; just an extra click before the same Confirmation page.
- **Severity**: UX
- **Status**: Won't Fix

### PL-021: Payment Ledger void/refund row — drop the "Reason:" suffix, tighten the void copy, and add a matching line for Refunds with the ADN refund tx number
- **Refs**: PL-014 (Payment Ledger must indicate target registration on Check/Correction/CC rows), TeamRegistration FP-001/FP-005 (Most Recent Transactions Comment cleanup)
- **Area**: Player Accounting / Payment Processing
- **Where**: Player Details popup → Accounting tab → **Payment Ledger** — VOIDED CC rows and Refund rows. Capture attached (VOIDED CC charge of $201.83 with the trailing "Reason: Admin refund from family payment" suffix).
- **What I did**: Looked at a VOIDED CC Payment Ledger row to read what the Admin would see at the moment of reconciling
- **What I expected**: A tight, single-purpose line — what happened, when, the ADN transaction id, amount — without the "Reason:" tail that doesn't add information for the reader
- **What happened**:
  1. **Drop the "Reason:" suffix** — the current trailing "Reason: Admin refund from family payment" doesn't add value (the void was already triggered through the Admin refund flow); strip it from the row text.
  2. **Tighten the void copy in general** — the body reads as one long inline paragraph ("CC was not yet settled at Authorize.Net, so the original $201.83 charge was VOIDED (not refunded). ADN void tx 12008414516."). Shorter, cleaner sentence; lead with the action, drop the explanatory clause unless it changes the Admin's next step.
  3. **Add the same treatment to Refund rows** — when an actual ADN refund (not a void) is issued, the Payment Ledger row should similarly show the refund transaction with the ADN refund tx number. Right now nothing equivalent is rendered; Admin needs a refund tx in the ledger to reconcile against the ADN dashboard.
- **Severity**: UX
- **Status**: Open

### PL-020: Accounting tables — Late Fee adjustments in Fee-Adj should render in red (parity with EBD shown in green)
- **Refs**: PL-004 (EBD / Late Fee "apply to all age groups"), PL-006 (Late Fee behavior), PL-011 (Early Bird → "Early Bird Discount" label), PL-015 (cross-level guidance), PL-016 ("Most-specific wins" wording), PL-008 (Player vs Team Payment screen alignment), PL-009 (Family Players Breakdown column order)
- **Area**: Early Bird / Late Fees / Player + Team Accounting tables
- **Where**: Every Accounting table that exposes a **Fee-Adj** column — must apply to **both** surfaces:
  1. **Registration Payment screen** (Player Registration Complete Payment, Team Registration Payment step — the family-/club-rep-facing view)
  2. **Admin Accounting popup menu** (Player Details popup → Accounting tab, Club Teams Breakdown under the Accounting menu — the Director-facing view)
  Color encoding has to be identical on both so the visual meaning carries across the parent and the Director.
- **What I did**: Looked at the Fee-Adj column when an Early Bird Discount was in effect versus when a Late Fee was in effect
- **What I expected**: Both adjustments to read as visually distinct from base fee, with sign indicating direction — EBD already renders in **green** (good = savings), so Late Fee should render in **red** (bad = surcharge)
- **What happened**: Late Fee values appear in the regular text color while EBD values are green. The asymmetry hides the Late Fee at a glance and makes the Director / parent re-read the column to know which way the adjustment goes. Apply **red** to Late Fee values in Fee-Adj across every Accounting table so the color encodes direction consistently.
- **Severity**: UX
- **Status**: Fixed

### PL-019: ARB Payment Plan selector — split the aggregated "10 payments of $985" into per-player amounts so parents know what to expect per kid
- **Refs**: PlayerRegistration SP-033 (ARB Complete Payment per-player installment details), SP-043 (ARB Payment screen Legacy parity), SP-028 (CAC per-event registration fee), PL-009 (Family Players Breakdown column order)
- **Area**: Payment Processing / ARB (Automated Recurring Billing)
- **Where**: Player Registration → Complete Payment → **PAYMENT PLAN** section → Automated Recurring Billing option (the radio with "10 payments of $X · billing starts <date>"). Capture attached.
- **What I did**: Looked at a family with 2 players on a Payment Plan job — Player 12 ($5,650 / 2032 White) → 10 × $565, Player 13 ($4,200 / 2035 White) → 10 × $420. The Accounting table at the top correctly shows the per-player ARB schedule. The radio under PAYMENT PLAN, however, only shows the **aggregated** "10 payments of $985.00 · billing starts Jun 7, 2026".
- **What I expected**: The Payment Plan radio to **split** the schedule by player so the parent sees the per-kid amounts they'll actually be billed each month — e.g.:
  - "Player 12 — 10 payments of $565.00"
  - "Player 13 — 10 payments of $420.00"
  - (Optionally) Combined: "10 payments of $985.00 total · billing starts Jun 7, 2026"
- **What happened**: The aggregated $985 figure hides the per-player split, so the parent has to mentally reconcile it against the Accounting table above. Split it out (the data is right there) so the radio matches what the parent expects to see on their statement each month.
- **Severity**: UX
- **Status**: Open

### PL-018: Correction full-payment — show a red reminder for the Admin to make the Player Active
- **Refs**: PL-014 (Payment Ledger needs to indicate target registration on Check/Correction/CC rows), PlayerRegistration SP-034 ("Registered - Inactive" in red for unpaid/incomplete), SP-049 (BActive activation rules for pay-by-check)
- **Area**: Player Accounting / Payment Processing
- **Where**: Player Details popup → Accounting → record a **Correction** that pays the player in full
- **What I did**: As Admin, recorded a Correction transaction that brought the player to fully paid
- **What I expected**: A clear nudge so I don't forget to flip the player to Active — Correction full-payments aren't auto-activating the registration today
- **What happened**: No reminder is shown, and it's easy to walk away with the player still Inactive. Add a **red note** on the Accounting view (right after a Correction that zeros out Owed) reminding the Admin to make the Player Active. Wording suggestion: **"Reminder: this Correction paid the player in full — make the Player Active."**
- **Severity**: Bug
- **Status**: Fixed

### PL-017: Player-side Discount Code success text — change "player(s)" to "registration(s)" so CAC multi-registration is covered
- **Refs**: SP-052 (Player-side DC "No discounts were applied" wording), SP-025 ("Girls100" splits across players), PlayerRegistration SP-028 (CAC per-event registration fee), TeamRegistration FP-010 (DC on Payment step)
- **Area**: Discount Code / Complete Payment (Player side)
- **Where**: Player Registration → Complete Payment → Discount Code apply success message
- **What I did**: Applied a Discount Code on the Player side and read the success message
- **What I expected**: Wording that fits every player flow, including CAC where a single player can hold multiple registrations
- **What happened**: The success text reads **"Successfully applied discount to 4 player(s)"**, which is wrong for CAC — one player can have multiple registrations, so the count is really registrations, not players. Change the text to **"Successfully applied discount to 4 registration(s)"** so it's accurate for tournaments, showcases, and CAC alike.
- **Severity**: UX
- **Status**: Fixed — changed "player(s)" → "registration(s)" in PlayerRegistrationPaymentController.cs (ApplyDiscountResponseDto.Message). Backend string; needs API rebuild/redeploy to take effect. "No discounts were applied" branch left for SP-052.

### PL-016: EBD / Late Fee — reword the "Most-specific wins (never stacked)" helper text
- **Refs**: PL-004 (EBD / Late Fee "apply to all age groups" checkbox), PL-006 (Late Fee behavior), PL-011 (Early Bird → "Early Bird Discount" label), PL-015 (cross-level guidance on Age Group vs League editors)
- **Area**: Early Bird / Late Fees
- **Where**: LADT Editor — helper text **"Most-specific wins (never stacked)."** shown under both the Late Fee and the EBD Fee entries
- **What I did**: Read the helper text under the Late Fee and EBD Fee entries
- **What I expected**: Wording a Director can read and immediately understand without needing to decode "most-specific" or "never stacked"
- **What happened**: Current text reads "Most-specific wins (never stacked)." — too jargon-y for the audience. Rewrite in plain English so the Director understands the precedence rule (e.g., when both Age Group and League settings exist, the Age Group value wins for that age group and the values don't combine). Replacement wording to be finalized together — likely paired with PL-015 cross-level guidance so the helper text and the cross-level pointer read as one coherent explanation.
- **Severity**: UX
- **Status**: Fixed — sentence removed outright from the League, Age Group, and Team fee-card hints (12 occurrences). The preceding sentence at each level already states the precedence rule in plain English, so no replacement wording was needed.

### PL-015: Early Bird Discount / Late Fee — add cross-level guidance text on both Age Group and League editors
- **Refs**: PL-004 (Early Bird / Late Fee "apply to all age groups" checkbox), PL-006 (Late Fee behavior), PL-011 (Early Bird → "Early Bird Discount" label)
- **Area**: Early Bird / Late Fees
- **Where**: LADT Editor — Early Bird Discount and Late Fee edit UI at both the **Age Group** level and the **League** level
- **What I did**: Reviewed the EBD / Late Fee edit forms at each level
- **What I expected**: Clear in-context guidance reminding the Director that the same setting can be adjusted at the other level, so they pick the right entry point for what they're trying to do
- **What happened**: Neither level points to the other, so a Director editing one age group at a time doesn't realize they could broadcast from the League level — and a Director editing at League level doesn't realize they can scope to a subset of age groups. Add helper text on each form:
  1. **On the Age Group editor**: a line stating that EBD / Late Fee can also be set at the League level to apply across **all** age groups in one shot.
  2. **On the League editor**: a line stating that EBD / Late Fee can also be set per Age Group to apply to a **selected subset** rather than all of them.
- **Severity**: UX
- **Status**: Fixed

### PL-014: Player Accounting Payment Ledger — Check, Correction, AND CC records must indicate which player registration was adjusted
- **Refs**: PL-010 (Family Account ledger + cross-player linking), PL-009 (Family Players Breakdown column order), TeamRegistration FP-001/FP-005 (transaction Comment cleanup)
- **Area**: Player Accounting / Payment Processing
- **Where**: Player Details popup → Accounting tab → **Payment Ledger** — Check, Correction, **and CC** transaction rows
- **What I did**: Reviewed the Payment Ledger on the Player Accounting popup for a family account with more than one player
- **What I expected**: Every Check, Correction, **and CC** row to clearly label **which player registration** the transaction applied to — Admin needs to scan the ledger and know at a glance who each row touched
- **What happened**: Check, Correction, and CC records don't show the target player registration, so on family accounts with multiple players the Admin can't tell from the ledger which kid each line affects. Add the player registration identifier (player name + event, or equivalent) to **all three** transaction types so the ledger is self-describing across every payment method.
- **Severity**: Bug
- **Status**: Fixed

### PL-013: Payment screen — after Discount Code applied, scroll to top so the updated Accounting table is in view
- **Refs**: TeamRegistration FP-010 (Payment columns refresh after DC apply), SP-052 (Player-side DC message), PL-005 (DC input)
- **Area**: Payment Processing / Discount Codes
- **Where**: Payment screen (both Player and Team flows) — Discount Code apply action
- **What I did**: Applied a Discount Code on the Payment screen and watched what the screen does
- **What I expected**: The view to **scroll back to the top of the Accounting table** so the user can immediately see how the discount changed the columns (Owed, Proc Fee, CC Owed, Check Owed, etc.) without having to manually scroll
- **What happened**: The view stays where it is (typically near the DC input / CC form), leaving the Accounting table out of sight at the moment the numbers actually changed. After a successful apply, auto-scroll to the top of the Accounting table so the change is the first thing the user sees.
- **Severity**: UX
- **Status**: Open

### PL-012: Family Players Breakdown — drop redundant "Owed" heading, kill horizontal scroll (widen popup + narrow columns)
- **Refs**: PL-009 (Family Players Breakdown column order: Total Fee → Proc Fee → Paid → Owes), PL-007 (Club Teams Breakdown + Team Payment horizontal-scroll consolidation), PL-010 (Family Account ledger improvements)
- **Area**: Player Accounting
- **Where**: Player Details popup → Accounting tab → **Family Players Breakdown**
- **What I did**: Reviewed the Family Players Breakdown table in the Player Details popup
- **What I expected**: A clean table that fits in the popup at a glance, no duplicate column labels and no horizontal scrolling
- **What happened**: A few cleanups needed — let's walk through them together:
  1. **Drop the "Owed" heading** — there's already an Owed/Owes column at the end of the row (per PL-009 ordering), so a separate "Owed" heading above is redundant.
  2. **Kill the horizontal scroll** by widening the popup a bit and tightening the columns. Same play we're using on PL-007 (Club Teams Breakdown / Team Payment) — coordinate the column-width tuning so the popup and the standalone grids land consistently.
- **Severity**: UX
- **Status**: Done — no longer an issue. No code fix.

### PL-011: Early Bird label should read "Early Bird Discount" so the Admin enters a discount amount, not a replacement fee
- **Refs**: PL-004 (Early Bird / Late Fee broadcast checkbox), PL-005 (dollar-amount spinner), PL-006 (Late Fee behavior)
- **Area**: Early Bird / Late Fees
- **Where**: LADT Editor / Age Group Details — Early Bird fee field
- **What I did**: Reviewed the Early Bird field as it would read to a Director entering a value
- **What I expected**: Wording that makes the input unambiguous — the Admin is entering the **discount amount**, not a new overall fee
- **What happened**: Field is labeled just "Early Bird", which leaves it ambiguous whether the value typed in is a discount or a replacement fee. Rename the label to **"Early Bird Discount"** so the intent is explicit on the field itself.
- **Severity**: UX
- **Status**: Fixed — verified all user-facing labels already read "Early Bird Discount" (grid headers, fee-card edit label/add button/validation msg). No code change needed; likely landed with an earlier LADT copy pass.

### PL-010: Admin Payment Ledger for Family Accounts — promote the "All Family Players" Info button + add direct link to the other player's account for cross-player payments
- **Refs**: PL-009 (Family Players Breakdown column order), PL-008 (Player vs Team Payment screen alignment)
- **Area**: Player Accounting / Payment Processing
- **Where**: Admin → Player Details popup → Accounting tab → Payment Ledger / **All Family Players** section. Scenario: a Family Account with 2 players, both with their own accounting records, Admin recording payment for one and needing to act on the other.
- **What I did**: As Admin on the Player Details popup, looked at the Accounting view for a family with 2 players in process-status accounting records. Saw the Info button at the bottom of the All Family Players section.
- **What I expected**: (1) The Info button to be visually prominent so the Admin doesn't miss the cross-player context, with clearer language about what it shows. (2) A direct, one-click way to jump into the **other** player's account from here so the Admin can record a payment for them without backing out, searching, and re-opening the popup.
- **What happened**:
  1. **Info button**: too small / not prominent enough, and the current label/text isn't doing the job of telling the Admin what's there or why to click it. Make it stand out (size, color, position) and reword so it reads as a clear next action.
  2. **Cross-player linking (more important)**: no direct link from this player's accounting view to the sibling player's account. Add per-player links (or a row-level "Open accounting" button) on the All Family Players list so the Admin can pivot into the other player's accounting and take a payment in the same flow — eliminates the search-and-reopen round-trip.
- **Severity**: UX
- **Status**: Fixed

### PL-009: Player Details popup — Family Players Breakdown column order should be Total Fee, Proc Fee, Paid, Owes
- **Refs**: PL-007 (Club Teams Breakdown column consolidation), PL-008 (Player vs Team Payment screen alignment), PlayerRegistration SP-024
- **Area**: Player Accounting
- **Where**: Player Details popup → Accounting Table → **Family Players Breakdown**
- **What I did**: Reviewed the Family Players Breakdown table on the Player Details popup
- **What I expected**: Columns to read left-to-right in the order a Director scans them: full fee first, then the proc surcharge, then what's been collected, then what's still due
- **What happened**: Current column order is out of sync. Reorder to: **Total Fee → Proc Fee → Paid → Owes**.
- **Severity**: UX
- **Status**: Deferred.

### PL-008: Align Player Payment screen with Team Payment screen — drop the top "Complete Payment" + tips, rename first-card header from ACCOUNTING to PAYMENT, harmonize headers/column order
- **Refs**: PL-007 (Club Teams Breakdown + Team Payment column consolidation), TeamRegistration TP-004/SP-006, PlayerRegistration SP-024 (accounting-table layout)
- **Area**: Payment Processing / Player Accounting
- **Where**: Player Registration → Complete Payment screen, compared side-by-side with Team Registration → Payment step
- **What I did**: Walked both Payment screens back-to-back to compare layout and headings
- **What I expected**: A consistent Payment-screen presentation across Player and Team flows
- **What happened**: Three changes needed on the Player Payment screen so it lines up with Team:
  1. **Remove** the "Complete Payment" title at the very top of the screen along with the tips block underneath it — Team Payment doesn't carry this, and it duplicates the card header below.
  2. **Rename** the first card's header from **ACCOUNTING** to **PAYMENT** so both flows label the same card the same way.
  3. **Harmonize headers + column order** with the Team Payment screen. Let's pick this up together with a Team Payment screen open beside the Player Payment screen — pair this with PL-007 so the column consolidation lands consistently across all three views (Player Payment, Team Payment, Club Teams Breakdown).
- **Severity**: UX
- **Status**: Done

### PL-007: Club Teams Breakdown + Team Registration Payment screen — consolidate columns to kill horizontal scroll
- **Refs**: PL-003 (Club Teams Breakdown Check Owed column), TeamRegistration TP-004 (Payment screen at-a-glance acceptance-critical), TeamRegistration SP-006 (accounting-column rework)
- **Area**: Team Accounting
- **Where**: Search Registrations → single Club Rep → **Accounting menu → Club Teams Breakdown**; and **Team Registration → Payment step** (shared accounting grid)
- **What I did**: Reviewed the Club Teams Breakdown screen under the Accounting menu — it carries more info than the Payment screen but the columns spill, forcing horizontal scroll. Compared that against the Payment screen, which has the same scrolling problem.
- **What I expected**: All accounting columns visible at a glance, no horizontal scroll — Directors and Club Reps both need to read the financial picture in one look
- **What happened**: Both screens horizontally scroll. We can trim a few columns and consolidate others so the essential info fits. Let's work this through together — pick which columns are must-keep, which can collapse into a combined "Owed / Paid" presentation, and which can move to a hover/detail row.
- **Severity**: UX
- **Status**: Fixed
- **Note**: Coordinate with PL-003 (add Check Owed Total) and TeamRegistration TP-004/SP-006 so the column set lands consistently on both the Club Teams Breakdown view and the shared registered-teams-grid used on the Payment step.

### PL-006: Late Fee — apply to balance-due teams (not just new registrations), single date, and add a reset-to-regular button on removal
- **Refs**: PL-004 (LADT Editor Early Bird / Late Fee broadcast checkbox)
- **Area**: Early Bird / Late Fees
- **What I did**: Looked at how the Late Fee is applied across team/club rep accounting
- **What I expected**: Late Fee to be effective on existing teams in a balance-due state (since those are exactly the teams the Director wants to nudge with a late fee), with accounting records reflecting the change as soon as it's applied
- **What happened**: Late Fee currently only applies to a newly registered team — it doesn't reach teams that are already registered but sitting in balance-due. Needed changes:
  1. **Scope**: Late Fee should apply at the team/club rep level to teams in a balance-due state, not only to brand-new registrations.
  2. **Accounting sync**: When a Late Fee is added, accounting records must update to reflect the new charge (owed totals, CC/Check owed columns, etc.).
  3. **Single date**: Late Fee should only require one date to enter (the effective-from date) — multiple-date entry feels unnecessary here.
  4. **Reset on remove**: When a Late Fee is removed, provide a "Reset to regular fee charges" button so the Director can cleanly back out the late-fee adjustment on affected accounting records.
- **Severity**: UX
- **Status**: Fixed

### PL-005: Team Discount Codes — dollar-amount spinner steps by $0.01; change to $1 (or drop spinner arrows entirely)
- **Area**: Discount Codes
- **Where**: Accounting → Team Discount Codes → dollar-amount field (also relevant: Early Bird Fee / Late Fee dollar entries)
- **What I did**: Used the up/down arrows on the dollar-amount input when entering a Team Discount Code
- **What I expected**: Increments that match how Directors actually set discounts — whole dollars at a minimum
- **What happened**: The arrows step by 1 cent at a time, which is far too granular. Change the dollar-amount step to **$1**. Percent fields are fine as-is.
- **Discussion**: Consider removing the up/down arrows entirely on these dollar fields — direct keyboard entry is what makes sense here. Same reasoning applies to Early Bird Fee and Late Fee dollar entries; arrows aren't needed in any of these spots.
- **Severity**: UX
- **Status**: Fixed — dollar-amount codes now step by $1 in both the Add/Edit Code and Bulk Add popups (`code-form-modal`, `bulk-code-modal`); percent fields keep their $0.01 step. Early Bird / Late Fee dollar entries were already on `step="1"`. Arrows retained per Todd — not removed.

### PL-004: LADT Editor — Age Group Details Early Bird / Late Fee needs an "apply to all age groups" checkbox
- **Area**: Early Bird / Late Fees
- **Where**: LADT Editor → Age Group Details popup → Early Bird Fee / Late Fee section (both Player Fees and Team Fees, every age group)
- **What I did**: In the LADT Editor, opened the Age Group Details popup and looked at the Early Bird / Late Fee setup on the expanded card for Player Fees and Team Fees
- **What I expected**: A way to apply the Early Bird / Late Fee setting to all age groups at once
- **What happened**: Each age group has to be edited individually — a real pain for the Director when the fee is the same across the board. Add a checkbox (consider red text to make it visible) inside the expanded card that says **"Add this Early Bird Fee / Late Fee to all age groups"** for both Player Fees and Team Fees, so the Director can broadcast the value with a single click.
- **Severity**: UX
- **Status**: Fixed

### PL-003: Club Teams Breakdown (Search Registrations → single Club Rep → Accounting menu) should show a Check Owed Total column
- **Area**: Team Accounting
- **Where**: Search Registrations → look up a single Club Rep → Accounting menu → Club Teams Breakdown
- **What I did**: Under Search Registrations, looked up a single Club Rep, then chose the Accounting menu and opened Club Teams Breakdown
- **What I expected**: A Check Owed Total column in the breakdown
- **What happened**: The Club Teams Breakdown does not show a Check Owed Total column — add it.
- **Severity**: UX
- **Status**: Fixed

### PL-002: LLL Summer 2027 Team Registration Payment screen — Check Owed Total double-subtracts Deposit Processing Fees
- **Area**: Team Accounting / Payment Processing
- **Where**: LLL Summer 2027 → Team Registration → Payment screen
- **What I did**: Reviewed the accounting columns on the Team Registration Payment screen for LLL Summer 2027
- **What I expected**: Check Owed Total = Owed (base fee) − Paid (with deposit processing fees already accounted for in Paid)
- **What happened**: Check Owed Total subtracts Deposit Processing Fees a **second time**, even though those fees are already included in the Paid/Owed totals. The deposit processing fees are being deducted twice — once via the Paid column, and again in the Check Owed calc.
- **Severity**: Bug
- **Status**: Fixed
- **Note**: Likely in the Check Owed formula on the registered-teams-grid (or whatever computes the Check Owed column). Verify whether the Paid column on this screen already nets out deposit processing fees — if it does, Check Owed should subtract Paid only, not Paid + DepositProcFee.

### PL-001: Club Rep fees missing on recently-built tournament sites — needs backfill
- **Area**: Team Accounting
- **What I did**: Reviewed Club Rep fee configuration on recently-built tournament sites (e.g., LADT Lax by the Sea Summer 2027) and other recent sites
- **What I expected**: Club Rep fees populated on every tournament site
- **What happened**: Club Rep fees are missing on LADT Lax by the Sea Summer 2027 and other recently-built sites. Need to identify all affected tournaments and populate the fees. Todd is aware and wants this tracked so it doesn't get forgotten.
- **Severity**: Bug
- **Status**: Fixed

