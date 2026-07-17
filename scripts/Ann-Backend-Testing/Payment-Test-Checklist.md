# Payment Testing Checklist

**Tester:** Ann · **Started:** 2026-07-17

A registration-payment run-through across job types. Tick ☐ as each is verified; jot issues under **Notes** for the punchlist.

**Job types:** (1) Tryouts · (2) Showcase · (3) Camps & Clinics · (4) ARB Players · (5) Maryland Summer Camps · (6) ISP Event Center · (7) Tournament — club rep · (8) Tournament — player self-roster

_Adult registration is out of scope for this pass._

<div style="page-break-before: always;"></div>

## 1. Tryouts Registration — single player

- ☐ CC payment by registrant

- ☐ eCheck payment by registrant — verify "pending" status

- ☐ CC payment by Admin

- ☐ Check payment by Admin

- ☐ Correction by Admin + make player Active

- ☐ Negative correction (increases owed)

- ☐ Void

- ☐ Refund

- ☐ Early Bird Discount

- ☐ Late Fee

- ☐ Discount Code ($)

- ☐ Discount Code (%)

- ☐ Discount Code that zeroes the balance ($0 → auto-active)

- ☐ Free registration ($0 fee, skips payment)

**Notes:**



<div style="page-break-before: always;"></div>

## 2. Showcase Registration — single player

_Same scenarios as Tryouts, plus team-option pricing and waitlist._

- ☐ Register selecting a team/position option — verify the selected option's fee is what's charged (and a different option charges its different amount)

- ☐ CC payment by registrant

- ☐ eCheck payment by registrant — verify "pending" status

- ☐ CC payment by Admin

- ☐ Check payment by Admin

- ☐ Correction by Admin + make player Active

- ☐ Negative correction (increases owed)

- ☐ Void

- ☐ Refund

- ☐ Early Bird Discount

- ☐ Late Fee

- ☐ Discount Code ($)

- ☐ Discount Code (%)

- ☐ Discount Code that zeroes the balance ($0 → auto-active)

- ☐ Free registration ($0 fee, skips payment)

- ☐ Waitlist player registration — registers into a full event → lands on waitlist

- ☐ Waitlist player off waitlist + complete payment

**Notes:**



<div style="page-break-before: always;"></div>

## 3. Camps & Clinics Registration — multi-player, multi-event

_Core scenarios (as Tryouts), plus a multi-event pricing check, multi-registration admin payments, and waitlist._

- ☐ Verify all LADT pricing presents correctly across the multi-player / multi-event cart (per-event / per-age-group fees show and total correctly)

- ☐ CC payment by registrant

- ☐ eCheck payment by registrant — verify "pending" status

- ☐ CC payment by Admin — also test covering more than one registration

- ☐ Check payment by Admin — also test covering more than one registration

- ☐ Correction by Admin + make player Active

- ☐ Negative correction (increases owed)

- ☐ Void

- ☐ Refund

- ☐ Early Bird Discount

- ☐ Late Fee

- ☐ Discount Code ($)

- ☐ Discount Code (%)

- ☐ Discount Code that zeroes the balance ($0 → auto-active)

- ☐ Free registration ($0 fee, skips payment)

- ☐ Waitlist player registration — registers into a full event → lands on waitlist

- ☐ Waitlist player off waitlist + complete payment

**Notes:**



<div style="page-break-before: always;"></div>

## 4. ARB Players Registration — club player, autopay payment plan

_Standard rows (as Tryouts) plus ARB payment-plan scenarios. ARB is card/bank recurring — there is no ARB-by-check. "Payment by registrant" here funds the plan (or chooses PIF)._

- ☐ CC payment by registrant

- ☐ eCheck payment by registrant — verify "pending" status (eCheck + ARB is brand new)

- ☐ CC payment by Admin

- ☐ Check payment by Admin

- ☐ Correction by Admin + make player Active

- ☐ Negative correction (increases owed)

