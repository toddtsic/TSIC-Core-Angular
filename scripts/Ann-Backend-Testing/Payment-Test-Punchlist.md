# Payment Testing - Punch List

**Tester:** Ann
**Date Started:** 2026-07-18
**Status:** In Progress

Pairs with [Payment-Test-Checklist.md](Payment-Test-Checklist.md). Each item notes what was being **Tested** (the job type), plus the checklist **section** and **scenario** it came from.

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

## Job Types (testing progress)

- [x] 1. Tryouts Registration — single player
- [ ] 2. Showcase Registration — single player
- [ ] 3. Camps & Clinics — multi-player, multi-event
- [ ] 4. ARB Players — autopay
- [ ] 5. Maryland Summer Camps — deposit / balance due
- [ ] 6. ISP Event Center — player league
- [ ] 7. Tournament — Club Rep (team)
- [ ] 8. Tournament — Player Self-Roster

---

## Punch List Items

_Ordered oldest → newest (newest at bottom). Item IDs are PL-### within this file._

### PL-001: Admin CC charge confirmation — remove "cannot be undone" (keep it on the Refund popup)
- **Tested**: Tryouts Registration
- **Checklist**: §1 Tryouts — CC payment by Admin
- **Area**: Player Details → Accounting → Add Accounting Record → Credit Card
- **Where**: The confirmation popup shown when adding a Credit Card accounting record (admin CC charge)
- **What I did**: On the player Accounting popup, added a Credit Card accounting record and reached the confirmation popup
- **What I expected**: A confirmation that does **not** warn "This cannot be undone" — an admin CC charge can be voided or refunded afterward
- **What happened**: The confirmation reads "This processes immediately via Authorize.Net and cannot be undone." That's inaccurate for a CC charge (it can be voided/refunded). Remove the "cannot be undone" wording here. **Keep** it on the Refund popup, where it's accurate.
- **Severity**: UX
- **Status**: Fixed
- **For Todd**: Drop the "and cannot be undone" / "Cannot be undone" phrase from the CC-charge confirmation only; leave the Refund confirmation as-is.
- **Note (dev)**: CC-charge confirmation text appears twice — `accounting-ledger.component.html:235` ("This processes immediately via Authorize.Net and cannot be undone.") and `:473` ("Processes immediately via Authorize.Net. Cannot be undone.") — both `cc-confirm-warning`. Keep the refund copy at `refund-modal.component.html:58, 72`.
- **Resolved**: Dropped "Cannot be undone." from the **charge** popup only (`accounting-ledger.component.html:473`). Note: `:235` is actually the **Refund** confirmation (heading "Confirm CC Refund"), not a second charge instance — its wording is correct and left unchanged, as is `refund-modal.component.html`.

### PL-002: Correction — a negative amount can't be entered (confirm button won't enable); reconcile with the "negative increases owed" hint
- **Tested**: Tryouts Registration
- **Checklist**: §1 Tryouts — Negative correction (increases owed)
- **Area**: Player Details → Accounting → Add Accounting Record → Correction
- **Where**: The correction amount input / confirm button, and the info hint beneath it
- **What I did**: On the player Accounting popup, chose Correction and tried to enter a negative amount
- **What I expected**: To be able to enter a negative correction — the hint says a negative amount increases the amount owed — and the confirm button to enable
- **What happened**: The negative amount wouldn't take and the confirm button doesn't highlight. But the info hint reads "A negative (–) amount increases the amount owed." So either negatives should be enterable (fix the input/button), or — if they're intentionally restricted — that hint should be removed/clarified.
- **Severity**: Question
- **Status**: Fixed
- **For Todd**: Decide the intended behavior. If negatives should work → fix so a negative can be entered and the button enables. If negatives are intentionally limited → remove/clarify the "A negative amount increases the amount owed" hint so it doesn't contradict the UI.
- **Note (dev)**: The amount input caps the negative at the amount paid: `[min]="paymentType() === 'correction' ? -modalPaid() : 0"` (`accounting-ledger.component.html:378`). With nothing paid yet (modalPaid = 0) the min is 0, so no negative is possible — the likely cause on a fresh registration. Also: a negative that "increases owed" being bounded by *amount paid* looks inconsistent with the hint at line 420 — the two may need reconciling.
- **Decision (Todd)**: **No negative correction records.** Corrections are positive-only (they reduce the amount owed). A negative correction previously "un-paid" (reduced PaidTotal) bounded by amount paid; that path is removed everywhere.
- **Resolved**: (1) Replaced the "+/− amount" hint with "A correction **reduces** the amount owed — for example, to waive a charge or fix an overcharge. It can't exceed the balance due." (2) FE now positive-only: `[min]="0"`, and `canSubmitPayment`/`correctionExceedsBounds` use `amt > 0 && amt <= checkBalanceDue()`; dead "refundable" validation branch and unused `modalPaid` computed removed. (3) Backend chokepoint `RegistrationSearchService.cs:532` now rejects `Amount <= 0`, and the redundant `-PaidTotal` floor (old lines 557–561) deleted. Copy and enforcement now agree on both client and server.

### PL-003: Add Accounting Record → Credit Card — the Optional Comment is dropped (never saved); Check keeps it
- **Tested**: Tryouts Registration
- **Checklist**: §1 Tryouts — CC payment by Admin
- **Area**: Player Details → Accounting → Add Accounting Record → Credit Card
- **Where**: The Optional Comment field on a CC accounting record; the Payment Ledger display
- **What I did**: Added a Credit Card accounting record with an Optional Comment entered
- **What I expected**: The comment to appear on the ledger row, the same as it does for a Check record
- **What happened**: The comment doesn't appear in the Payment Ledger for a CC record. It works for Check.
- **Severity**: Bug
- **Status**: Fixed
- **For Todd**: Carry the comment through on a CC charge, the same as Check/Correction. It's currently dropped before it's ever sent.
- **Decision (Todd)**: Option A — save it. Keep the Optional Comment on CC charges and persist it like Check does.
- **Resolved**: Threaded the comment end-to-end — `CcChargeEvent` + emit (accounting-ledger), `onCcCharge` request build (registration-detail-panel), `RegistrationCcChargeRequest` + `RegistrationChargeItem` DTOs, and `ChargeCcAsync`. The canonical engine (`PaymentService.cs:1580`) previously hard-coded `ra.Comment = "Registration Payment"` after capture, clobbering any note — now it uses the admin comment when non-blank and falls back to the default otherwise. Registrant self-pay never sets a comment, so it's unaffected. API models regenerated (`comment?` on `RegistrationCcChargeRequest`).
- **Note (dev)**: In `accounting-ledger.component.ts`, the CC branch emits `ccChargeSubmitted` with only `creditCard` + `amount` — **no `comment`** (lines 482-495). The check/correction branch does send `comment: this.comment() || null` (line 500). So the comment is lost at the source for CC. Fix = add the comment to the CC payload, and make sure the `ccChargeSubmitted` handler + the CC-charge backend accept and persist it. Secondary: `displayComment()` / `isAutoChargeDescription()` (:258) deliberately hide the auto-generated charge description — confirm a genuine admin comment isn't also suppressed once it's passed through.

### PL-004: LADT fee table (EBD / Late Fee) — the "Inherited from" text isn't legible; it overwrites other info
- **Tested**: Tryouts Registration
- **Checklist**: §1 Tryouts — Early Bird Discount / Late Fee (LADT setup)
- **Area**: LADT Editor → fee table (sibling grid) → EBD / Late Fee columns
- **Where**: The "Inherited from …" indicator (ⓘ) on a fee / phase in the LADT fee table
- **What I did**: Set EBD / Late Fee amounts in LADT and looked at the fee table
- **What I expected**: The amount **and** its "inherited from" source both readable
- **What happened**: The amount shows correctly, but the "inherited from" text isn't legible — it overwrites / overlaps other info in the cell.
- **Severity**: UX
- **Status**: Fixed
- **For Todd**: Lay it out so the "Inherited from …" indicator doesn't overlap adjacent info — widen the column, render it as a proper hover tooltip, or reposition it.
- **Root cause**: Not positioning — `.fee-inherited` dimmed the pill with `opacity: 0.55`, and CSS opacity on an ancestor applies to the entire subtree including the `position: fixed` `<app-info-tooltip>` panel. So the tooltip rendered at 55% opacity and the row beneath bled through it → "both texts," illegible. (Confirmed live via DOM inspect: the panel is a descendant of `div.fee-pill.fee-inherited`.)
- **Resolved**: Moved the dimming off the container onto its text children only — `.fee-inherited > :not(app-info-tooltip) { opacity: 0.55; font-style: italic; }` (ladt-sibling-grid.component.ts:460). Inherited fees still read dimmed + italic; the tooltip is no longer in an opacity-reduced subtree and renders fully opaque. Scoped to the LADT grid; shared `info-tooltip` component untouched.
- **Known residual (deferred — Todd)**: The same opacity trap still applies to fee tooltips on `.e-row.inactive-row` (opacity 0.55) and `.e-row.special-row` (opacity 0.6) — the row-level opacity sits above the pill, so the direct-child `:not()` trick can't reach it. The only durable fix is portaling the tooltip panel to `document.body` in the shared `info-tooltip` component (app-wide). Todd's call: leave it for now; revisit if it surfaces on those rows.
- **Note (dev)**: The text comes from `sourceTooltip()` → `Inherited from ${label}` in `ladt-sibling-grid.component.ts:731-736` (the ⓘ-icon tooltip in the fee columns). Likely a column-width / tooltip-positioning collision in the sibling grid. NB: this is a LADT-editor display issue (surfaced during EBD/Late-Fee setup) — could also live in the LADT punch list if you'd rather group it there.

### PL-005: Family account — a player with a Correction record (not activated) shows neither Pending nor Registered
- **Tested**: Tryouts Registration
- **Checklist**: §1 Tryouts — Correction by Admin + make player Active
- **Area**: Family Account (players list) ← Player Accounting → Correction
- **Where**: The family-account player status after an admin Correction record
- **What I did**:
    - Baseline: added a player, did **not** pay → player Inactive → family account correctly shows **Pending**. ✓
    - Then entered a **Correction record** as Admin for a player but did **not** make them Active → re-entered the Family Account.
- **What I expected**: The player to still show **Pending** — they're inactive and haven't actually paid — same as the no-payment case.
- **What happened**: The player shows **neither Pending nor Registered** — they drop out of the family-account view entirely.
- **Severity**: Bug
- **Status**: Fixed
- **For Todd**: An inactive, unpaid player who has only a Correction record should still read as Pending. Right now a Correction knocks them out of the "pending" test without making them active, so they vanish.
- **Decision (Todd)**: Drop the `PaidTotal <= 0` clause from the pending test.
- **Resolved**: `isPending` now = `BActive != true && AssignedTeamId.HasValue && !isParkedDivision` (FamilyService.cs:813). Rationale: real tender (CC/check/eCheck) always sets `BActive = true`, so an inactive reg with `PaidTotal > 0` can only be a non-activating Correction — which must still read as Pending. The `PaidTotal` gate was the only thing dropping correction-only regs; removing it restores them without mislabeling genuinely-paid (active) regs. Lead comment updated to match.
- **Note (dev)**: `isPending` (backend) = `BActive != true && AssignedTeamId.HasValue && PaidTotal <= 0 && !isParkedDivision` (`FamilyService.cs:813-816`). A Correction record evidently raises `PaidTotal` above 0, so the `PaidTotal <= 0` gate fails → `isPending` = false while the player stays inactive. The frontend then drops them via `.filter(r => r.active || r.isPending)` (`family-players.service.ts:171`), so they show as neither Pending nor Registered. Fix: don't let a Correction (a non-tender adjustment) satisfy the paid gate — base "pending" on actual tender paid, or exclude corrections from the paid figure used here. (Confirm PaidTotal is the tripped condition.)

### PL-006: Correction note — "toggle Active above" is wrong for a multi-player family; point to Search Registrations
- **Tested**: Tryouts Registration
- **Checklist**: §1 Tryouts — Correction by Admin + make player Active
- **Area**: Player Accounting → Add Accounting Record → Correction (the info note)
- **Where**: The note "After recording a correction record for an Inactive registration, you may want to toggle them Active above."
- **What I did**: Entered a Correction for one player in a multi-player family, where a **different** player was the one selected/shown above
- **What I expected**: Guidance that correctly tells me how to activate the **corrected** player
- **What happened**: The note says to "toggle them Active above," but the Active control above belongs to the selected player, which can be a different player in a multi-player family. To actually activate the corrected (Inactive) player you have to go back to the Search Registrations table, select that player, and make them Active.
- **Severity**: UX (misleading copy)
- **Status**: Fixed
- **For Todd**: Reword so it doesn't rely on "above." **Suggested:** "A correction doesn't make the player Active. To activate them, go to the Search Registrations table, select that player, and set them Active." (Shorter alt: "Recording a correction won't activate the player. To activate them, find them in Search Registrations and toggle Active.")
- **Resolved**: Reworded the correction activation note (accounting-ledger.component.html:417) to Ann's suggested copy, dropping the "above" reference. The separate check-payment activation note is unchanged.
- **Note (dev)**: `accounting-ledger.component.html:423-427` — the `showActivationNotes()` correction note.

### PL-007: Registration insurance (Vertical Insure) — a 100% Discount Code still shows the full insurance amount; it shouldn't be offered at all
- **Tested**: Tryouts Registration
- **Checklist**: §1 Tryouts — Discount Code that zeroes the balance ($0 → auto-active)
- **Area**: Player registration → payment step → Vertical Insure (RegSaver) offer
- **Where**: The Vertical Insure insurance offer for a player whose balance was zeroed by a 100% Discount Code
- **What I did**: Applied a 100% Discount Code so the player owed $0. Accounting handled it correctly (balance zeroed, player active).
- **What I expected**: No insurance offer for that player — there's nothing forfeitable to insure when they paid $0.
- **What happened**: Vertical Insure showed the **full** (pre-discount) registration amount as insurable for that player. It shouldn't appear for them at all.
- **Severity**: Bug
- **Status**: Fixed
- **For Todd**: A fully-discounted ($0-owed) player shouldn't be offered RegSaver, and if it ever shows the amount must be net of the code. Right now the offer is frozen at the pre-code price.
- **Decision (Todd)**: Server-side fix. The offer is built at preSubmit (full), but the apply-discount request **rebuilds** it (`BuildOfferAsync`) off the now-persisted discount and returns it; the FE swaps it in and hides VI when `offer.available` is false. The gap was only that the rebuild never went unavailable at $0 net insurable.
- **Resolved**: Added a gate in `VerticalInsureService.cs` (after the net-insurable calc, ~line 327): `if (insurable <= 0m) continue;` — so a fully-discounted reg yields no product → rebuilt offer `Available=false` → FE (`payment-v2.service.ts:482`, `data: offer.available ? playerObject : null`) hides VI. Keys off net *insurable* (fee after discount), not owed, so a paid-in-full reg with a real fee still gets the offer.
- **Note (dev)**: The VI offer is built at **preSubmit** — it's carried on `PreSubmitPlayerRegistrationDto.PlayerObject` (`VerticalInsureService.cs:82` → `BuildProductsAsync`), i.e. **before** the Discount Code is applied at the payment step. So `r.TotalDiscount()` is still 0 when the insurable amount is computed at `VerticalInsureService.cs:326` (`ComputeNetInsurableAmount(configuredTeamFee, r.TotalDiscount(), r.FeeLatefee)`), leaving it at the full configured fee. Separately, the "should we offer it" gate at `:315` only skips when the *configured team fee* is $0 — it never looks at net-of-discount owed, so a 100%-code player passes the gate. Two fixes: (1) re-quote / recompute the offer after a Discount Code is applied (or compute insurable off the net-owed including any applied code), and (2) extend the gate to also suppress the offer when the net insurable ≤ $0 (VI rejects a $0 policy anyway). NB: registration insurance was flagged "not testing yet" on the checklist — found incidentally.

