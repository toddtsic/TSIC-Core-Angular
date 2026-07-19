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
- **Status**: Open
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
- **Status**: Open
- **Root cause (verified against the live DB for this job)**: `RosterIsFull = current >= MaxCount && MaxCount > 0` ([TeamLookupService.cs:67](../../TSIC-Core-Angular/src/backend/TSIC.API/Services/Teams/TeamLookupService.cs#L67)) is computed **live**, so raising Max correctly flips the *real* event's `rosterIsFull` back to false — good. But when the event was full, a **WAITLIST twin was minted as a real `Teams` row** (agegroup `WAITLIST - {name}`, MaxCount 100000). That twin **persists** — I confirmed `WAITLIST - 2028 / 2029 / 2030` rows exist for this job alongside the open real teams. `GetAvailableTeamsQueryResultsAsync` deliberately surfaces WAITLIST agegroups ([TeamRepository.cs:147-149](../../TSIC-Core-Angular/src/backend/TSIC.Infrastructure/Repositories/TeamRepository.cs#L147)) so a pending waitlisted player can resume, and nothing hides the twin once the parent regains capacity. So the leftover $0 twin shows next to the now-open real event.
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
- **Status**: Open
- **Root cause (same as PL-010 — the persistent minted twin)**: For each full team the list carries two rows:
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
- **Status**: Open
- **Recommended text (Ann)**: "Only one Discount Code can be applied per player."
- **For Todd — where/how**: The backend already detects this exact case — the one-use guard `if (reg.DiscountCodeId != null)` sets a per-player result message **"Discount already applied to this player"** ([PlayerRegistrationPaymentController.cs:247-256](../../TSIC-Core-Angular/src/backend/TSIC.API/Controllers/PlayerRegistrationPaymentController.cs#L247)). But the frontend shows the **aggregate** `resp.message` ([payment-v2.service.ts:499-500](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/registration/player/state/payment-v2.service.ts#L499)), which when nothing applied is the generic **"No discounts were applied"** ([PlayerRegistrationPaymentController.cs:369](../../TSIC-Core-Angular/src/backend/TSIC.API/Controllers/PlayerRegistrationPaymentController.cs#L369)) — so the specific reason never surfaces. Fix by surfacing the specific reason, e.g. update the per-player message at `:254` to Ann's wording AND have the failure path reflect it (either the aggregate Message picks up the common failure reason when `successCount == 0`, or the frontend renders `resp.Results[].message`). **Do NOT** simply relabel line 369 — it's a catch-all that also fires for an invalid code and the "No discount applicable" ($0 balance) case at `:282`, which Ann's wording would mislabel.

### PL-013: Vertical Insure doesn't reliably re-quote after a Discount Code — $ code stale until re-login; 100% not cleanly gated (relates to PL-007)
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
- **Status**: Open
- **Finding (verified)**: The admin accounting ledger's `PaymentType` is `check | cc | correction | refund` only ([accounting-ledger.component.ts:175, 387, 501](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/shared-ui/components/accounting-ledger/accounting-ledger.component.ts#L175)); there is no `echeck` type. Consistent with the earlier decision to punt admin-entered eCheck.
- **For Todd — recommendation**: Likely leave as-is. An eCheck (ACH) is a bank draft that needs the **payer's** bank account/routing + their authorization to draft — an admin doesn't have and shouldn't key a family's bank credentials. A paper check the admin can record (they hold it) and a CC they can key, but an eCheck has no natural admin workflow — it's the family's own action in the registrant flow. Confirm you're comfortable not adding admin eCheck; if you ever do want it, it'd need the family's bank details captured some other way (e.g. a family-initiated eCheck the admin only records after the fact).

### PL-015: "Check Owed" column — behavior confirmed correct; discuss its appearance with Todd
- **Tested**: Showcase Registration
- **Area**: Player popup → Accounting → Account Summary (per-method owed columns; eCheck enabled)
- **Where**: The "Check Owed" column that appears alongside CC Owed / eCheck Owed when eCheck is activated
- **What I did**: Reviewed the Account Summary on a job with eCheck on.
- **Behavior — confirmed CORRECT**: "Check Owed" is proc-free — it's the amount owed for a physical check, and it correctly excludes **eCheck processing AND CC processing**. It flows from the single canonical resolver `PaymentState.ResolveOwed`: `Check = OwedFor(0m)`, documented "Check == Cash == Correction — all proc-free" ([PaymentState.cs:325-337, 353-354](../../TSIC-Core-Angular/src/backend/TSIC.Contracts/Payments/PaymentState.cs#L325)). Order is Check < eCheck < CC owed, as intended.
- **Severity**: UX (discussion — no math/logic change)
- **Status**: Open
- **For Todd (discussion)**: The math is right; this is purely about **presentation** of the three owed columns (CC Owed / eCheck Owed / Check Owed) when eCheck is on. Ann wants to review the column's appearance with you — e.g. label clarity ("Check Owed" vs "eCheck Owed" read similarly), whether all three columns should always show or be consolidated, and visual treatment/width. No behavior change intended — a display decision.

### PL-016: Account Summary — show a Fee-Adj that includes a Late Fee in RED (mirror the green used for decreases)
- **Tested**: Showcase Registration
- **Area**: Player popup → Accounting → Account Summary (registered-teams / event breakdown grid) → Fee-Adj column
- **Where**: The Fee-Adj amount when it reflects a **late fee** (a net increase)
- **What I did**: Looked at the Fee-Adj value on a registration carrying a Late Fee.
- **What I expected**: A Fee-Adj that increases the amount (late fee) shown in **red**, symmetric with how a decrease (discount) shows in **green**.
- **What happened**: A negative Fee-Adj (decrease) is green; a positive Fee-Adj (late-fee increase) shows in the **default color**, not red.
- **Severity**: UX (color)
- **Status**: Open
- **For Todd — the change**: The cell colors green only on a decrease: `<span [class.text-success]="data.feeAdj < 0">{{ data.feeAdj | currency }}</span>` ([registered-teams-grid.component.ts:125](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/registration/team/components/registered-teams-grid.component.ts#L125)). Add the symmetric red for an increase — `[class.text-danger]="data.feeAdj > 0"` — and do the same on the column sum at [:208](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/registration/team/components/registered-teams-grid.component.ts#L208) (`[class.text-danger]="sumFeeAdj() > 0"`). Note this grid is shared (family-payment + club-rep views), so the change applies everywhere the Fee-Adj column shows — confirm that's desired (it should be — same decrease-green/increase-red convention).