- ☐ Void — on an ARB-paid registration (the first draft holds real money; the plan itself books none up front)

- ☐ Refund — on an ARB-paid registration

- ☐ Early Bird Discount

- ☐ Late Fee

- ☐ Discount Code ($)

- ☐ Discount Code (%)

- ☐ Discount Code that zeroes the balance ($0 → auto-active)

- ☐ Free registration ($0 fee, skips payment)

_ARB-specific:_

- ☐ ARB with PIF option — registrant can choose pay-in-full instead of the plan

- ☐ Register more than one player — verify each gets ONE subscription, no double-billing

- ☐ Add a second player AFTER enrolling the first (enroll #1 → back to Review → add #2 → return to Payment) — verify #1 isn't re-subscribed/re-billed and shows as already enrolled

- ☐ Per-player installment amounts display correctly with multiple players (each shows "N payments of $X", not one lumped total)

- ☐ Discount Code applied to an ARB registration — verify it reduces the plan / installments correctly

- ☐ Check ARB Subscription Information in Player Details

- ☐ Admin cancels an ARB subscription, then re-enroll

- ☐ Failed / declined draft — force a decline and check the autopay-failed handling

- ☐ Check sandbox next day — payment applied (both CC and eCheck)

**Notes:**



<div style="page-break-before: always;"></div>

## 5. Maryland Summer Camps — single player, deposit / balance due

_Two-phase: deposit first, then balance due. Two registrant fee types (Overnight / Commuter). Run the full payment matrix in each phase._

**Fee types — verify each charges its correct fee (across both phases):**

- ☐ Overnight registrant — correct fee

- ☐ Commuter registrant — correct (different) fee

**— Deposit Phase — all payment rows:**

- ☐ CC payment by registrant (deposit)

- ☐ eCheck payment by registrant (deposit) — verify "pending" status

- ☐ Choose PIF at deposit time (pay full now, skip the balance phase) — all payment types

- ☐ CC payment by Admin

- ☐ Check payment by Admin

- ☐ Correction by Admin + make player Active

- ☐ Negative correction (increases owed)

- ☐ Void

- ☐ Refund

- ☐ Early Bird Discount

- ☐ Late Fee

- ☐ Discount Code ($) — deposit phase

- ☐ Discount Code (%) — deposit phase

- ☐ Discount Code that zeroes the balance ($0 → auto-active)

**— Balance Due Phase — all payment options again:**

- ☐ CC payment by registrant (balance)

- ☐ eCheck payment by registrant (balance) — verify "pending" status

- ☐ CC payment by Admin

- ☐ Check payment by Admin

- ☐ Correction by Admin + make player Active

- ☐ Negative correction (increases owed)

- ☐ Void

- ☐ Refund

- ☐ Late Fee

- ☐ Discount Code ($) — balance-due phase (verify it discounts only the remaining balance)

- ☐ Discount Code (%) — balance-due phase

- ☐ New registrant during balance-due phase pays PIF (full camp total) — verify the amount = full total, not the deposit figure; test all payment types

**— Cross-phase payment-method mixing:**

- ☐ Deposit by eCheck → balance by eCheck

- ☐ Deposit by CC → balance by eCheck

- ☐ Deposit by eCheck → balance by CC

**— Waitlist:**

- ☐ Waitlist player registration — registers into a full event → lands on waitlist

- ☐ Waitlist player off waitlist + complete payment

**Notes:**



<div style="page-break-before: always;"></div>

## 6. ISP Event Center — player league registration

_League registration. Takes **CC or check only** — no eCheck._

- ☐ CC payment by registrant

- ☐ **Pay-by-check by registrant** — chooses "pay by check" at checkout → player marked active pending the mailed check (verify the check amount is lower than CC — a check skips the processing fee)

- ☐ CC payment by Admin

- ☐ Check payment by Admin — record the mailed check against the registration

- ☐ Correction by Admin + make player Active

- ☐ Negative correction (increases owed)

- ☐ Void

- ☐ Refund

- ☐ Early Bird Discount

- ☐ Late Fee

- ☐ Discount Code ($)

- ☐ Discount Code (%)

- ☐ Discount Code that zeroes the balance ($0 → auto-active)

- ☐ Free registration ($0 fee, skips payment)

**— Waitlist:**

- ☐ Waitlist player registration — registers into a full event → lands on waitlist

- ☐ Waitlist player off waitlist + complete payment

- ☐ **Inactive / unpaid check payees count toward the max** — register up to the cap using pay-by-check (still unpaid), then verify the next registrant is waitlisted. Unpaid check-payees must hold their seats against the max so the event can't over-fill. Also confirm what status a pay-by-check registrant has before the check is recorded.

**Notes:**



<div style="page-break-before: always;"></div>

## 7. Tournament — Club Rep (team registration)

_Team registration, two-phase (deposit / balance due). Club rep pays for one or more teams. Waitlist auto-engages when an age group hits Max Teams._

**— Deposit Phase — team payment:**

- ☐ CC payment (deposit)

- ☐ Check payment (deposit)

- ☐ Correction by Admin (deposit)

**— Balance Due Phase — team payment:**

- ☐ CC payment (balance)

- ☐ Check payment (balance)

- ☐ Correction by Admin (balance)

**— Cross-phase method combinations (CC / Check):**

- ☐ Deposit CC → Balance CC

- ☐ Deposit CC → Balance Check

- ☐ Deposit Check → Balance CC

- ☐ Deposit Check → Balance Check

**— Repeat with eCheck enabled:**

- ☐ eCheck works in both phases (deposit + balance)

- ☐ eCheck cross-phase combos (deposit eCheck → balance CC/Check, and the reverse)

**— Team Discount Codes (both phases):**

- ☐ Team Discount Code ($) — deposit phase

- ☐ Team Discount Code (%) — deposit phase

- ☐ Team Discount Code ($) — balance-due phase (discounts only the remaining balance)

- ☐ Team Discount Code (%) — balance-due phase

**— Other options:**

- ☐ Early Bird Discount (EBD)

- ☐ Late Fee

- ☐ Void

- ☐ Refund

- ☐ Club rep pays for more than one team at once

- ☐ Full-payment-required mode (teams pay full at registration, no deposit/balance split)

**— Max Teams & Waitlist:**

- ☐ Max Teams per age group — register up to the cap, verify the next team is waitlisted

- ☐ Max Teams per Club — register up to the per-club cap, verify a further team from the same club is blocked / waitlisted

- ☐ **Waitlist tracking** — unpaid teams (deposit-only or pay-by-check) count toward Max Teams so the age group can't over-fill; verify what status an unpaid team holds

**Notes:**



<div style="page-break-before: always;"></div>

## 8. Tournament — Player Self-Roster

_A self-roster player pays only when their assigned team carries a per-registrant fee ("House Team"). Most tournament self-roster players are on $0 teams and pay nothing._

**— House Team (paying) player — full single-payment matrix (as Tryouts):**

- ☐ CC payment by registrant

- ☐ eCheck payment by registrant — verify "pending" status

- ☐ CC payment by Admin

- ☐ Check payment by Admin

- ☐ Correction by Admin + make player Active

- ☐ Negative correction (increases owed)

- ☐ Void

- ☐ Refund

- ☐ Early Bird Discount

- ☐ Late Fee

- ☐ Discount Code ($)

- ☐ Discount Code (%)

- ☐ Discount Code that zeroes the balance ($0 → auto-active)

**— Non-paying player ($0 self-roster) — the common case:**

- ☐ Self-roster onto a regular ($0) team → verify $0 owed, no payment step, player goes active

- ☐ Fee resolves correctly by assigned team — picking a House Team option charges the per-registrant fee; picking a regular option = $0

**Notes:**