### PL-008: Pay-by-check hold — no auto-expiry, misleading "held pending" copy, and no "check pending" marker in Search Registrations
- **Tested**: Tryouts Registration
- **Checklist**: §1 Tryouts — registrant pay-by-check (applies to every check-eligible job type: §6 ISP, §7 Tournament, etc.)
- **Area**: Player payment step (pay-by-check confirmation) + Search Registrations table
- **Where**: The "Your registration will be held pending receipt of payment" message; the Active status a check-payer gets; the Search Registrations row
- **What I did**: Selected pay-by-check (not eCheck) as a registrant. Payment screen said "Your registration will be held pending receipt of payment." Player showed **Active** on confirmation.
- **What I expected**: To know how long the hold lasts / how long the parent has to get the check to the Director before the player goes Inactive — and ideally a visible marker that this player opted to pay by check and hasn't paid.
- **What happened / what I found**:
    - **Active is indefinite.** There is **no timer, no expiry, no background job** that ever flips an unpaid check-payer to Inactive. They stay Active until an admin manually records the check or voids/removes the registration. So "held pending receipt of payment" implies a hold that could lapse, but nothing lapses.
    - **No indicator** in Search Registrations that a registrant chose pay-by-check and it hasn't been received — the table shows only Active/Inactive and a discount-code badge.
