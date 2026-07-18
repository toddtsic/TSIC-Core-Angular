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
- **Status**: Open
- **For Todd**: Drop the "and cannot be undone" / "Cannot be undone" phrase from the CC-charge confirmation only; leave the Refund confirmation as-is.
- **Note (dev)**: CC-charge confirmation text appears twice — `accounting-ledger.component.html:235` ("This processes immediately via Authorize.Net and cannot be undone.") and `:473` ("Processes immediately via Authorize.Net. Cannot be undone.") — both `cc-confirm-warning`. Keep the refund copy at `refund-modal.component.html:58, 72`.

### PL-002: Correction — a negative amount can't be entered (confirm button won't enable); reconcile with the "negative increases owed" hint
- **Tested**: Tryouts Registration
- **Checklist**: §1 Tryouts — Negative correction (increases owed)
- **Area**: Player Details → Accounting → Add Accounting Record → Correction
- **Where**: The correction amount input / confirm button, and the info hint beneath it
- **What I did**: On the player Accounting popup, chose Correction and tried to enter a negative amount
- **What I expected**: To be able to enter a negative correction — the hint says a negative amount increases the amount owed — and the confirm button to enable
- **What happened**: The negative amount wouldn't take and the confirm button doesn't highlight. But the info hint reads "A negative (–) amount increases the amount owed." So either negatives should be enterable (fix the input/button), or — if they're intentionally restricted — that hint should be removed/clarified.
- **Severity**: Question
- **Status**: Open
- **For Todd**: Decide the intended behavior. If negatives should work → fix so a negative can be entered and the button enables. If negatives are intentionally limited → remove/clarify the "A negative amount increases the amount owed" hint so it doesn't contradict the UI.
- **Note (dev)**: The amount input caps the negative at the amount paid: `[min]="paymentType() === 'correction' ? -modalPaid() : 0"` (`accounting-ledger.component.html:378`). With nothing paid yet (modalPaid = 0) the min is 0, so no negative is possible — the likely cause on a fresh registration. Also: a negative that "increases owed" being bounded by *amount paid* looks inconsistent with the hint at line 420 — the two may need reconciling.

### PL-003: Add Accounting Record → Credit Card — the Optional Comment is dropped (never saved); Check keeps it
- **Tested**: Tryouts Registration
- **Checklist**: §1 Tryouts — CC payment by Admin
- **Area**: Player Details → Accounting → Add Accounting Record → Credit Card
- **Where**: The Optional Comment field on a CC accounting record; the Payment Ledger display
- **What I did**: Added a Credit Card accounting record with an Optional Comment entered
- **What I expected**: The comment to appear on the ledger row, the same as it does for a Check record
- **What happened**: The comment doesn't appear in the Payment Ledger for a CC record. It works for Check.
- **Severity**: Bug
- **Status**: Open
- **For Todd**: Carry the comment through on a CC charge, the same as Check/Correction. It's currently dropped before it's ever sent.
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
- **Status**: Open
- **For Todd**: Lay it out so the "Inherited from …" indicator doesn't overlap adjacent info — widen the column, render it as a proper hover tooltip, or reposition it.
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
- **Status**: Open
- **For Todd**: An inactive, unpaid player who has only a Correction record should still read as Pending. Right now a Correction knocks them out of the "pending" test without making them active, so they vanish.
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
- **Status**: Open
- **For Todd**: Reword so it doesn't rely on "above." **Suggested:** "A correction doesn't make the player Active. To activate them, go to the Search Registrations table, select that player, and set them Active." (Shorter alt: "Recording a correction won't activate the player. To activate them, find them in Search Registrations and toggle Active.")
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
- **Status**: Open
- **For Todd**: A fully-discounted ($0-owed) player shouldn't be offered RegSaver, and if it ever shows the amount must be net of the code. Right now the offer is frozen at the pre-code price.
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
- **Status**: Open
- **For Todd** — three decisions:
    1. **Policy**: is an unpaid check-hold meant to be indefinite, or should there be an N-day window after which it auto-inactivates? (Today it's indefinite.)
    2. **Copy**: reword the payment-step message to match the real behavior — either "stays active until we receive your check" (no auto-release) or, if you add an expiry, state the deadline.
    3. **Enhancement**: add a "check pending" marker (Ann's suggested **P** badge + hover, e.g. "Opted to pay by check — payment not yet received") on the Search Registrations row.
- **Note (dev)**: Pay-by-check → `POST /player-registration/submit-by-check` → `PlayerRegistrationService.SubmitByCheckAsync` stamps `PaymentMethodChosen = 3 (Check)` + claims the seat and sets `BActive = true` (`:768-807`); no expiry field is written. No hosted/scheduled service inactivates check-payers — the only `BackgroundService` is `AdnSweepService` (Authorize.Net settlement reconciliation). Message source: `payment-step.component.ts:604` (player) / `payment-step.component.ts:552` (team). A pending-check badge is derivable from `PaymentMethodChosen == 3 && active && owed > 0`; Search Registrations currently renders only `active-badge` / `dc-badge` (`search-registrations.component.html:782, 765`).