- **Severity**: Question + UX enhancement
- **Status**: Fixed
- **Decision (Todd)**: A check-payer is **Active** on submit, so there is no "hold" and nothing to auto-expire — the policy is correct as-is (no timer). The honest consequence is discretionary: the director may drop the registration if the check never arrives. Fix is the copy only.
- **Resolved**: Reworded both pay-by-check confirmations — player (payment-step.component.ts:604) and team (:552) — from "…will be held pending receipt of payment" to "Your registration is active. Please mail your check to complete payment — if it isn't received, the director may drop your registration." (team: "Your teams are registered. …"). No hold/expiry language.
- **For Todd** — three decisions:
    1. **Policy**: is an unpaid check-hold meant to be indefinite, or should there be an N-day window after which it auto-inactivates? (Today it's indefinite.)
    2. **Copy**: reword the payment-step message to match the real behavior — either "stays active until we receive your check" (no auto-release) or, if you add an expiry, state the deadline.
    3. **Enhancement**: add a "check pending" marker (Ann's suggested **P** badge + hover, e.g. "Opted to pay by check — payment not yet received") on the Search Registrations row.
- **Note (dev)**: Pay-by-check → `POST /player-registration/submit-by-check` → `PlayerRegistrationService.SubmitByCheckAsync` stamps `PaymentMethodChosen = 3 (Check)` + claims the seat and sets `BActive = true` (`:768-807`); no expiry field is written. No hosted/scheduled service inactivates check-payers — the only `BackgroundService` is `AdnSweepService` (Authorize.Net settlement reconciliation). Message source: `payment-step.component.ts:604` (player) / `payment-step.component.ts:552` (team). A pending-check badge is derivable from `PaymentMethodChosen == 3 && active && owed > 0`; Search Registrations currently renders only `active-badge` / `dc-badge` (`search-registrations.component.html:782, 765`).

### PL-009: Intermittent "Player registration is not currently open" toast — despite the job being fully open
- **Tested**: Showcase Registration
- **Job**: `American Select Lacrosse:INDIVIDUAL Showcase 2026` — JobId `31284005-8A6D-44FE-ACAB-85675BF7F65B`
- **Area**: Public landing → Register Player (route guard) → the job pulse
- **Where**: The warning toast "Player registration is not currently open for this event."
- **What I did**: Tried to enter Player Registration for the Showcase. Sometimes it lets me in, sometimes the toast fires and bounces me back to the landing — same job, no config change. It flapped repeatedly (couldn't get in → could → after registering a player, couldn't → then couldn't even without registering).
- **What I expected**: Consistent behavior — the job is open, so it should let me in every time.
- **What happened**: Intermittent "registration not currently open" toast even though registration is open.
- **Severity**: Bug (intermittent / race)
- **Status**: Fixed
- **Resolved**: The `/pulse` endpoint ([JobsController.cs:322](../../TSIC-Core-Angular/src/backend/TSIC.API/Controllers/JobsController.cs#L322)) now sends `Cache-Control: private, no-cache` + `Vary: Authorization` — the same headers the sibling menu endpoint already used. Its body varies by auth (the `My*` overlay) and by live config, so a "closed" pulse can no longer be cached and replayed, which was the stale-snapshot source of the intermittent toast. FE guard bypass-for-authenticated (contributing item 2) left as-is; revisit only if the flap survives the header fix.
- **Root cause (verified against the live DB for this job)**: Every gate is **open** — nothing in the saved config should close it:
    - `BRegistrationAllowPlayer = 1` (QL toggle on)
    - 7 Player-role fee rows exist (role `DAC0C570` = Player) → `PlayerRegistrationOpen` = true
    - 6 real events (2028/2029/2030 Field/Goalie) Active, self-rostering enabled on the age group, window `2025-09-09 → 2026-07-31` contains today → `PlayerTeamsAvailableForRegistration` = true
    - `EventConcluded` = false (EventEndDate 2026-07-22 > today; no schedule), not superseded (2026 is newest)
    - `BplayerRegRequiresToken = 0` (no invite token)
  So the correct behavior is "let you in." The toast is [registration-invite.guard.ts:68](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/infrastructure/guards/registration-invite.guard.ts#L68); it fires **only** when the pulse it fetched reports `playerRegistrationOpen === false` OR `playerTeamsAvailableForRegistration === false`. So at the moments it fires, the guard received a **stale/closed pulse snapshot** even though the live config is open.
- **For Todd — the fix**:
    1. **Primary (backend):** the `/pulse` endpoint ([JobsController.cs:273](../../TSIC-Core-Angular/src/backend/TSIC.API/Controllers/JobsController.cs#L273)) ships with **no `Cache-Control` and no `Vary: Authorization`**, yet its body varies by auth (the `My*` fields) and by config. So a "closed" pulse can be cached and reused. The sibling endpoint right above it already sets `private, no-cache` + `Vary: Authorization` ([JobsController.cs:258-261](../../TSIC-Core-Angular/src/backend/TSIC.API/Controllers/JobsController.cs#L258)) — apply the same to the pulse endpoint. Almost certainly the real cure.
    2. **Contributing:** the guard bypasses the whole check for authenticated users (`if (auth.isAuthenticated()) return true`, [registration-invite.guard.ts:65](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/infrastructure/guards/registration-invite.guard.ts#L65)). If the JWT lapses / hasn't rehydrated (cold load, time passing while registering), you drop to the anonymous path and act on whatever pulse comes back — matches "after registering a player, couldn't get in." Consider cache-busting the guard's pulse GET too.
    3. **Config note (not a code bug):** this job is currently `BSuspendPublic = 1` (public page suspended) and freshly configured (event start 07-22 is imminent) — exactly the state that would have produced "closed" pulses that then linger in cache. Confirm the page is meant to be live for parents.

### PL-010: Waitlist option lingers after Max is raised — real event (with fee) AND its $0 WL twin both show
- **Tested**: Showcase Registration
- **Job**: `American Select Lacrosse:INDIVIDUAL Showcase 2026` — JobId `31284005-8A6D-44FE-ACAB-85675BF7F65B`
- **Area**: Player registration → event/team selection
- **Where**: The selectable event list (the real event carrying its fee, and the `WAITLIST - {age}` twin at $0)
- **What I did**: An age group had hit its Max Number, so a Waitlist option was created and shown. I then **raised Max Number above the current registrant count** so the event is no longer full, and reopened the registration event list.
- **What I expected**: The Waitlist option to disappear once Max is no longer reached — only the real event (with fee) should remain.
- **What happened**: **Both** show — the real event *with its fee* AND the WL option at $0.
- **Severity**: Bug
- **Status**: Fixed (with PL-011 — same root cause, same one-line fix)
- **Resolved**: `GetAvailableTeamsQueryResultsAsync` now excludes `WAITLIST` agegroups from the picker, not just `Dropped` ([TeamRepository.cs:149](../../TSIC-Core-Angular/src/backend/TSIC.Infrastructure/Repositories/TeamRepository.cs#L149)). A waitlist twin is never a bookable option — waitlisting is expressed on the REAL team via `RosterIsFull` (UI badges it "WAITLIST"), and the twin is a payment-time-only artifact. This makes the live query match what its own test already documents ([WaitlistTwinDisplayTests.cs:53-54](../../TSIC-Core-Angular/src/backend/TSIC.Tests/TeamRegistration/WaitlistMirror/WaitlistTwinDisplayTests.cs#L53) — "the base query NEVER returns WAITLIST agegroups") and the `AgegroupConstants` system-bucket contract. No registrant-count logic and no data deletion — the leftover twin simply stops being surfaced; the real event (now open) shows alone. Resume is unaffected: a pending player sits on the real team until payment, so they still match the real entry. Stale inline comment corrected.
- **Root cause (verified against the live DB for this job)**: `RosterIsFull = current >= MaxCount && MaxCount > 0` ([TeamLookupService.cs:67](../../TSIC-Core-Angular/src/backend/TSIC.API/Services/Teams/TeamLookupService.cs#L67)) is computed **live**, so raising Max correctly flips the *real* event's `rosterIsFull` back to false — good. But when the event was full, a **WAITLIST twin was minted as a real `Teams` row** (agegroup `WAITLIST - {name}`, MaxCount 100000). That twin **persists** — I confirmed `WAITLIST - 2028 / 2029 / 2030` rows exist for this job alongside the open real teams. `GetAvailableTeamsQueryResultsAsync` was filtering only `Dropped` (not `WAITLIST`), so it surfaced the twin as a standalone pickable row and nothing hid it once the parent regained capacity. So the leftover $0 twin showed next to the now-open real event.
- **Design note**: The intended model (per the code's own comment) is that a waitlist is just a **badge on the full real team** ("⚠ WAITLIST · $0"), with the twin minted only at payment — NOT a standalone pickable option ([team-selection-step.component.ts:869-872](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/registration/player/steps/team-selection-step.component.ts#L869)). A standalone twin appearing next to its real parent already deviates from that; showing it once the parent isn't even full is the visible bug.
- **For Todd — the fix**: Suppress a `WAITLIST - {name}` twin from the *new-selection* list whenever its parent real event has open seats (`rosterIsFull === false`), while still keeping it visible/resumable for a player already registered onto that twin (so pending waitlisted players aren't stranded). The age-group picker already does exactly this collapse — `availableAgegroupOptions()` shows the real name when any team `hasOpenSeat`, else the `WAITLIST -` name ([team.service.ts:162-181](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/registration/player/services/team.service.ts#L162)). The team/event-level list (`getAvailableTeamDtos` → `filterByEligibility`, [team-selection-step.component.ts:864](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/registration/player/steps/team-selection-step.component.ts#L864)) applies no such collapse. Cleanest is to filter in the backend (`TeamLookupService` / `GetAvailableTeamsQueryResultsAsync`) so every consumer benefits, plus optionally retire an **empty** minted twin when its parent's capacity is restored so stale twins don't accumulate.

### PL-011: Full FP/Goalie shows TWO waitlist entries each (one with ⚠ icon, one without) — should be one per team
- **Tested**: Showcase Registration
- **Job**: `American Select Lacrosse:INDIVIDUAL Showcase 2026` — JobId `31284005-8A6D-44FE-ACAB-85675BF7F65B`
- **Area**: Player registration → event/team selection (waitlist display)
- **Where**: The event list for an age group where both Field Player and Goalie are full
- **What I did**: Looked at an age group whose FP and Goalie positions have reached Max (waitlist engaged).
- **What I expected**: One waitlist option per full team (no icon).
- **What happened**: **Four** options — each full team (FP and Goalie) shows up **twice**: one entry with the yellow ⚠ triangle icon and one without.
- **Severity**: Bug
- **Status**: Fixed (with PL-010 — same root cause, same one-line fix)
- **Resolved**: Same fix as PL-010 — `GetAvailableTeamsQueryResultsAsync` now excludes `WAITLIST` agegroups ([TeamRepository.cs:149](../../TSIC-Core-Angular/src/backend/TSIC.Infrastructure/Repositories/TeamRepository.cs#L149)). The standalone twin (the plain, icon-less duplicate) no longer appears, so a full team shows exactly one entry: the real team badged `⚠ WAITLIST` off its live `RosterIsFull` flag. The "which representation to keep" decision resolved structurally — the badged real team is canonical (the code's intended model); the twin was never meant to be a selectable option.
- **Root cause (same as PL-010 — the persistent minted twin surfaced by the picker)**: For each full team the list carries two rows:
    1. the **real** team, `rosterIsFull=true` → badged `⚠ WAITLIST · {name} ($0)` — the ⚠ icon ([team-selection-step.component.ts:792-794](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/registration/player/steps/team-selection-step.component.ts#L792));
    2. the **minted twin** (`Teams` row whose agegroup is `WAITLIST - {name}`, MaxCount 100000 so `rosterIsFull=false`) → falls to the `else` branch, plain `WAITLIST - {name} ($0)`, **no icon** ([:795-800](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/registration/player/steps/team-selection-step.component.ts#L795)).
  Both are surfaced because `GetAvailableTeamsQueryResultsAsync` emits the WAITLIST twin agegroups alongside the real teams and nothing dedupes the two representations. (Confirmed twins exist for this job: `WAITLIST - 2028/2029/2030` positions.)
- **For Todd — decision + fix**: Collapse to **one** waitlist entry per full team. Which representation to keep is a display call:
    - **Ann's preference**: keep the plain `WAITLIST - {name}` (no icon), drop the badged duplicate.
    - **Code's intended model**: the badged `⚠ WAITLIST · {name}` on the real team is canonical, and the standalone twin isn't meant to be a selectable option at all (it's supposed to be minted only at payment — see comment [team-selection-step.component.ts:869-872](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/registration/player/steps/team-selection-step.component.ts#L869)). By that model the fix is to hide the standalone twin — which also fixes PL-010.
  Recommend picking one representation and suppressing the other where the available-teams list is built (backend `TeamLookupService` / `GetAvailableTeamsQueryResultsAsync`, so every consumer is consistent), keeping the twin resumable for a player already registered onto it. Resolving this together with PL-010 is natural — they're the same duplicate-twin root cause.

### PL-012: 2nd Discount Code for the same player shows generic "No discounts were applied" — should say only one code per player
- **Tested**: Showcase Registration
- **Area**: Player registration → payment → Apply Discount Code
- **Where**: The error banner after entering a second Discount Code for a player who already has one (before payment)
- **What I did**: Entered a Discount Code for a player, then entered a **second** Discount Code for the same player before completing payment.
- **What I expected**: A message telling me only one code can be applied per player.
- **What happened**: The message reads **"No discounts were applied"** — generic and doesn't explain why.
- **Severity**: UX (copy)
- **Status**: Fixed
- **Resolved**: Two backend changes in `PlayerRegistrationPaymentController` (no FE/model change — the FE already renders `resp.message`). (1) The per-player one-use message is now Ann's exact text: "Only one Discount Code can be applied per player" ([:254](../../TSIC-Core-Angular/src/backend/TSIC.API/Controllers/PlayerRegistrationPaymentController.cs#L254)). (2) When `successCount == 0`, the aggregate `Message` now surfaces the specific per-player reason **only when every failure agrees on it**; mixed reasons keep the generic "No discounts were applied" ([:366-381](../../TSIC-Core-Angular/src/backend/TSIC.API/Controllers/PlayerRegistrationPaymentController.cs#L366)). Verified the catch-all is safe to reuse: invalid/expired code ([:152](../../TSIC-Core-Angular/src/backend/TSIC.API/Controllers/PlayerRegistrationPaymentController.cs#L152)), zero-amount ([:186](../../TSIC-Core-Angular/src/backend/TSIC.API/Controllers/PlayerRegistrationPaymentController.cs#L186)), and no-valid-players ([:168](../../TSIC-Core-Angular/src/backend/TSIC.API/Controllers/PlayerRegistrationPaymentController.cs#L168)) all return **before** this line, so the reasons reaching the aggregate are only the loop-level ones — the "No discount applicable" ($0 balance) case at [:282](../../TSIC-Core-Angular/src/backend/TSIC.API/Controllers/PlayerRegistrationPaymentController.cs#L282) still shows its own accurate text, not the one-code wording.
- **Recommended text (Ann)**: "Only one Discount Code can be applied per player."
- **For Todd — where/how**: The backend already detects this exact case — the one-use guard `if (reg.DiscountCodeId != null)` sets a per-player result message **"Discount already applied to this player"** ([PlayerRegistrationPaymentController.cs:247-256](../../TSIC-Core-Angular/src/backend/TSIC.API/Controllers/PlayerRegistrationPaymentController.cs#L247)). But the frontend shows the **aggregate** `resp.message` ([payment-v2.service.ts:499-500](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/registration/player/state/payment-v2.service.ts#L499)), which when nothing applied is the generic **"No discounts were applied"** ([PlayerRegistrationPaymentController.cs:369](../../TSIC-Core-Angular/src/backend/TSIC.API/Controllers/PlayerRegistrationPaymentController.cs#L369)) — so the specific reason never surfaces. Fix by surfacing the specific reason, e.g. update the per-player message at `:254` to Ann's wording AND have the failure path reflect it (either the aggregate Message picks up the common failure reason when `successCount == 0`, or the frontend renders `resp.Results[].message`). **Do NOT** simply relabel line 369 — it's a catch-all that also fires for an invalid code and the "No discount applicable" ($0 balance) case at `:282`, which Ann's wording would mislabel.

### PL-013: Vertical Insure doesn't reliably re-quote after a Discount Code — $ code stale until re-login; 100% not cleanly gated (relates to PL-007)
- **Status**: **Fixed** (verified 2026-07-19, retested on Camps & Clinics — all Discount Code types adjust VI correctly, no re-login needed)
- **Resolved**: The server-side net-$0 gate is now in place — `VerticalInsureService.BuildProductsAsync` skips a registration whose net insurable is ≤ $0 after discounts (`if (insurable <= 0m) continue;`, [VerticalInsureService.cs:333-339](../../TSIC-Core-Angular/src/backend/TSIC.API/Services/Shared/VerticalInsure/VerticalInsureService.cs#L333)). A 100%/balance-zeroing code → no product → `Available=false` → widget hides cleanly (closes PL-007's phantom full-amount offer); partial $/% codes rebuild with the reduced premium. Retest confirms the client remount race no longer reproduces (all code types refresh immediately). **This also closes PL-007** (same root cause).
- **Tested**: Showcase Registration
- **Area**: Player registration → payment → Apply Discount Code + Vertical Insure (RegSaver) offer
- **Where**: The VI offer/premium as it should track the discounted insurable amount
- **What I did / saw** (three codes on the payment screen):
    - **$ (fixed) code** — VI did **not** adjust; it only corrected after I logged back in.
    - **50% code** — VI adjusted **immediately** on the payment screen.
    - **100% code** — **no** VI offered (correct outcome). (In Tryouts / PL-007 a 100% code showed the **full** amount — the opposite — which is why this felt inconsistent / "was it fixed?")
- **What I expected**: VI to re-quote consistently and immediately whenever a code changes the insurable amount.
- **Severity**: Bug (intermittent refresh + inconsistent 100% gating)
- **Status**: Open
- **Findings (verified in code)** — two separate causes:
    1. **Server not cleanly gating a net-$0 offer (this is PL-007's exact root cause).** On every successful discount the server DOES rebuild the offer (`BuildOfferAsync`, [VerticalInsureService.cs:64](../../TSIC-Core-Angular/src/backend/TSIC.API/Services/Shared/VerticalInsure/VerticalInsureService.cs#L64)) and returns it, and the offer is only marked `Available=false` when **zero products** are built ([:83-89](../../TSIC-Core-Angular/src/backend/TSIC.API/Services/Shared/VerticalInsure/VerticalInsureService.cs#L83)). BUT `BuildProductsAsync` skips a reg **only when the *configured* team fee is $0** ([:315](../../TSIC-Core-Angular/src/backend/TSIC.API/Services/Shared/VerticalInsure/VerticalInsureService.cs#L315)); it does **not** skip when the net-of-discount `insurable` computed at [:326](../../TSIC-Core-Angular/src/backend/TSIC.API/Services/Shared/VerticalInsure/VerticalInsureService.cs#L326) is $0. So a **100% code on a paid reg** yields a product with `InsurableAmount = 0` and `Available=true` — the offer is not hidden server-side; it only "disappears" because VerticalInsure rejects a $0 policy (incidental). That's why 100% showed the full amount in Tryouts (stale offer) but nothing here — timing, not clean gating.
    2. **Client remount race ($ vs %).** After a successful discount the client remounts the widget: `refreshViAfterDiscount()` → `reset()` + `setTimeout(tryInitVerticalInsure, 0)` + a 150ms×20 poll for the `#dVIOffer` host ([payment-step.component.ts:1314-1359](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/registration/player/steps/payment-step.component.ts#L1314)). Both `$` and `%` take this SAME path — nothing branches on code type — so the "$ didn't refresh until re-login" is that fire-and-forget remount losing the race (a small fixed-$ change is also easy to miss vs a 50% swing). Re-login rebuilds the offer at preSubmit → correct.
- **For Todd — fix**:
    1. **Server (also closes PL-007):** add a `if (insurable <= 0) continue;` right after [VerticalInsureService.cs:326](../../TSIC-Core-Angular/src/backend/TSIC.API/Services/Shared/VerticalInsure/VerticalInsureService.cs#L326) so a net-$0 reg builds no product → `products.Count == 0` → `Available=false` → clean hide. This makes the 100% (and any balance-zeroing code) deterministically drop the offer instead of leaning on VI's $0 rejection.
    2. **Client:** make the post-discount VI refresh deterministic instead of a fire-and-forget `setTimeout` + poll — e.g. key the widget host off the offer/insurable identity so Angular tears down and recreates it on change, or await a widget-ready signal before clearing the spinner — so a `$` code re-quotes reliably without a re-login.
- **Note**: This supersedes PL-007's analysis (which suspected the preSubmit build + configured-fee gate). PL-007's instinct was right; the precise cause is the missing net-insurable skip here plus the remount race. Resolve PL-007 and PL-013 together.

### PL-014: Admin can't record an eCheck payment (when eCheck is enabled) — confirm this is intentional
- **Tested**: Showcase Registration
- **Area**: Player Details → Accounting → Add Accounting Record
- **Where**: The admin payment-method choices on a job that has eCheck enabled
- **What I did**: On a job with eCheck turned on, opened Add Accounting Record as Admin.
- **What I noticed**: The admin can enter **Credit Card, Check, Correction, Refund** — but there is **no eCheck option**. Just confirming we don't want to add it.
- **Severity**: Question
- **Status**: Won't Fix
- **Decision (Todd)**: Leave as-is — no admin eCheck. An eCheck (ACH) is a bank draft that requires the payer's own bank account/routing + authorization to draft; an admin doesn't have and shouldn't key a family's bank credentials. A paper check the admin physically holds and records, and a CC they can key, but an eCheck has no natural admin workflow — it's the family's own action in the registrant flow. Intentional behavior, not changing.
- **Finding (verified)**: The admin accounting ledger's `PaymentType` is `check | cc | correction | refund` only ([accounting-ledger.component.ts:175, 387, 501](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/shared-ui/components/accounting-ledger/accounting-ledger.component.ts#L175)); there is no `echeck` type. Consistent with the earlier decision to punt admin-entered eCheck.
- **For Todd — recommendation**: Likely leave as-is. An eCheck (ACH) is a bank draft that needs the **payer's** bank account/routing + their authorization to draft — an admin doesn't have and shouldn't key a family's bank credentials. A paper check the admin can record (they hold it) and a CC they can key, but an eCheck has no natural admin workflow — it's the family's own action in the registrant flow. Confirm you're comfortable not adding admin eCheck; if you ever do want it, it'd need the family's bank details captured some other way (e.g. a family-initiated eCheck the admin only records after the fact).

### PL-017: 🔴 CRITICAL — migrated player fees can be MISSING from the new fee engine (silently price to $0). Cross-job-type migration risk.
- **Tested**: Camps & Clinics Registration
- **Job (found on)**: `Yellow Jackets South:Camps and Clinics 2026` — JobId `CF705E2A-89B5-4203-95AB-02AE8C9A6A90`; team `YJS Pre-Travel Academy: 2033, 2034, 2035, 2036` (legacy fee $300)
- **Area**: Fee configuration / migration integrity → player self-roster pricing (LADT editor "Balance Due" + registration balance due)
- **What I found**: Pre-Travel Academy's **$300 player fee did not come forward to the new fee structure**. The LADT editor showed "Balance Due — Agegroup default / Inherits from the age group," and a registration would price the event at **$0**.
- **Severity**: **Bug — CRITICAL** (money: registrations silently priced at $0)
- **Status**: Not an Issue (migration data — **run the seed script**)
- **Decision (Todd)**: Not a code defect. The app is correct — the resolver reads `fees.JobFees` (verified: `FeeRepository.GetResolvedFeeAsync` reads only `JobFees`, never `perRegistrantFee`) and the LADT editor writes the right row. The gap is purely that the fee-population seed hasn't been re-run since these ad-hoc C&C teams were added. **Fix = run [`scripts/6a) seed-fees-from-legacy.sql`](../../scripts/6a) seed-fees-from-legacy.sql)** — its section 2 (lines 51-60) backfills team-scoped `JobFees` from `teams.PerRegistrantFee` keyed on `AgegroupId + TeamId`, the exact shape Ann's manual fix wrote. The script is **idempotent** (`DELETE FROM fees.JobFees` + repopulate), so it re-seeds every JobFee from legacy in one pass and heals all broken teams (active 2026 + historical). Verify after with [`scripts/6c) verify-fees-concordance.sql`](../../scripts/6c) verify-fees-concordance.sql). No code change; no targeted DML needed.
- **Residual (operational, not code)**: any C&C team added/edited *after* a seed run stays $0 until the seed is re-run — the recurrence is a workflow item (re-seed as part of cutover / after ad-hoc team edits), not a bug. Ann's per-job validation gate (below) is the go/no-go check.
- **Root cause (verified in DB)**: The app has **two parallel fee stores**. The **legacy** `Leagues.teams.perRegistrantFee` held **$300**, but the **new engine reads only `fees.JobFees`** (team → age-group → league `BalanceDue`, `FeeRepository.GetResolvedFeeAsync`) and **never consults `perRegistrantFee`**. There was **no team-scoped `JobFees` row** for this team, so the resolver fell back to the "Programs" age-group default = **$0.00**. First DB query (before any fix) confirmed the team-scoped row was absent; the fee resolved to $0.
- **Confirmation the mechanism is exactly this**: Ann then entered **300** in the editor's Balance Due override → that **created** the correct team-scoped `JobFees` row (JobId `CF705E2A`, RoleId Player, AgegroupId `12D20BE0` "Programs" ✓, TeamId matches, `BalanceDue = 300`) → the fee now resolves to **$300**. So the editor writes the right row when set, and the resolver reads it once present — the gap was purely that migration never populated it.
- **THIS IS NOT CAMPS-ONLY — it is a cross-job-type migration risk.** Scanning all jobs for the same signature (legacy `perRegistrantFee > 0` but the cascade-resolved new player fee = $0):
    - **~1,763 teams across ~139 jobs** historically match the signature.
    - Job **types** affected (from the broad scan): Camps & Clinics, Tournaments / "Main Event", Fall Signups, Clinics & Leagues, Training Programs, rec-council **Soccer / Basketball / Field Hockey**, Showcase / "Individual Event", Day Camps, Winter League — essentially every type that uses player self-roster fees.
    - Among **active 2026** jobs (cascade-precise): `Premier Lacrosse:Camps and Clinics 2026` (8 teams), `ODU Lacrosse:Camps and Clinics 2026` (1), `Yellow Jackets North:Camps and Clinics 2026` (1) — **plus** `YJS Camps and Clinics 2026` (dropped out of the scan only because Ann had already hand-fixed Pre-Travel).
- **For Todd — the migration must guarantee this before going wide**:
    1. **Backfill (scalable fix):** for every team with `perRegistrantFee > 0` and no resolvable `JobFees` player fee, write a team-scoped `fees.JobFees` row — `JobId = team.JobId`, `RoleId = Player`, `AgegroupId = team.AgegroupId` (BOTH keys are required — the resolver matches team-level on AgegroupId **and** TeamId), `TeamId`, `BalanceDue = perRegistrantFee`, `Deposit = perRegistrantDeposit`. (Same shape the editor wrote for the manual fix.)
    2. **Or resolver fallback:** have `FeeRepository.GetResolvedFeeAsync` fall back to `team.perRegistrantFee` / `perRegistrantDeposit` when no `JobFees` row resolves — lower-effort but changes resolution semantics, so weigh carefully.
    3. **Pre-migration validation gate (run per job, must return 0 rows):**
       ```sql
       DECLARE @pl UNIQUEIDENTIFIER='DAC0C570-94AA-4A88-8D73-6034F1F72F3A'; -- Player
       SELECT t.TeamId, t.TeamName, t.perRegistrantFee
       FROM Leagues.teams t JOIN Leagues.agegroups ag ON t.AgegroupId=ag.AgegroupId
       WHERE t.JobId=@job AND ISNULL(t.perRegistrantFee,0) > 0
         AND ISNULL(COALESCE(
           (SELECT TOP 1 f.BalanceDue FROM fees.JobFees f WHERE f.JobId=t.JobId AND f.RoleId=@pl AND f.AgegroupId=t.AgegroupId AND f.TeamId=t.TeamId),
           (SELECT TOP 1 f.BalanceDue FROM fees.JobFees f WHERE f.JobId=t.JobId AND f.RoleId=@pl AND f.AgegroupId=t.AgegroupId AND f.TeamId IS NULL),
           (SELECT TOP 1 f.BalanceDue FROM fees.JobFees f WHERE f.JobId=t.JobId AND f.RoleId=@pl AND f.LeagueId=ag.leagueID AND f.AgegroupId IS NULL AND f.TeamId IS NULL)
         ),0) = 0;
       ```
       Any rows returned = teams that will silently register at $0. Note: for tournament/club-team jobs this can surface club teams legitimately not priced via the Player role — review by job type, but for Camps/Clinics/Showcase (player self-roster) every hit is a real mispricing.
- **Scope of the audit — all fee dimensions scanned**:

  | Fee dimension | Legacy source → new engine reads | Total (all history) | Active 2026 | Assessment |
  |---|---|---|---|---|
  | **Player Balance Due** | `teams.perRegistrantFee` → `JobFees.BalanceDue` | **1,851 teams / 147 jobs** | 3 jobs (see below) + YJS (fixed) | 🔴 the real problem |
  | Player Deposit | `teams.perRegistrantDeposit` → `JobFees.Deposit` | 19 teams | 0 | minor; none active |
  | Early-Bird / Late-Fee | `teams.discountFee`/`lateFee` → `FeeModifiers` | 0 in 2026; only 2 `FeeModifiers` rows exist DB-wide | 0 | not a migration risk |
  | Club-Rep / team-registration | *(no direct legacy per-team column)* | not scanned | — | needs a separate pass |

- **⚠️ KEY PATTERN for Todd — active breakage is C&C-only AND sporadic within a job**: every active (2026) job with the Balance-Due gap is **Camps & Clinics**, and even then only *some* teams break while the rest of the same job is fine:

  | 2026 job | Player-fee teams | Broken |
  |---|---|---|
  | Premier Lacrosse:Camps and Clinics 2026 | 32 | **8** |
  | ODU Lacrosse:Camps and Clinics 2026 | 4 | **1** |
  | Yellow Jackets North:Camps and Clinics 2026 | 1 | **1** |
  | Yellow Jackets South:Camps and Clinics 2026 | 34 | 0 (hand-fixed) |

  Every **non-C&C** 2026 job is clean — incl. `American Select:Main Event 2026` with **132** player-fee teams, 0 broken — and **most** C&C jobs are clean too (All American, Hero's, StateOne, YJ Mid-Atlantic/Midwest = 0). So this is **not** a whole-job-type clone failure; it's **specific teams** that received a legacy `perRegistrantFee` without a matching `JobFees` row. Diagnostic thread: find the **team-creation/edit path used for ad-hoc C&C event teams** that writes `perRegistrantFee` but skips the `JobFees` write (manual add / partial clone / import) — that's where the gap is minted. The rest of the 147-job / 1,851-team total is overwhelmingly **historical (2021–2024) jobs** — affected but **likely not critical** (completed, won't be re-registered) — plus club-team false positives in tournament jobs (priced via ClubRep, not Player). The critical, actionable set is the active C&C jobs in the table above.

### PL-018: Re-open of PL-003 — Admin CC Optional Comment STILL doesn't show on retest (source looks correctly wired)
- **Tested**: Camps & Clinics Registration (re-test of a Tryouts item)
- **Area**: Player Details → Accounting → Add Accounting Record → Credit Card → Optional Comment
- **Relates to**: **PL-003** (marked Fixed). On retest the comment **still does not appear** in the Payment Ledger for an admin CC entry.
- **Severity**: Bug (regression / not-resolved on retest)
- **Status**: Awaiting Ann retest (source confirmed correct — likely a stale build)
- **Decision (Todd)**: No code defect. Re-verified [accounting-ledger.component.ts:491](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/shared-ui/components/accounting-ledger/accounting-ledger.component.ts#L491) — the CC emit carries `comment: this.comment() || null` — so the PL-003 fix is present and complete in master. Ann's retest showing the default "Registration Payment" means the comment reached the backend blank, which on correct source points to the **tested build predating the fix** (the model + emit change needs a fresh Angular bundle). **Action → Ann**: confirm the tested env is at/after the PL-003 commit (hard-refresh / rebuilt FE bundle), then retest the admin CC Optional Comment. If it still fails on a confirmed-fresh build, reopen and we'll instrument the controller boundary. Optional display cleanup (suppress the "Registration Payment"/"eCheck Registration Payment" defaults so comment-less rows render blank like Check/Correction) deferred — separate from this retest.
- **What Claude found (full end-to-end source trace — every link is correct in current master)**:

  | Link | Location | Status |
  |---|---|---|
  | Comment input rendered + bound for CC (un-gated "Common Fields") | `accounting-ledger.component.html:370, 399-402` | ✓ |
  | CC emit includes `comment` | `accounting-ledger.component.ts:491` | ✓ |
  | `onCcCharge` → `request.comment` | `registration-detail-panel.component.ts:762, 786` | ✓ |
  | Generated model carries `comment` | `RegistrationCcChargeRequest.ts:10` | ✓ |
  | `ChargeCcAsync` sets `item.Comment = request.Comment` | `RegistrationSearchService.cs:604` | ✓ |
  | Forwards items intact to core engine | `PaymentService.cs:1333` | ✓ |
  | Persists `ra.Comment = item.Comment` (admin comment wins over default) | `PaymentService.cs:1580` | ✓ |
  | Ledger displays non-auto comment | `accounting-ledger.component.ts:258` + `.html:67` | ✓ |

- **Interpretation**: There is **no wiring gap in the source** — the PL-003 fix is complete and correct. So the retest failure is most likely **(a)** the tested build predates the fix (deployment/rebuild lag or a stale cached frontend bundle), or **(b)** a runtime discrepancy static tracing can't see.
- **Next steps (Claude + Todd)**:
    1. **Confirm the env under test actually has the fix** — check the deployed commit against PL-003's fix commit; hard-refresh / rebuild the Angular bundle (the model change needs a fresh FE build).
    2. **If it's deployed and still fails**, instrument the runtime: log the `request.Comment` received in `ChargeCcAsync`, and inspect the new CC row's `record.comment` + `ownerName` returned by the ledger query — to rule out (i) the comment arriving null despite the field being filled, or (ii) `isAutoChargeDescription` suppressing it (fires when `comment.includes(':' + ownerName)`; a normal comment shouldn't, but confirm on the actual saved value).
    3. Confirm the exact entry point Ann used is the search-registrations **registration-detail-panel** host of the ledger (→ `ChargeCcAsync`), not a different host with its own CC handler.
- **Ann's diagnostic observation (narrows it further)**: the CC row currently shows the **default "Registration Payment"** in the same spot where Check/Correction show the optional comment. That default is the **blank-comment fallback** at `PaymentService.cs:1580` (`item.Comment` non-blank → use it; else `"Registration Payment"`). So the display works and the backend writes *a* comment — but it's writing the **default**, which means **`item.Comment` arrived blank**. ⇒ The comment is lost **upstream of the backend** (capture / emit / transport, or the fix isn't in the tested build) — **not** in persistence or display. Runtime step 2 above should log `request.Comment` at the controller boundary to catch exactly where it goes null.
- **Secondary (separate, easy) UX fix**: `displayComment` / `isAutoChargeDescription` suppress the auto `{Job}:{Player}:…` description but **not** the `"Registration Payment"` / `"eCheck Registration Payment"` defaults, so a comment-less CC/eCheck row shows that boilerplate while Check/Correction show blank. Extend the suppression to treat those two default strings as "no comment" — a comment-less row then renders blank (matching Check/Correction), and a genuine admin comment stands out. (`accounting-ledger.component.ts:249-258`.)

### PL-019: Events screen — a $0 camp/clinic shows no amount; should display "$0" in blue like priced events
- **Tested**: Camps & Clinics Registration
- **Area**: Player registration → Events screen (Camps & Clinics event list) → per-event fee
- **Where**: The per-event fee shown on each camp/clinic card
- **What I did**: Viewed a camp/clinic whose balance due is **$0** on the Events screen.
- **What I expected**: The amount to show as **$0 in blue**, consistent with how priced events display their fee per event.
- **What happened**: A $0 event shows **no amount at all** (blank), while priced events show their fee in blue.
- **Severity**: UX
- **Status**: Fixed
- **Resolved**: Dropped `&& team.effectiveFee > 0` from both fee-render guards — selected-camps [team-selection-step.component.ts:140](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/registration/player/steps/team-selection-step.component.ts#L140) and available-camps [:183](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/registration/player/steps/team-selection-step.component.ts#L183) — now `@else if (team.effectiveFee != null)`. A configured $0 renders `{{ 0 | currency }}` = **$0** in the existing blue `camp-fee` class (no CSS change). Order preserved: `feeConfigured === false` → "Fee not set" stays first (unset ≠ a real $0), and `!= null` still hides a genuinely unresolved fee. FE-only.
- **For Todd — the fix (one token, two spots)**: The blue `camp-fee` span renders only when `effectiveFee > 0`, so a configured $0 falls through to nothing. Change `@else if (team.effectiveFee != null && team.effectiveFee > 0)` → `@else if (team.effectiveFee != null)` in **both** the selected-camps list ([team-selection-step.component.ts:140](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/registration/player/steps/team-selection-step.component.ts#L140)) and the available-camps list ([:183](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/registration/player/steps/team-selection-step.component.ts#L183)). A configured $0 event then renders `{{ 0 | currency }}` = **$0** in the existing blue `camp-fee` class — no CSS change. The `feeConfigured === false` → "Fee not set" branch stays first (an *unset* fee is different from a configured $0), and the `!= null` guard still hides a genuinely unresolved fee.

### PL-020: "Successfully applied discount" message lingers on the payment screen with no code entered
- **Tested**: Camps & Clinics Registration (job: YJS:Camps and Clinics 2026)
- **Area**: Player registration → payment step → Discount Code section
- **Where**: The green "Successfully applied discount to N registration(s)" message under the (empty) Discount Code field
- **What I did**: Reached the payment screen where a **late fee** applied. The Discount Code field is empty and I did **not** enter a code, yet "Successfully applied discount to 2 registration(s)" shows.
- **What I expected**: No discount message when no code has been applied to the current registration.
- **What happened**: The success message displays even though the code field is empty and no code was entered for this view.
- **Also confirmed in — ARB Players Registration (2026-07-21)**: same green "Successfully applied discount to 2 registration(s)" shows on the ARB payment screen **before any code or payment is entered**. This is **cross-job-type**, exactly as the singleton root cause below predicts (a discount applied in an earlier registration this session leaks forward). Ann: *"This needs to be removed."* Confirms it's not job-specific — raises priority.
- **Severity**: Bug (misleading stale message)
- **Status**: Fixed
- **Resolved**: Added `this.paySvc.resetDiscount()` to `PaymentStepComponent.ngOnInit` ([payment-step.component.ts:1206](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/registration/player/steps/payment-step.component.ts#L1206)), beside the existing `resetDonation()` — the same sibling reset already deemed sufficient for donation staleness (same singleton lifetime). Each fresh payment view now starts with no stale discount message; a discount actually applied during the view still shows (re-set by `applyDiscount`). Minimal fix — did not add the extra `billableLineItems()`-change reset (unneeded for the reported case). FE-only.
- **Root cause**: `PaymentV2Service` is `@Injectable({ providedIn: 'root' })` — a **singleton** ([payment-v2.service.ts:123](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/registration/player/state/payment-v2.service.ts#L123)) — so `_discountMessage` / `_discountAppliedOk` persist across registrations and navigations. The message is cleared **only** in `resetDiscount()` and at the start of a new `applyDiscount()`, and `resetDiscount()` is called from **just one place**: `chooseOption()` (PIF/Deposit/ARB pick, [payment-step.component.ts:1294](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/registration/player/steps/payment-step.component.ts#L1294)). It is **not** reset when the payment step is entered or when the billable line items change. So a success message from an **earlier** discount apply in the same page session lingers, and the template shows it whenever it's non-null ([payment-step.component.ts:464](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/registration/player/steps/payment-step.component.ts#L464)). (The late fee is **incidental** — the message originates from an earlier successful apply, not the late fee; `applyDiscount` won't fire on an empty code — guarded at `payment-step.component.ts:1299`.)
- **For Todd — the fix**: Reset the discount UI state when it's no longer relevant to the current view — e.g. call `paySvc.resetDiscount()` on payment-step init (and/or when `billableLineItems()` changes / a new family context loads), mirroring what `chooseOption()` already does. Optionally also clear it when the code input is emptied. Net: each fresh payment view starts with no stale discount message.

### PL-015: "Check Owed" column — behavior confirmed correct; discuss its appearance with Todd
- **Tested**: Showcase Registration
- **Area**: Player popup → Accounting → Account Summary (per-method owed columns; eCheck enabled)
- **Where**: The "Check Owed" column that appears alongside CC Owed / eCheck Owed when eCheck is activated
- **What I did**: Reviewed the Account Summary on a job with eCheck on.
- **Behavior — confirmed CORRECT**: "Check Owed" is proc-free — it's the amount owed for a physical check, and it correctly excludes **eCheck processing AND CC processing**. It flows from the single canonical resolver `PaymentState.ResolveOwed`: `Check = OwedFor(0m)`, documented "Check == Cash == Correction — all proc-free" ([PaymentState.cs:325-337, 353-354](../../TSIC-Core-Angular/src/backend/TSIC.Contracts/Payments/PaymentState.cs#L325)). Order is Check < eCheck < CC owed, as intended.
- **Severity**: UX (discussion — no math/logic change)
- **Status**: Won't Fix (leave as-is)
- **Decision (Todd)**: Leave the three owed columns as they are. Reviewed with Todd — the math is confirmed correct (Check = proc-free, eCheck = base + ~1.5% ACH, CC = base + ~3.5% card; three views of the same owed amount via `PaymentState.ResolveOwed`), and the three-column scenario view is intentional for an admin ledger (seeing each method's settlement cost at a glance is the point). Considered but declined a group-header relabel ("Owed by Method" over Card/eCheck/Check) to fix the "Check Owed"/"eCheck Owed" read-alike + width — not worth a change; no behavior issue. Revisit only if it becomes a real admin complaint.
- **For Todd (discussion)**: The math is right; this is purely about **presentation** of the three owed columns (CC Owed / eCheck Owed / Check Owed) when eCheck is on. Ann wanted to review the column's appearance — e.g. label clarity ("Check Owed" vs "eCheck Owed" read similarly), whether all three columns should always show or be consolidated, and visual treatment/width. Renders in two modes: wizard shows ONE method-adaptive column; admin search ledger shows all three ([registered-teams-grid.component.ts:128-154, 557-570](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/registration/team/components/registered-teams-grid.component.ts#L128)).

### PL-016: Account Summary — show a Fee-Adj that includes a Late Fee in RED (mirror the green used for decreases)
- **Tested**: Showcase Registration
- **Area**: Player popup → Accounting → Account Summary (registered-teams / event breakdown grid) → Fee-Adj column
- **Where**: The Fee-Adj amount when it reflects a **late fee** (a net increase)
- **What I did**: Looked at the Fee-Adj value on a registration carrying a Late Fee.
- **What I expected**: A Fee-Adj that increases the amount (late fee) shown in **red**, symmetric with how a decrease (discount) shows in **green**.
- **What happened**: A negative Fee-Adj (decrease) is green; a positive Fee-Adj (late-fee increase) shows in the **default color**, not red.
- **Severity**: UX (color)
- **Status**: Fixed
- **Resolved**: Fee-Adj now colors symmetrically — decrease green, increase (late fee) red — on both the cell ([registered-teams-grid.component.ts:125](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/registration/team/components/registered-teams-grid.component.ts#L125)) and the column sum ([:208](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/registration/team/components/registered-teams-grid.component.ts#L208)). Implemented with the file's own `[style.color]` + `--brand-success`/`--brand-danger` idiom (the same tokens the owed columns at :131/:213 already use) rather than a Bootstrap `.text-*` class — this also retired the lone `.text-success` class on Fee-Adj. Palette-responsive, color reinforces the already-signed amount (not color-alone). Shared grid (family-payment + club-rep) — same convention everywhere, as intended.
- **For Todd — the change**: The cell colors green only on a decrease: `<span [class.text-success]="data.feeAdj < 0">{{ data.feeAdj | currency }}</span>` ([registered-teams-grid.component.ts:125](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/registration/team/components/registered-teams-grid.component.ts#L125)). Add the symmetric red for an increase — `[class.text-danger]="data.feeAdj > 0"` — and do the same on the column sum at [:208](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/registration/team/components/registered-teams-grid.component.ts#L208) (`[class.text-danger]="sumFeeAdj() > 0"`). Note this grid is shared (family-payment + club-rep views), so the change applies everywhere the Fee-Adj column shows — confirm that's desired (it should be — same decrease-green/increase-red convention).

### PL-021: College Recruiting fields show when Recruiting Grad Years is EMPTY — should be hidden on a non-recruiting event ⚠ CONFLICTS WITH A DELIBERATE FIX (02894891)
- **Tested**: Tournament — Player Self-Roster (job: **Live Love Lax: Girls Fall Festival 2026**)
- **Area**: Player registration → profile step → **College Recruiting** fieldset visibility
- **Where**: The recruiting-field gate when the job's **Recruiting Grad Years** (`JsonOptions.List_RecruitingGradYears`) has **no** dropdown entries
- **What I did**: On this job, left **Recruiting Grad Years empty** (no grad years configured), then ran a self-roster registration.
- **What I expected**: With no recruiting grad years configured, this isn't a recruiting event — the **College Recruiting fields (height/weight/SAT/GPA/etc.) should be hidden**.
- **What happened**: The College Recruiting fields **show anyway**.
- **Severity**: Bug — but a **design conflict**, not a straight defect (see below). Do NOT just flip the boolean.
- **Status**: Fixed
- **Decision (Todd)**: **Option A** — the Recruiting Grad Years list is the source of truth for "is this a recruiting event." Empty ⇒ NOT recruiting ⇒ hide the recruiting fields. The festival (PP20) is already correctly configured with grad years empty; the code just wasn't honoring it. Genuine recruiting jobs (PP35/PP27) already value grad years, so they're unaffected. Accepted trade-off: a recruiting job that forgets to set grad years will hide its required recruiting fields (config invariant — value the grad years). Declined the `field.required` safety-net (Option B) in favor of the simple, source-of-truth-driven rule.
- **Resolved**: [player-forms.service.ts:377](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/registration/player/state/player-forms.service.ts#L377) empty-list branch changed from `return true` to `return false`; comment rewritten to state the grad-years-list-is-source-of-truth model and the config invariant. FE-only.
- **⚠ This is intentional current behavior**: The gate returns "show" on an empty list by design — [player-forms.service.ts:377](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/registration/player/state/player-forms.service.ts#L377) (`if (recruitingGradYears.length === 0) return true;`), added in **commit 02894891** ("show recruiting fields when no grad-years gate"). Before that, **5ffed45d** hid them on an empty list — and that produced a real, worse defect: a showcase (PP35) marked `heightInches` **required on the backend** but had an empty grad-years list, so the fields were hidden, `PreSubmit` validation failed, and an **all-NULL profile saved after payment was taken**. Reverting to empty=hide **re-opens that defect**: on any job where a recruiting field is backend-required, hiding it makes the registration an **unresolvable dead-end** (post-02894891 it now shows a visible warning and blocks payment — user can't fill a hidden required field).
- **Root cause (the real one)**: `List_RecruitingGradYears` is **overloaded**. It's being asked to answer two different questions at once — (a) *"does this event collect recruiting info at all?"* and (b) *"which grad years are within NCAA contact rules?"* — and **empty** currently resolves to "show for everyone," which leaks the recruiting fieldset onto non-recruiting events like this festival.
- **VERIFIED against dev TSICV5 (Ann confirmed: NOT a recruiting event)**:
  - Girls Fall Festival 2026 uses profile **`PP20|BYCLUBNAME`** (jobPath `livelovelax-girlsfallfestival-2026`). PP20 is the **standard festival/tournament profile — 178 jobs use it** — and PP20's template *carries the recruiting block by design* (the canonical recruiting field list literally = "PP20.cshtml's recruittinginfo block"). So **Option B is out**: there's no clean non-recruiting sibling to swap to; PP20 *is* the profile these festivals use.
  - **On PP20 every recruiting field is `validation.required = false`** (checked heightInches, satMath, position, schoolName, gpa, etc. — all optional, with sane min/max bounds now present post-remigrate).
  - The showcase profiles that DO mark recruiting fields **required** are **different profiles** — **PP35** (`position, heightInches, schoolName` required) and **PP27** (`position, schoolName` required) — i.e. genuine recruiting events, which populate grad-years anyway.
  - **Why this matters**: 02894891's silent-loss/dead-end defect *only* fires when a recruiting field is **required-but-hidden**. On PP20 nothing is required, so **hiding recruiting fields when grad-years is empty is safe here** — no PreSubmit failure, no all-NULL save, no dead-end.
- **For Todd — recommended fix (surgical, safe, no showcase regression)**: Change the empty-list branch at [player-forms.service.ts:377](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/registration/player/state/player-forms.service.ts#L377) from unconditional show to **"show only if the field is required"** — i.e. `if (recruitingGradYears.length === 0) return field.required === true;`. This satisfies both cases at once:
  - **PP20 festival (optional fields)** → all recruiting fields **hidden** when grad-years empty. ✓ Ann's finding resolved.
  - **Misconfigured showcase (PP35, empty list, required fields)** → the **required** fields (position/heightInches/schoolName) still show, so 02894891's silent-loss protection is preserved; only optional fields hide (harmless — nothing required is dropped). ✓
  - Net principle: *a gate may hide an optional field, but must never hide a required one.* That's the real invariant 02894891 was groping for — this makes it explicit without the blunt "show everything on empty."
  - Verify after change: `heightInches`/`satMath` bounds still enforce when the fields DO show (populated grad-years); showcase reg with empty grad-years still submits.
- **Alternative if Todd prefers explicit config**: a per-job "collect recruiting info?" flag decoupled from the grad-years list. Bigger change; the `field.required` guard above is the minimal fix and resolves this ticket directly.

### PL-022: Legacy College Recruiting field values don't carry forward (prefill) into a new-system registration — fall tournament recruiting players affected
- **Tested**: Tournament — Player Self-Roster (raised while on **Live Love Lax:Girls Fall Festival 2026**); applies to any player with a prior **legacy** registration
- **Area**: Player registration → cross-job form **prefill** (a player's prior registration values pre-populate the new form) → College Recruiting fields (height, weight, SAT, GPA, position, school, etc.)
- **Where**: The College Recruiting fieldset on a fresh registration for a player who entered recruiting info in the **legacy** system
- **What I did / observed**: A player who entered College Recruiting fields under the legacy system does **not** see those values carried forward when they register in the new system. They have to re-enter recruiting info.
- **What I expected**: A returning player's recruiting profile (height, SAT/GPA, school, position, etc.) prefills from their prior registration, as other profile fields do.
- **Severity**: UX / data-continuity — **not data loss** (legacy rows are intact in the DB); the cost is re-entry and risk of incomplete recruiting profiles.
- **Status**: Fixed (Height) + config note (Position); rest already prefilled correctly
- **Systematic field-by-field audit (all 13 PP20 recruiting fields, worked backwards from stored data — only SELECT fields can fail to prefill; text/number inputs always render their stored value)**:

  | Field | New input | Legacy stored | Prefilled? | Resolution |
  |---|---|---|---|---|
  | **HeightInches** | was a forced `f-i` dropdown | raw inches `36–84` (+ dirty) | ❌ majority wiped | **FIXED → NUMBER input** (below) |
  | **Position** | dropdown `JsonOptions.List_Positions` | real off-list values (`LSM, F/O, forward, guard, center`) + junk | ⚠️ off-list wiped | **Config** — widen the job's `List_Positions` to the sport's real positions; junk values correctly stay dropped. No code. |
  | **GradYear** | dropdown `List_GradYears` | 4-digit years (+ 2 junk `Futures`/`Open`) | ✅ years show; junk dropped | None |
  | Act, ClassRank, Gpa, SatMath, SatVerbal, SatWriting, WeightLbs | NUMBER/text | numbers (incl. decimals) | ✅ | None — never cleared |
  | SchoolName, CollegeCommit | text | free text | ✅ | None |
  | BCollegeCommit | checkbox | bool | ✅ | None (linkage = PL-024) |

- **Resolved (Height — the sole representation bug)**: `HeightInches` was a raw-inches NUMBER in legacy (`[Range(36,84)]`, [PP20ViewModel.cs:130](../../reference/TSIC-Unify-2024/TSIC-Unify-Models/ViewModels/RegPlayersSingle_ViewModels/PP20ViewModel.cs#L130)) but the new system force-cast it to a feet-inches `<select>` whose options never matched the stored number, so `clearInvalidSelectValues` wiped every prefill. Fixed at the source: (1) parser now types `HeightInches` as `NUMBER` and drops its `List_HeightInches` data source ([CSharpToMetadataParser.cs](../../TSIC-Core-Angular/src/backend/TSIC.API/Services/Metadata/CSharpToMetadataParser.cs) `InferInputType`/`InferDataSource`); the existing `[Range]` extraction supplies min 36 / max 84. (2) FE stops overriding Height — removed the `f-i` fallback + forced-`select`, added `heightinches` to `numericColumns` ([form-schema.service.ts](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/registration/player/services/form-schema.service.ts)). Height now renders as a `36–84` number typein; stored raw-inch values prefill directly. **Requires a profile remigrate (all templates) for the metadata to take effect** — Todd's op.
- **NOT a code fix (Position)**: off-list legacy positions fail to prefill because the job's `JsonOptions.List_Positions` is narrower than the sport's real positions — widen that DDL per job. Junk free-text (`still figuring it out`) *should* stay dropped.
- **VERIFIED against dev TSICV5**:
  - **The data source exists in bulk.** Legacy recruiting values live in the same `Jobs.Registrations` columns the new prefill reads. Of 663,209 registrations: **126,474** have `height_inches`, **487,802** `satMath`, **597,859** `school_name`, **591,829** `position`, **488,149** `gpa`. So carry-forward is *possible* — the gap is in the pipeline, not a missing source.
  - **The prefill path**: backend `FamilyService.BuildLatestVisibleFieldValues` ([FamilyService.cs:205](../../TSIC-Core-Angular/src/backend/TSIC.API/Services/Families/FamilyService.cs#L205)) walks the player's whole registration history (legacy regs included — same table), takes the **first non-null** value per field, but **only for fields in `visibleFieldNames`** ([:221](../../TSIC-Core-Angular/src/backend/TSIC.API/Services/Families/FamilyService.cs#L221)); it ships as `DefaultFieldValues`. The frontend applies them only into blank fields ([player-forms.service.ts:514-521](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/registration/player/state/player-forms.service.ts#L514)).
  - **CONFIRMED failure — Height.** Legacy `height_inches` is stored as **raw inches** (`53`, `62`, `70`, and even dirty values like `64.40`, `37.21`, leading tabs/spaces). The new-system Height field is a **`select` dropdown** in feet-dash-inches format (`4-0`…`6-10`, `HEIGHT_INCHES_FALLBACK` in [form-schema.service.ts:7](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/registration/player/services/form-schema.service.ts#L7); forced to `select` at [:149](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/registration/player/services/form-schema.service.ts#L149)). Prefill then runs `clearInvalidSelectValues` ([player-forms.service.ts:541](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/registration/player/state/player-forms.service.ts#L541)), which **wipes any select value that matches no option** — legacy `70` ≠ `5-10`, so it's dropped. Height never carries.
  - **Likely OK — plain text/number fields.** SAT (`satMath` etc.) and GPA are numeric/text inputs with no option list, so they should prefill cleanly; matching dropdowns (e.g. `position` = attack/defense/midfield) should carry when the legacy string matches an option. This is why the breakage is **field-specific to whatever changed representation legacy→new**, not a blanket wipe.
- **Root cause (general)**: Any field whose **new-system representation differs from how legacy stored it** loses its prefill — most cleanly demonstrated by Height (raw-inches number → constrained dropdown). The prefill/clear path has **no legacy-value normalization step**, so mismatched formats are discarded rather than coerced.
- **For Todd — fix direction**: Add a **legacy-value normalizer** in the prefill path, before `clearInvalidSelectValues`, for fields whose representation changed. Concretely for Height: map a legacy raw-inch value (round `70` → `70` inches → `5-10`) to the dropdown option, so it matches instead of being cleared. Audit other recruiting selects for the same legacy-vs-option format drift (position/school-grade/college-commit). Numeric-text fields (SAT/GPA) likely need no change — confirm in repro.
- **Repro to pin full scope**: pick a known player with legacy recruiting data (e.g. a `Jobs.Registrations` row with non-null `height_inches`/`satMath`/`school_name`), register them into a fall tournament (grad-years configured so the recruiting fieldset shows), and record which fields prefill vs. blank. That enumerates exactly which fields need normalization.
- **Note**: This is **independent of PL-021** — PL-021 is about *whether* the fieldset shows; PL-022 is about *whether prior values populate it once shown*. Both hit fall tournament recruiting players.

### PL-023: Search Registrations — the **Assignment** column doesn't sort (falls through to name order); obvious on a large self-rostered group
- **Tested**: Tournament — Player Self-Roster (Search / Registrations grid, a large group of self-rostered players)
- **Area**: Search → Registrations → results grid → **Assignment** column header sort
- **Where**: Clicking the Assignment column header to sort
- **What I did**: On a large group of self-rostered players, clicked the **Assignment** column to sort by it.
- **What I expected**: Rows to order by their assignment (age group / team), so like assignments group together.
- **What happened**: The column shows a sort indicator but the rows **don't reorder by assignment** — they stay in name (LastName/FirstName) order.
- **Severity**: Bug — a column advertised as sortable (`allowSorting` on, arrow shows) that silently doesn't sort. Most visible on large result sets.
- **Status**: Fixed
- **Resolved**: Added an `"assignment"` case to the sort switch ([RegistrationRepository.cs:1745](../../TSIC-Core-Angular/src/backend/TSIC.Infrastructure/Repositories/RegistrationRepository.cs#L1745)) ordering by the same components the display string is built from — `ClubRepClubName → AgegroupName → TeamName` — with the `RegistrationId` tiebreaker for stable paging. Verified those are projected DTO scalars (read post-materialization at :1806), so the sort runs server-side on `projected` before Skip/Take. For a homogeneous self-rostered group (Ann's case) `ClubRepClubName` is null across the board, so it collapses to AgegroupName → TeamName = the displayed order. Stale "not DB-backed / falls through" comment updated. Took the minimal component-sort (not the mixed-role exact-string-key variant — deferred as noted in the caveat).
- **Root cause (CONFIRMED, end-to-end)**: The grid sorts **server-side** — the header click sends `sortField:"assignment"` to the backend ([search-registrations.component.ts:490-493](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/search/registrations/search-registrations.component.ts#L490)). The backend sort switch in [RegistrationRepository.cs:1719-1748](../../TSIC-Core-Angular/src/backend/TSIC.Infrastructure/Repositories/RegistrationRepository.cs#L1719) has cases for firstName/roleName/registered/phone/dob/position/paidTotal/owedTotal/lastName **but none for `assignment`**, so it hits the `_ =>` default and orders by LastName, FirstName. The code comment ([:1715-1716](../../TSIC-Core-Angular/src/backend/TSIC.Infrastructure/Repositories/RegistrationRepository.cs#L1715)) rationalizes this as "the computed Assignment column is not DB-backed."
- **Why the fix is straightforward (the comment's premise is only half true)**: The Assignment *string* is composed **post-materialization** from `ClubRepClubName + AgegroupName + TeamName` (space-joined, ClubName fallback) at [:1806-1811](../../TSIC-Core-Angular/src/backend/TSIC.Infrastructure/Repositories/RegistrationRepository.cs#L1806) — but **those component fields are already on the projected DTO** (they have to be, since the post-step reads them off `dto`). So they're available to `OrderBy` in the switch, on the `projected` IQueryable, *before* Skip/Take — fully server-side-paging-safe.
- **For Todd — the fix**: Add an `"assignment"` case to the switch that orders by the same components the string is built from, with the RegistrationId tiebreaker (required for stable paging):
  ```csharp
  "assignment" => desc
      ? projected.OrderByDescending(r => r.Dto.ClubRepClubName).ThenByDescending(r => r.Dto.AgegroupName).ThenByDescending(r => r.Dto.TeamName).ThenBy(r => r.Dto.RegistrationId)
      : projected.OrderBy(r => r.Dto.ClubRepClubName).ThenBy(r => r.Dto.AgegroupName).ThenBy(r => r.Dto.TeamName).ThenBy(r => r.Dto.RegistrationId),
  ```
  For a homogeneous group (e.g. all self-rostered players — Ann's case), ClubRepClubName is null across the board, so this collapses to AgegroupName → TeamName, which **matches the displayed string order**. Update the now-stale "not DB-backed / falls through" comment.
- **Caveat (exact-match option)**: Across a **mixed-role** result set the leading token of the displayed string differs by role (rep rows lead with club, players with age group), so the component sort won't reproduce the literal string order perfectly, and the ClubName *fallback* rows (no team) sort under the null-club/age/team group rather than by their shown ClubName. If Todd wants the sort to match the displayed string exactly, build a single **sort-key expression** in the EF projection (the same `parts`-join with `ClubName` fallback, as a projected column) and order by that. Bigger change; the component sort above resolves the reported case and is the minimal fix.

### PL-024: College Recruiting — the "committed to a college?" checkbox and the college-name field aren't linked; a name entered with the box unchecked is silently dropped from reports
- **Tested**: Tournament — Player Self-Roster (College Recruiting fieldset); applies to every job on the PP20 recruiting block
- **Area**: Player registration → College Recruiting → **"Have you committed to a college?"** (`bCollegeCommit`, checkbox) + **"College you have committed to"** (`collegeCommit`, text)
- **Where**: The two adjacent college-commit fields
- **What I did**: Left the **"Have you committed to a college?"** checkbox **unchecked** but still typed a **college name** in the text field.
- **What I expected**: The college-name field should only accept input when the box is checked — the two should be linked (or drop the checkbox).
- **What happened**: The college-name field accepts a value independently of the checkbox — you can enter a college without ever confirming commitment.
- **Severity**: Bug — and worse than cosmetic: it causes **silent data loss on reports** (see below).
- **Status**: Won't Fix (leave as-is) — see decision
- **Decision (Todd)**: **Leave it.** On review, the dependent-field fix (option 2) is a trap with far more blast radius than it looks, and the underlying data risk is already backstopped. **For Ann — why:**
    1. **The recruiting gate short-circuits any condition on `collegeCommit`.** `collegeCommit`/`bcollegecommit` are in `RECRUITING_FIELD_NAMES`, so the visibility gate returns inside the recruiting branch ([player-forms.service.ts:371-381](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/registration/player/state/player-forms.service.ts#L371)) **before** the `field.condition` check at :382. A wired condition would never even be evaluated.
    2. **It compounds with PL-021.** `collegeCommit` now shows only on recruiting events (grad years configured); gating it *also* on the checkbox makes the real rule compound — (recruiting event) AND (box checked) — i.e. the recruiting branch itself would have to apply the field's condition, not just carry one.
    3. **Two condition conventions.** The player parser reads only `f.condition` ([form-schema.service.ts:158](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/registration/player/services/form-schema.service.ts#L158)); metadata carries `conditionalOn` (the adult path reads that). And the condition originates in the profile *template* (`ApplyConditional`, [ProfileMetadataService.cs:183](../../TSIC-Core-Angular/src/backend/TSIC.API/Services/Metadata/ProfileMetadataService.cs#L183)), across two metadata pipelines — a template edit + remigrate, not a one-line change.
    4. **The data loss is already prevented at the report.** The packed-roster PDF only draws the college when `bCollegeCommit` is checked ([PackedRosterPdfService.cs:568](../../TSIC-Core-Angular/src/backend/TSIC.API/Services/Reporting/PackedRosterPdfService.cs#L568)) — so an unconfirmed college is *correctly* dropped today. The report is doing the right thing; the form allowing the contradictory state is cosmetic given that backstop.
  Net: high-risk change touching the gate we just fixed (PL-021) + parser + templates, for a UX nicety the report already guards. Not worth it. If it ever resurfaces as a real complaint, the cheap path is a submit-time reconcile (clear `collegeCommit` when `bCollegeCommit` is false) at one backend write chokepoint — not the dependent-field wiring.
- **VERIFIED against dev TSICV5 metadata**: Both fields are `visibility: public` with **`conditionalOn: null`** — no dependency link exists. `bCollegeCommit` = CHECKBOX "Have you committed to a college?"; `collegeCommit` = TEXT "College you have committed to".
- **Why this matters (the real impact)**: Reporting already treats **`bCollegeCommit` as the source of truth** and gates the name on it. The packed-roster PDF only draws the college when the box is checked ([PackedRosterPdfService.cs:568](../../TSIC-Core-Angular/src/backend/TSIC.API/Services/Reporting/PackedRosterPdfService.cs#L568)) and the shaper zeroes it out otherwise (`committed = BCollegeCommit == true; CollegeCommit = committed ? … : ""` — [:980-1005](../../TSIC-Core-Angular/src/backend/TSIC.API/Services/Reporting/PackedRosterPdfService.cs#L980)). So a player who **types a college but doesn't check the box** has that college **silently discarded** on the roster — the exact inconsistency the form currently allows.
- **Why option 1 (remove the checkbox) is the wrong call**: `bCollegeCommit` is the committed-flag that reporting (packed roster, `TournamentRosterRowDto.BCollegeCommit`) depends on. Dropping it would break the report's commit gate and force a rework to infer "committed" from a non-empty name. Keep the checkbox.
- **For Todd — recommended fix (option 2: gate the name on the box)** — two parts, because the player path has a wiring gap:
  1. **Wire `conditionalOn` into the player form parser.** `FormSchemaService.parse` reads only `f.condition` ([form-schema.service.ts:180-181](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/registration/player/services/form-schema.service.ts#L180)) and never `f.conditionalOn` — so a metadata condition is **inert on the player form today** (the *adult* profile step already honors `conditionalOn`, [profile-step.component.ts:493](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/registration/adult/steps/profile-step.component.ts#L493)). Map `conditionalOn` → the schema's `condition` in the player parse so both paths agree. The visibility gate then hides `collegeCommit` when the condition is false ([player-forms.service.ts:381-384](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/registration/player/state/player-forms.service.ts#L381)).
  2. **Set the condition on `collegeCommit`** → `conditionalOn = { field: "bCollegeCommit", value: true, operator: "equals" }`, emitted by the profile migration so **all** PP20 jobs pick it up on remigrate (not hand-edited per job).
- **Fallback if the parser wiring is deferred**: a submit-time reconciliation — if `collegeCommit` is non-empty while `bCollegeCommit` is false, either auto-set the flag true or clear the name — so the stored data can't contradict the report's gate. The dependent-field approach is better UX (prevents the bad state at entry); this is the stopgap.

### PL-025: Waitlist crossing during registration is only surfaced by a transient 12s toast at payment — easy to miss; and intra-cart contention isn't flagged before Submit
- **Tested**: Camps & Clinics Registration — waitlist behavior (set one camp's max to **1**, selected it for **2 players**)
- **Area**: Player registration → Events/team selection → Review → **Payment** → waitlist notification
- **What I did**: Set max = 1 on a camp, selected that camp for **two** players in one registration, clicked **Submit Registration**, landed on the Payment screen.
- **What I expected**: To clearly know that the 2nd player would be waitlisted — ideally *before* Submit, and if not, via a notice I can actually read.
- **What happened**: The 2nd registration was correctly placed on the **Waitlist** and a **gold popup** described it — but it **auto-dismissed before I could read it**. (Accounting handled it **perfectly** — this is purely a notification-UX issue, no math/logic problem.)
- **Severity**: UX (accounting correct)
- **Status**: ✅ Persistent notice DONE — pre-Submit intra-cart warning deferred (see below)
- **RESOLVED (persistent post-Submit notice)** — the transient 12s gold toast is gone. On payment success the waitlisted players (`response.needsWaitlist`) now ride into `lastPayment.waitlisted`, and the **confirmation screen** renders a **persistent** amber banner that stays on screen: header count, "these were **not charged** and were placed on the waitlist," and a per-player list each tagged with a **WAITLISTED** badge (`playerName — teamName`). Nothing auto-dismisses, so a family can't miss which kids still need to finish waitlist signup.
  - Files: [payment-step.component.ts:1594](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/registration/player/steps/payment-step.component.ts#L1594) (toast removed; `waitlisted` carried into `setLastPayment`), [player-wizard.types.ts:99](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/registration/player/types/player-wizard.types.ts#L99) (`PaymentSummary.waitlisted?`), [confirmation-step.component.ts:42](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/registration/player/steps/confirmation-step.component.ts#L42) (persistent banner + `waitlisted` computed).
  - **Deferred (Todd's call):** the *pre-Submit intra-cart contention* warning (flag when two of your own cart picks will collide on a team whose server roster count is still 0). That's a separate, additive improvement in the selection/Review step; not built here.
- **VERIFIED — current behavior**:
  - **Already-full team IS flagged before Submit**: selecting a team that's full at selection time shows a **persistent** "Waitlist Only — this team is currently full…" alert in the selection step ([team-selection-step.component.ts:211-216](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/registration/player/steps/team-selection-step.component.ts#L211)).
  - **Intra-cart contention is NOT flagged before Submit**: in Ann's case the camp's roster count was **0** when each player was selected, so `isSelectedTeamWaitlisted` was false for both — the system can't tell from the server roster count that two of *your own* picks will collide. The over-capacity is only resolved at payment reserve.
  - **The payment-time notice is a transient toast**: `this.toast.show(…, 'warning', 12000)` ([payment-step.component.ts:1598-1601](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/registration/player/steps/payment-step.component.ts#L1598)) — a 12-second auto-dismiss. Nothing persistent on the payment screen marks which player was waitlisted (the warning banners at [:541](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/registration/player/steps/payment-step.component.ts#L541)/[:598](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/registration/player/steps/payment-step.component.ts#L598) are insurance/check, not waitlist).
- **Ann's Q1 — should a WL crossing be known before Submit?** Pre-existing-full already is (persistent selection alert). Intra-cart contention isn't, and can't be from server data alone — but the **client can detect it**: if a capacity-limited event is selected for **more players than seats remaining** (`selectedCountForEvent > maxCount − currentRoster`), warn at **selection and/or on the Review screen** ("2 players selected for an event with 1 seat — 1 will be waitlisted"). Recommended enhancement.
- **Ann's Q2 — make the notification more persistent/obvious.** Replace/supplement the 12s toast with a **persistent** element on the Payment (and Confirmation) screen: a sticky `alert-warning` banner **and/or a per-player "WAITLISTED" badge** on the affected line item, reusing the same persistent `.waitlist-alert` treatment already used at selection ([team-selection-step.component.ts:212](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/registration/player/steps/team-selection-step.component.ts#L212), [:686](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/registration/player/steps/team-selection-step.component.ts#L686)). This way the family can't miss which child wasn't charged and still needs to finish waitlist signup.
- **For Todd**: two independent, additive improvements — (1) pre-Submit intra-cart contention warning (selection/Review), (2) persistent post-Submit waitlist banner + per-player badge (payment/confirmation). Either alone helps; both together fully close the gap. No accounting change — the seat-split and $0 twin handling are already correct.

### PL-026: 🐛 After a waitlist twin is minted, the C&C event list shows BOTH the real (full) camp AND its "WAITLIST -" twin — only one should appear
- **Refs**: PL-010 / PL-011 (same "persistent minted twin" root cause, at the agegroup level); PL-025 (same waitlist topic)
- **Tested**: Camps & Clinics — **Yellow Jackets South:Camps and Clinics 2026** (`yjsouth-cac-2026`)
- **Area**: Player registration → C&C **Select Event(s)** list
- **Where**: The camp/clinic cards on the event-selection screen, after a waitlist entry has been generated for that camp
- **What I did**: Generated a waitlist item (camp max = 1, registered a 2nd player → waitlisted), then **went back into registration**.
- **What I expected**: Only the **Waitlist** option for that camp should be selectable at that point — not the real (now-full) camp as well.
- **What happened**: The event list shows **BOTH** the actual camp **and** its "WAITLIST -" twin as options.
- **Severity**: Bug
- **Status**: 🔁 Fixed in code (built green, pushed) — **awaiting Ann retest** (not yet run E2E)
- **ANN — retest (deploy + API restart first), one camp job AND one single-team job**:
  1. Set a team/camp max = 1. Register player A (takes the seat), then player B → B is waitlisted.
  2. Re-enter registration as B → **confirm** you see B's placement badged **"Waitlisted · $0"** (not a phantom empty "1 selected").
  3. Switch B to a team that has an open seat, Submit.
  4. **The thing that must be true:** B lands **pending/unpaid** on the new team — owes the fee, NOT confirmed at $0. If B shows confirmed without paying, flag it (the BActive reset didn't take).
- **RESOLUTION (2026-07-21)**:
  - **The reported "both rows show" symptom is already gone** — our PL-010/011 fix filters any `WAITLIST`-agegroup team out of `GetAvailableTeamsQueryResultsAsync` ([TeamRepository.cs:154-155](../../TSIC-Core-Angular/src/backend/TSIC.Infrastructure/Repositories/TeamRepository.cs#L154)), the single query feeding BOTH the agegroup dropdown and the C&C event list. The twin can no longer appear as a second row. **Ann: retest on the deployed build.**
  - **The deeper bug that filter exposed (now FIXED):** a **previously-registered player who was moved to a WAITLIST team** is stranded. Their registration's `assignedTeamId` is the waitlist team — which the list now excludes — so the picker seeds a selection it can't resolve: a phantom "1 selected" that renders nothing, their real placement invisible, and their team re-offered as if new. It's *just a team the player is on*; the picker simply refused to show any team not also offerable as a fresh pick.
  - **Decision (Todd):** a waitlisted player **CAN switch teams** — a $0 waitlist spot is a queue ticket, not a paid commitment.
  - **Fix — CAC (camps):** `getWaitlistPlacementCamps` rebuilds the player's own placement from their registration and `getSelectedCamps` re-adds it (selected-render path only, base list & backend filter untouched); `isCampAlreadyRegistered` excludes waitlist placements so the card stays deselectable/switchable; rendered badged **"Waitlisted · $0"**.
  - **Fix — PP (single-team):** symmetric — `isPlayerLocked` excludes waitlist placements (so the existing team dropdown renders instead of a locked label) and `getTeamDropdownItems` injects the placement as its current value.
  - **Fix — backend (switch safety):** on any team change, the registration resets to `BActive=false` so switching a $0 waitlist spot onto a real, fee'd team can't grant a **confirmed seat without payment** ([PlayerRegistrationService.cs:478](../../TSIC-Core-Angular/src/backend/TSIC.API/Services/Players/PlayerRegistrationService.cs#L478) CAC, [:413](../../TSIC-Core-Angular/src/backend/TSIC.API/Services/Players/PlayerRegistrationService.cs#L413) PP). PP's active-team lock now excepts waitlist placements, detected **canonically** by agegroup (`AgegroupConstants.WaitlistPrefix`, new `ITeamRepository.GetWaitlistTeamIdsAsync`) — no display-string sniffing.
- **VERIFIED against dev TSICV5**: `yjsouth-cac-2026` now has both rows in `Leagues.teams` — **"YJS TEST Summer Camp"** (agegroup "Programs", max 1, full) **and** its minted **"WAITLIST - YJS TEST Summer Camp"** (agegroup "WAITLIST - Programs", max 100000) — both `Active = 1`.
- **Root cause (CONFIRMED — same as PL-010/011, at the C&C event level)**: The design invariant is "one entry per camp — the REAL team, badged '⚠ WAITLIST · $0'; the twin is payment-time plumbing minted at PreSubmit" ([team-selection-step.component.ts:869-872](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/registration/player/steps/team-selection-step.component.ts#L869)). Once the twin is minted as a **real DB row**, the availability query returns it as a **second** entry ([TeamRepository.cs:134-166](../../TSIC-Core-Angular/src/backend/TSIC.Infrastructure/Repositories/TeamRepository.cs#L134)), and the C&C list builder `getAvailableTeamDtos` only **sorts** — it never collapses the real+twin pair ([:864-898](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/registration/player/steps/team-selection-step.component.ts#L864)). The agegroup dropdown DOES collapse this (`team.service.ts` — "a full agegroup shows ONLY its $0 waitlist team", [team.service.ts:99-102](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/registration/player/services/team.service.ts#L99), [:166-178](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/registration/player/services/team.service.ts#L166)); the C&C event path has no equivalent.
- **Important nuance (why not just hide the twin)**: the "WAITLIST -" mirror agegroup is **deliberately surfaced** so a player already placed on the twin can **resume** onto it ([TeamRepository.cs:147-149](../../TSIC-Core-Angular/src/backend/TSIC.Infrastructure/Repositories/TeamRepository.cs#L147)). So the collapse must be **context-aware**, not a blanket suppression.
- **Extra wrinkle for the already-waitlisted player**: `isCampAlreadyRegistered` matches on `assignedTeamId === team.teamId` ([:1028-1032](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/registration/player/steps/team-selection-step.component.ts#L1028)). The waitlisted player's registration is on the **twin** id, so the **real** camp card is NOT marked already-registered — meaning they can re-pick the full real camp (double-registration risk), which is exactly the "both show" symptom from the user's seat.
- **For Todd — the fix**: Collapse the real+twin pair to a **single** C&C event entry, context-aware (mirror the agegroup logic, or — better per PL-011 — do it once in the backend `GetAvailableTeamsQueryResultsAsync`/`TeamLookupService` so every consumer is consistent):
  - Player **already on the twin** → show only the twin (their resumable waitlist placement).
  - Player **not** on it → show only the real camp badged "⚠ WAITLIST · $0" (the designed selectable; twin stays payment-time plumbing).
  - Never both. Also map a twin registration back to the real camp in `isCampAlreadyRegistered` so a waitlisted player sees the camp as already-registered/disabled rather than re-pickable.
- **Note**: Accounting is unaffected (consistent with PL-025 — the seat-split/$0-twin math is correct); this is a **selection-list de-duplication** bug. Resolving it together with PL-010/PL-011 is natural — one collapse rule, applied where the available-teams list is built, fixes agegroup dropdowns and C&C events alike.

### PL-027: ARB Subscription Details — replace the "Stored record… shown in Production" note with a testable **Cancel Subscription** action (and don't show the dev note in Production)
- **Tested**: ARB Players Registration — Search / Registrations → player popup → **Subscription Details**
- **Area**: Search → Registrations → registration detail panel → ARB subscription card
- **Where**: The `sub-stored-note` under the subscription fields ([registration-detail-panel.component.html:483-486](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/search/registrations/components/registration-detail-panel.component.html#L483)): *"Stored record — live Authorize.Net status is shown in Production."*
- **What I did**: Opened an ARB player's Subscription Details in the Search/Registrations popup (dev).
- **What I expected**: This dev-explainer note to be gone once the site is live in Production.
- **What happened**: The note shows (expected in dev). Flagging it must **not** appear in Production.
- **Severity**: UX / release-readiness
- **Status**: ✅ Note FIXED (built green, pushed) — Cancel-in-Sandbox deferred (Ann's refinement, separate)
- **RESOLUTION (2026-07-21)** — the note is a **non-prod reminder**, so it's now gated on the environment, not on `subscriptionIsLive`: `@if (!isProdEnv)` ([registration-detail-panel.component.html:483](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/search/registrations/components/registration-detail-panel.component.html#L483)), with `isProdEnv` made template-readable ([registration-detail-panel.component.ts:804](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/search/registrations/components/registration-detail-panel.component.ts#L804)). In Production it now never renders — killing both the open-flash and the wrong-wording-on-failed-read; the failed-read case is already covered by the existing toast ([:849](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/search/registrations/components/registration-detail-panel.component.ts#L849)). Non-prod behavior unchanged. No loading flag or new copy needed. **Applied to all three cards** carrying the note — registration-detail-panel, [family-payment.component.html:129](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/shared-ui/components/family-payment/family-payment.component.html#L129), and [team-detail-panel.component.html:285](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/search/teams/components/team-detail-panel.component.html#L285) (`isProdEnv` exposed on each) — so launch behavior is consistent. **Cancel Subscription button: no change for now** (Ann's Sandbox-testability ask is a separate enhancement, notes below).
- **VERIFIED — how it's gated**: The note renders `@if (!subscriptionIsLive())`. `subscriptionIsLive` starts **false** ([registration-detail-panel.component.ts:828](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/search/registrations/components/registration-detail-panel.component.ts#L828)). Only in Production (`isProdEnv = environment.envName === 'production'`, [:804](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/search/registrations/components/registration-detail-panel.component.ts#L804)) does `loadSubscription()` auto-fire on detail load / accounting-tab open ([:323](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/search/registrations/components/registration-detail-panel.component.ts#L323), [:353-354](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/search/registrations/components/registration-detail-panel.component.ts#L353)); on success it sets `subscriptionIsLive = true` ([:841](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/search/registrations/components/registration-detail-panel.component.ts#L841)) → **note hides**. Off-Production the live call is never made (the snapshot is the honest source) → the note always shows. **So for a healthy ARB in Production the note does disappear on its own** — it won't linger the way it does in dev.
- **Two edge cases that still show it in Production**:
  1. **Transient flash**: `subscriptionIsLive` starts false and the live read is async, so the note appears for a beat on open until the fetch resolves. No loading guard on the `@if`.
  2. **Live-read failure**: if the Authorize.Net call fails in Production, `subscriptionIsLive` stays false ([:847](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/search/registrations/components/registration-detail-panel.component.ts#L847)) → the note shows — and its wording *"…is shown in Production"* is written for the **dev** case, so it reads wrong while actually in Production.
- **For Todd — the fix**: make the note environment-/state-aware so it never shows the dev wording in Production:
  - **Loading guard** — don't render the note until the live read has resolved (track a `subscriptionLoading`/`liveChecked` flag; show a small spinner or nothing while in flight). Kills the flash.
  - **Production failure copy** — when `isProdEnv` and the live read failed, show a *"Live status unavailable — showing stored record"* message (matches the existing toast at [:849](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/search/registrations/components/registration-detail-panel.component.ts#L849)) instead of the "shown in Production" wording. Reserve the current dev note for off-Production only (`@if (!isProdEnv && !subscriptionIsLive())`).
- **Same note lives elsewhere** — apply the same treatment to the identical string in [family-payment.component.html:131](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/shared-ui/components/family-payment/family-payment.component.html#L131) (and the team-panel variant at [team-detail-panel.component.html:287](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/search/teams/components/team-detail-panel.component.html#L287)) so the launch behavior is consistent across all ARB/subscription cards.
- **Ann's refinement (2026-07-21) — the note's spot should host a *testable* Cancel Subscription; "I can't test this in Sandbox."**
  - **The Cancel action already exists but is Production-only**: the note sits exactly where the **Cancel Subscription** button renders — `@if (subscriptionIsLive() && sub.status === 'active')` ([registration-detail-panel.component.html:490-494](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/search/registrations/components/registration-detail-panel.component.html#L490)). Because `subscriptionIsLive` only flips true after the Production live read, **off-Production the note shows *instead of* Cancel, and Cancel can never be exercised** (comment [:488-489](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/search/registrations/components/registration-detail-panel.component.html#L488): "Off-Production the stored snapshot is display-only"). That's the testability gap Ann hit.
  - **The plumbing to fix it already exists**: the backend carries **Authorize.Net Sandbox** credentials (`AdnSettings.SandboxLoginId` / `SandboxTransactionKey`, [AdnSettings.cs:9-10](../../TSIC-Core-Angular/src/backend/TSIC.API/Configuration/AdnSettings.cs#L9)) and an `IsSandbox()` gate ([HostEnvironmentExtensions.cs:14](../../TSIC-Core-Angular/src/backend/TSIC.API/Extensions/HostEnvironmentExtensions.cs#L14)); **Staging already queries the Sandbox ADN account** ([AdnReconciliationController.cs:144](../../TSIC-Core-Angular/src/backend/TSIC.API/Controllers/AdnReconciliationController.cs#L144)). The subscription endpoints ([RegistrationSearchController.cs:353-368](../../TSIC-Core-Angular/src/backend/TSIC.API/Controllers/RegistrationSearchController.cs#L353)) are not controller-env-gated — the Production restriction is a frontend `isProdEnv` gate plus (to confirm) the service/gateway account selection.
  - **For Todd — enhancement**: let the ARB **live-status read + Cancel Subscription run in Staging against Authorize.Net Sandbox**, so the whole cancel flow is testable before go-live and the "Stored record" note is replaced by a working Cancel button in Staging (backed by sandbox subs created via the existing sandbox registration tooling). Two parts: (1) frontend — broaden the `isProdEnv` gate on `loadSubscription()`/Cancel to also allow Staging-sandbox (e.g. an `env.adnSandbox` flag), and (2) backend — confirm `GetSubscriptionDetailAsync` / `CancelSubscriptionAsync` resolve the **Sandbox** ADN credentials off-Production (rather than returning null/404), so a sandbox ARB sub reads live and cancels. In Production the flow is unchanged (Cancel already appears once the live read succeeds). **Ann can't verify the cancel path at all until this is enabled in Staging.**

### PL-028: 🐛 A subscription cancelled AT Authorize.Net (not via the app's Cancel button) never syncs back to the player's stored data — status stays "active" forever
- **Refs**: PL-027 (same ARB subscription card / cancel flow)
- **Tested**: ARB Players Registration — cancelled a subscription in the **Authorize.Net Sandbox portal**, then viewed the player.
- **Area**: ARB subscription status sync (Authorize.Net → `Registrations.AdnSubscriptionStatus`)
- **What I did**: Cancelled an ARB subscription **at Authorize.Net (Sandbox)** — i.e. externally, not through the app's Cancel Subscription button.
- **What I expected**: The cancellation to **carry over to the player's data** (stored status → canceled, no more "active"/scheduled autopay), **as legacy does in Production**.
- **What happened**: It **did not carry over** — the player's stored subscription data still reflects the old (active) state.
- **Severity**: Bug (data correctness) — a dead subscription shows as **active**; the header ARB badge, "scheduled autopay," accounting, and reports all key off the stored status and misstate it.
- **Status**: Open
- **Root cause (CONFIRMED)**: The **only** paths that write `AdnSubscriptionStatus` are (a) the app's own **Cancel button** → "canceled" ([RegistrationSearchService.cs:732](../../TSIC-Core-Angular/src/backend/TSIC.API/Services/Admin/RegistrationSearchService.cs#L732)), (b) ARB **creation** → "active" ([PaymentService.cs:2192](../../TSIC-Core-Angular/src/backend/TSIC.API/Services/Payments/PaymentService.cs#L2192)), and (c) the **ADN sweep** ([AdnSweepService.cs:429-438](../../TSIC-Core-Angular/src/backend/TSIC.API/Services/Sweep/AdnSweepService.cs#L429)). The sweep is the only live→stored sync — **but it is transaction-driven**: `RunAsync` enumerates settled **transactions** ([:149](../../TSIC-Core-Angular/src/backend/TSIC.API/Services/Sweep/AdnSweepService.cs#L149), from batch lists), and only syncs a sub's status **while importing a new ARB charge** for it. A **cancellation produces no transaction**, so it never enters the sweep and the status is never updated. Separately, the on-view live read `GetSubscriptionDetailAsync` shows live status but is **display-only — it never persists** ([RegistrationSearchService.cs:637-703](../../TSIC-Core-Angular/src/backend/TSIC.API/Services/Admin/RegistrationSearchService.cs#L637), returns a DTO, no `SaveChanges`). Net: nothing ever writes a **cancellation** back unless it went through the app's own button.
- **Why this matters beyond testing**: Authorize.Net itself **auto-terminates** an ARB sub after too many failed payments — an **external** status change with no new transaction. Under the current design those terminations also never sync, so the system keeps treating a dead subscription as active (wrong autopay/accounting/reporting). This is a production-correctness gap, not just a Sandbox artifact.
- **For Todd — the fix**: add a **subscription-status reconciliation that is independent of transactions** — enumerate registrations (and teams) with a stored `AdnSubscriptionId` whose local status is still active, call `GetSubscriptionStatus` for each, and write back any change (canceled / terminated / expired / suspended) via `UpdateSubscriptionStatusAsync`. Run it on a **schedule in Production** (mirroring legacy's sync). Cheap partial win in the meantime: have `GetSubscriptionDetailAsync` **persist** a detected status change (write back + `SaveChanges` when live ≠ stored) so simply **viewing** the player's Subscription Details in Production reconciles it. Make both testable in Staging/Sandbox (ties to PL-027) so this can be verified before go-live.
- **Note**: The app's own **Cancel Subscription button already syncs correctly** ([RegistrationSearchService.cs:732](../../TSIC-Core-Angular/src/backend/TSIC.API/Services/Admin/RegistrationSearchService.cs#L732)) — the gap is strictly **externally-initiated** status changes (portal cancels, Authorize.Net auto-terminations).

### PL-029: ARB confirmation screen — the "charged the day after registration" red text shows even for PIF payers; reword to be installment-conditional
- **Tested**: ARB Players Registration — on-screen registration confirmation (e.g. Yellow Jackets North 2025-2026)
- **Area**: Player registration → **Confirmation screen** → the red ARB payment notice
- **Where**: The red 18px block on the confirmation, currently: *"**Payment WILL NOT SHOW BELOW because it is charged the day after registration.** **You will receive a transaction receipt when it is processed.**"*
- **What I did**: Completed an ARB-eligible registration and viewed the confirmation.
- **What I expected**: This notice to apply only to **installment (ARB)** payers — a **PIF** (pay-in-full) player is charged at registration, so it doesn't apply to them.
- **What happened**: The notice shows regardless of payment choice, so a PIF player is told their payment "will not show / is charged the day after," which is wrong for them.
- **Severity**: UX / copy (misleading for PIF)
- **Status**: Open
- **Requested reword (Ann)** — make it installment-conditional and fix the last sentence:
  > **If you are paying with an installment subscription, the first payment** WILL NOT SHOW BELOW because it is charged the day after registration.
  > You will receive a transaction receipt **when each installment is processed.**
- **VERIFIED — it's editable content, not code**: the confirmation is server-rendered from the per-job template `Jobs.PlayerReg_ConfirmationOnScreen`, run through token substitution and injected as `[innerHTML]` ([confirmation-step.component.ts:72](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/registration/player/steps/confirmation-step.component.ts#L72); built by [PlayerRegConfirmationService.BuildConfirmationHtmlAsync:171-182](../../TSIC-Core-Angular/src/backend/TSIC.API/Services/Players/PlayerRegConfirmationService.cs#L171)). The red block sits right after the `!F-ADN-ARB` token in the stored HTML. **~70 jobs carry this same line** (dev TSICV5), so it's a shared content string, not a one-off.
- **For Todd — the fix (content, with scope choice)**:
  - **Bulk content update** the ~70 affected `PlayerReg_ConfirmationOnScreen` templates to Ann's reworded text, **and** update whatever default/seed new ARB jobs inherit, so new jobs get the corrected copy. (Per-job editing via the job config confirmation editor is the manual alternative but doesn't scale to 70.)
  - **Better, if feasible**: gate the block on ARB payers using the existing **conditional-token** syntax (the template already uses `?!F-DISPLAYINACTIVEPLAYERS`, so `?`-prefixed conditional blocks exist) — show it only when the family actually chose installments, so PIF payers never see it at all. Confirm the substitution engine supports an ARB/installment conditional; if so this is cleaner than the "If you are paying with…" opener. Ann's reworded copy is the safe fallback that works with no engine change.

### PL-030: NEW optional smart bulletin for ARB sites — explain the installment option, above the USA Lacrosse Membership bulletin
- **Tested**: ARB Players Registration — job landing smart bulletins (e.g. Yellow Jackets North 2025-2026)
- **Area**: Job landing → **Smart Bulletins** band
- **Where**: A new proactive bulletin to sit **above** the "USA Lacrosse Membership Required" notice
- **Request (Ann)**: Add an **optional** smart bulletin for **ARB-enabled** sites that explains the pay-in-full vs. installment choice up front. Final copy (with substitution tokens marked):
  > **ACCEPTED Players welcome to `!Customer:Job`**
  >
  > You have the option to pay in full at registration, or set up automatic payments.
  >
  > If you select automatic payments, **`{N}`** equal installments will be charged as follows — *(`{N}` = number of installments, from the job's Recurring Billing config under **Job → Settings → Payment**)*:
  >
  > Payment #1: the day after you register
  > Payments #2-`{N}`: every month from the date of your 1st payment
  >
  > You will receive a transaction receipt when each payment has been processed.
- **Severity**: Feature
- **Status**: Open — **PL for Todd to review**
- **VERIFIED — how smart bulletins work (so this slots in cleanly)**: each bulletin is a **code section** in [smart-bulletins.component.ts](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/widgets/communications/smart-bulletins/smart-bulletins.component.ts), gated on **lifecycle phase (`CTAS_BY_PHASE`) AND a pulse flag**, and ordered by position in the template. The USA Lacrosse notice is the model: [uslax-info.component.ts](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/widgets/communications/smart-bulletins/uslax-info.component.ts), shown only when the job's profile requires a USA Lacrosse number ([smart-bulletins.component.ts:138](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/widgets/communications/smart-bulletins/smart-bulletins.component.ts#L138)). The new ARB bulletin mirrors this: a sibling component rendered **before** the USA Lacrosse section, gated on an **ARB-available** signal.
- **For Todd — build notes**:
  - **New component** (e.g. `arb-info.component`) placed **above** `uslax-info` in the smart-bulletins template so ordering is correct.
  - **Gate ("optional") — DECIDED (Ann)**: it's a **director on/off toggle** — a Job setting to show/hide this bulletin, because **some directors don't use it**. So the display gate is **toggle ON *and* ARB-enabled job *and* registration-open phase** (don't show it on non-ARB jobs even if the toggle is on). Needs: a new Job setting (persisted flag, surfaced in Job → Settings → Payment or the bulletins config) + carry it onto `JobPulseDto` so the smart-bulletins band can gate on it.
  - **Dynamic content**: `{N}` (installment count) comes from the job's **Recurring Billing** config (Job → Settings → Payment); the component must read it and derive both the "`{N}` equal installments" and the "Payments #2-`{N}`" range from that single value. Job/customer name via the same substitution the confirmation templates use (`!JOBNAME`/customer token — confirm exact token for `!Customer:Job`).
  - **Consistency with PL-029**: keep this bulletin's installment wording aligned with the reworded confirmation copy so the story matches end to end (registration landing → confirmation).

<!-- ═══ Cross-topic items consolidated here so Todd sees them in the payment review. Topic tagged in each. ═══ -->

### PL-031: [LADT] Adding a new entity (e.g. House Team) doesn't scroll the sibling table to it — new row is highlighted but off-screen at the bottom
- **Topic**: LADT editor (filed in the payment punchlist so Todd sees it; surfaced during payment/House-Team testing)
- **Area**: Team Settings / all level grids (League / Age Group / Division / Team sibling tables)
- **What I did**: Added a **House Team** under LADT; the teams table came up.
- **What I expected**: The table to jump to the newly-added team with it highlighted/selected so I can edit it right away.
- **What happened**: The new team was appended at the **bottom** of the list and off-screen — I couldn't see it without scrolling down manually.
- **Severity**: UX
- **Status**: Open
- **Root cause (confirmed)**: The add flow **already selects and highlights** the new entity — after the tree reloads, [ladt.component.ts:323-328](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/ladt/editor/ladt.component.ts#L323) finds the new node and calls `expandAncestors` + `selectNode`, and the sibling grid stamps the `row-selected` class in `onRowDataBound` ([ladt-sibling-grid.component.ts:670-683](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/ladt/editor/components/ladt-sibling-grid.component.ts#L670)). **What's missing is scroll-into-view**: the grid has *no* scroll logic anywhere — `syncSelectedRow` toggles the highlight in place, and its own comment says "no scroll reset" ([:685-691](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/ladt/editor/components/ladt-sibling-grid.component.ts#L685)). So a new row that sorts to the bottom is highlighted but never brought into the viewport. (The highlight already works — only the scroll is absent.)
- **For Todd — the fix**: After the selection is applied, scroll the selected row into view. Two ways:
  - **Targeted (preferred)**: add a one-shot "scroll to newly-added entity" trigger the parent sets only after an **add** (not on ▲/▼ sibling nav), so the deliberate "no scroll on in-place nav" behavior is preserved. In `syncSelectedRow`, once the matching row element is found, call `rowEl.scrollIntoView({ block: 'nearest', behavior: 'smooth' })`.
  - **Simpler**: always `scrollIntoView({ block: 'nearest' })` the selected row on selection change — `block:'nearest'` no-ops when the row is already visible, so ▲/▼ nav to an on-screen sibling won't jump; only off-screen selections scroll.
  - A brief highlight pulse (e.g. a fade animation on `.row-selected`) would make the target even easier to spot, per Ann's "highlighted in some way."

### PL-032: [LADT / self-roster] Can't self-roster onto a team with no club — and LADT gives no way to assign a club to a House Team; the only working path is to seed it via a club rep
- **Topic**: LADT / self-roster setup (filed here for Todd)
- **Refs**: PL-031 (adding a House Team from the tree); relates to self-roster registration (payment testing job type 8)
- **Area**: Team Settings / House-Team setup / self-roster (BYCLUBNAME jobs)
- **What I did**: Added a **House Team** from the LADT tree and looked on the team popup for where to set its **club** (the way CTWLOO shows a **"HOUSE TEAM"** club on theirs). Asked whether the only way to register a House Team is as a club rep.
- **What I expected**: A way from LADT to give the new House Team a club so players can self-roster onto it.
- **What happened**: The team-detail popup has **no "assign club" control**. The only club action is **"Change Club"**, and it's gated to teams that **already** have a club rep ([team-detail.component.ts:41](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/ladt/editor/components/team-detail.component.ts#L41)) — so a clubless team can never be given one here. On a **BYCLUBNAME** job a team with no club is then **unreachable for self-roster**.
- **Severity**: Bug / Question — a real setup gap; confirm the intended House-Team authoring path with Todd.
- **Status**: Open
- **VERIFIED — how CTWLOO set theirs up (dev TSICV5, `ctwloo-atxzebraopen-2026`)**: job = **PP21|BYCLUBNAME**. Each House Team (U08/U10/U12/U14/HS House Team) carries a **PerRegistrantFee** ($28, HS $40), its **agegroup has `bAllowSelfRostering = 1`**, and it's rostered under a **club rep whose `club_name` = "HOUSE TEAM"**. That "HOUSE TEAM" club rep is what makes the teams appear and be self-rosterable — i.e. **they were seeded via a club rep, not from the LADT tree**.
- **Root cause (CONFIRMED, why a clubless team is unreachable)**: On a BYCLUBNAME job the self-roster flow makes the player **pick a club first**. The club list is built from `teams.filter(t => !!t.clubName)` ([eligibility-step.component.ts:265](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/registration/player/steps/eligibility-step.component.ts#L265)), and teams are then matched to the picked club by name ([team.service.ts:45-47](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/registration/player/services/team.service.ts#L45)). A team's `clubName` comes **only** from `Teams.ClubrepRegistrationid → Registrations.club_name` ([review-step.component.ts:451-452](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/registration/player/steps/review-step.component.ts#L451)). No club rep ⇒ no `clubName` ⇒ the team contributes no club option and matches none ⇒ **invisible**. (Note: the backend availability query itself does **not** require a club — [TeamRepository.cs:134-166](../../TSIC-Core-Angular/src/backend/TSIC.Infrastructure/Repositories/TeamRepository.cs#L134) returns clubless self-roster teams — so this is specifically the **BYCLUBNAME club-first UI** that blocks them.)
- **Answers to Ann's questions**:
  1. *Where do you add "club: HOUSE TEAM" on the popup?* — You can't from LADT. A team's club is its **ClubrepRegistration** (`club_name`). CTWLOO created a club rep with club name **"HOUSE TEAM"** and rostered the House Teams under it. LADT's team-detail only lets you **move** a team between existing clubs, and only if it already has one.
  2. *Is the only way to register a House Team as a club rep?* — On a BYCLUBNAME job, **effectively yes today**: the team needs a ClubrepRegistration to carry a `club_name`, without which it's unreachable in the club-first self-roster flow.
- **For Todd — fix direction (pick the intended authoring path)**:
  - **(a) Let LADT assign a club to a clubless team** — extend the team-detail "Change Club" so it also **assigns** a club (incl. creating/attaching a "HOUSE TEAM"-style club rep) when `clubRepRegistrationId` is null, so House Teams can be built end-to-end from the tree.
  - **(b) Make clubless self-roster teams reachable on BYCLUBNAME** — surface them under a synthetic group (e.g. a "House" bucket or the agegroup) so the club-first UI doesn't hide a team that has `bAllowSelfRostering` + a `PerRegistrantFee` but no club.
  - **(c) A dedicated "Add House Team" action** that seeds the whole pattern in one step (agegroup self-roster on + PerRegistrantFee + a "HOUSE TEAM" club association), matching how CTWLOO is configured.

### PL-033: [Configure] Job Settings → Players — move "Player Settings (SuperUser)" up (just under Player Registration Settings), above the text boxes, so profile settings are findable
- **Topic**: Configure / Job Settings (filed here for Todd)
- **Area**: Job Settings → Configure / **Players** tab
- **Where**: The **Players** tab has three stacked sections — (1) **Player Registration Settings**, (2) the long **PLAYER Confirmation, Liability Waiver & Code of Conduct Text** boxes, (3) **Player Settings (SuperUser)** at the very bottom ([player-tab.component.html:3](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/configure/job/tabs/player-tab.component.html#L3), [:55](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/configure/job/tabs/player-tab.component.html#L55), [:121-159](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/configure/job/tabs/player-tab.component.html#L121)).
- **What I did**: As SuperUser, went to adjust player settings — e.g. tried to find where to reset/change the **Player Profile**.
- **What I expected**: The SuperUser settings to be near the top, easy to reach.
- **What happened**: "Player Settings (SuperUser)" sits at the **very bottom**, below the long Confirmation/Waiver/Code text boxes, so it's easy to miss — I couldn't find where to reset the Player Profile.
- **Severity**: UX
- **Status**: Open
- **For Todd — the change**: In [player-tab.component.html](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/configure/job/tabs/player-tab.component.html), move the whole `@if (svc.isSuperUser()) { … }` block (the SuperUser section, currently the **last** section at lines 121-159) to **immediately after the "Player Registration Settings" section** (after line 54), so the new order is: (1) Player Registration Settings → (2) **Player Settings (SuperUser)** → (3) Confirmation/Waiver/Code text. This puts the SuperUser controls **above** the big text boxes. The "reset Player Profile" Ann was hunting for is the **Registration Form** field in that block (`coreRegformPlayer` — the `PPxx|CONSTRAINT` profile assignment, [:129-132](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/configure/job/tabs/player-tab.component.html#L129)); it also holds RegSaver / Mom / Dad labels. No logic change — pure section reorder.

### PL-034: [Configure] ARB Subscription Health page exists and works but has NO menu entry — add it (Director / SuperDirector / SuperUser) so directors can check failed ARB payments & email families like in legacy
- **Topic**: Configure / Menus (navigation) — ARB / Accounting (filed here for Todd)
- **Area**: Configure / Menus (navigation) — ARB / Accounting
- **Where**: The **ARB Subscription Health** page — route `{jobPath}/arb/health` ([app.routes.ts:280-288](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/app.routes.ts#L280), `ArbHealthComponent`)
- **What I did**: Looked for the legacy ability to **check ARB failed payments and send emails** to those families.
- **What I expected**: A menu entry to reach it, as in legacy.
- **What happened**: The page **exists and works** but has **no menu link** anywhere — no routerLink in the app, no `JobMenu_Items` entry, and the legacy `AdnArbSweep` menu items ("Manual ARB Sweep (ALL)", "List of Suspicious ARBs", "Get Transactions From Past N Days") don't point to it. It's reachable **only by typing the URL**, so I couldn't find it.
- **Severity**: Bug / UX (a working, needed feature is effectively hidden)
- **Status**: Open — **PL for Todd**
- **VERIFIED**:
  - Route `{jobPath}/arb/health` → "**ARB Subscription Health**" is already role-gated to **Superuser / Director / SuperDirector** ([app.routes.ts:286](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/app.routes.ts#L286)) — so the required access is **already correct**; only the menu entry is missing.
  - Page capabilities (the legacy equivalent): a table of **flagged** registrants (behind in payment / failed ARB) with **Subscription / Status / Fee Total / Paid / Owes Now**, **select** (incl. select-all), and **compose + send a custom batch email** to the selected, with a sent/failed summary ([arb-health.component.html](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/arb/health/arb-health.component.html)). Backed by `/api/arb-defensive` (`GetFlagged`, `SendEmails`, `substitution-variables`) — [ArbDefensiveController.cs](../../TSIC-Core-Angular/src/backend/TSIC.API/Controllers/ArbDefensiveController.cs).
- **Requirement (Ann)**: must be available to **Director, SuperDirector, and SuperUser** (the route already permits all three — the menu entry must use the **same** role gate).
- **For Todd — the change**:
  - Add a **menu entry** routing to `arb/health` (e.g. under a Director/Accounting or ARB menu), gated to Director / SuperDirector / SuperUser to match the route.
  - **Retire or repoint** the stale legacy `AdnArbSweep` menu items to this new page so directors aren't sent to dead/legacy tools.
  - (Optional) also surface it from the Accounting area and/or the existing "ARB Health" filter in Search/Registrations.

### PL-035: eCheck ARB needs full parity with CC ARB — payment-method not shown on the subscription, not distinguished in ARB Health, and no self-service eCheck update
- **Tested**: ARB Players Registration — set up an **eCheck** ARB subscription for **Brynn Shoulberg**, **YJ North: Players 2026-2027**, **Subscription ID #9872151**. The charge/setup **handled correctly (incl. Sandbox)**. This item is about the surrounding tooling, which is still CC-centric.
- **Area**: ARB subscription display + ARB Health + self-service payment-info update
- **Overarching ask (Ann)**: **the ARB process for eCheck must mirror CC for ARB** across every surface.
- **Severity**: Feature / parity gap (eCheck ARB is newly enabled; the ancillary tooling hasn't caught up)
- **Status**: Open
- **Q1 — should the ARB Subscription section show eCheck vs CC?** Yes, and it can't today. `SubscriptionDetailDto` carries **no payment-method** ([AccountingDtos.cs:156-165](../../TSIC-Core-Angular/src/backend/TSIC.Contracts/Dtos/RegistrationSearch/AccountingDtos.cs#L156)) — only SubscriptionId / Status / amount / occurrences / start / interval. **Fix**: add payment method (CC vs eCheck) + a masked identifier (card last-4 or bank acct last-4 / routing) to the DTO and show it on the subscription card ([registration-detail-panel](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/search/registrations/components/registration-detail-panel.component.ts) + [family-payment](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/shared-ui/components/family-payment/family-payment.component.ts)).
- **Q2 — how is eCheck recorded in ARB Health & follow-up?** Partially, and it's indistinguishable. ARB Health has two flag types — **`ExpiringCard`** (CC-only; a bank account doesn't expire) and **`BehindInPayment`** ([ArbFlagType.cs](../../TSIC-Core-Angular/src/backend/TSIC.Contracts/Dtos/Arb/ArbFlagType.cs); [ArbDefensiveService.cs:42-46](../../TSIC-Core-Angular/src/backend/TSIC.API/Services/Admin/ArbDefensiveService.cs#L42)). A failed **eCheck NSF return** (processed by the daily reconciliation sweep — "ARB import + eCheck return processing", [Program.cs:472](../../TSIC-Core-Angular/src/backend/TSIC.API/Program.cs#L472)) leaves the sub behind, so it **would** appear under *Behind in Payment* — **but** `ArbFlaggedRegistrantDto` has **no payment-method field** ([ArbFlaggedRegistrantDto.cs:3-15](../../TSIC-Core-Angular/src/backend/TSIC.Contracts/Dtos/Arb/ArbFlaggedRegistrantDto.cs#L3)), so eCheck can't be told apart from CC in the list. **Fix**: add payment method to `ArbFlaggedRegistrantDto` + an ARB Health column; and consider an **eCheck-return/NSF** flag analog (the proactive `ExpiringCard` concept has no eCheck equivalent — closed/invalid-account is the closest).
- **Q3 — self-service eCheck update in the logged-in upper-right menu?** Not today — CC only. The header dropdown pushes **"Update CC Info"** → `arb/update-cc/:registrationId` ([client-header-bar.component.ts:151-153](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/layouts/components/client-header-bar/client-header-bar.component.ts#L151); route [app.routes.ts:290-293](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/app.routes.ts#L290)) — the comment says "stored **card**." There is **no** eCheck/bank-account update route or component ([ArbUpdateCcComponent](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/arb/arb-update-cc.component.ts) is CC-specific). **Fix**: add an **"Update Bank / eCheck Info"** self-service path mirroring `update-cc` (new route + component + backend `ArbUpdateEcheckRequest`), and push that menu item when the subscription is an eCheck sub (vs the CC item for card subs).
- **For Todd — the parity checklist** (mirror each CC surface for eCheck):
  1. **Display**: payment method + masked identifier on the subscription card (Q1).
  2. **ARB Health / follow-up**: payment-method column + eCheck-return flagging so failed eChecks are actionable and distinguishable (Q2).
  3. **Self-service update**: an eCheck "update bank info" flow in the logged-in menu, gated to eCheck subs (Q3).
  4. Confirm the **defensive email** templates read correctly for eCheck (they currently speak in card terms — e.g. "update your card").
