# Team Registration Accounting — Phase & Processing-Fee Verify List

**Purpose:** verify the two reworks that touched team-reg accounting:
1. **Payment-phase rework** (PL-060 migration/persist + PL-062 "apply-to-all" retirement + League/Age-Group cascade).
2. **Processing-fee-on-deposit correctness** — the "fee UI discloses by data; deposit doesn't accept proc fees" work.

**Environment note:** run on the **staging build** with the DB re-seeded (§8P phase stamp). Actual **email delivery** is a go-live check (SES doesn't send off-prod) — here you're verifying **amounts, phase display, and owed math**, not receipts. ADN charges use the **Sandbox** gateway.

---

## Fee-config cheat sheet (what the processing fee should do)

| Config | `bAddProcessingFees` / `…ToTeamDeposit` | Proc fee on DEPOSIT | Proc fee on BALANCE |
|---|---|---|---|
| **A — none** | 0 / — | ❌ none | ❌ none |
| **B — balance only** | 1 / 0 | ❌ **none** ← the key fix | ✅ yes |
| **C — both** | 1 / 1 | ✅ yes | ✅ yes |

*Proc fee applies to **Credit Card** (and **eCheck** at its own rate). **Check** never carries a proc fee.* A NULL job `ProcessingFeePercent` floors to **3.5%** (eCheck to its own floor).

## Test jobs (dev TSICV5, all JobTypeId=2, non-ARB)

| Use for | Job | Deposit / Balance | Phase | Fee cfg |
|---|---|---|---|---|
| **Config A (no fees)**, deposit phase | `Top Threat Tournaments:The Knockout 2026` | 500 / 1370 | Deposit | A |
| **Config B, PIF** | `Top Threat Tournaments:Fall Draw 2026` | 500 / 1500 | **PIF** | B |
| **Config B, deposit phase** | `Top Threat Tournaments:Five Star 2026` (or Platinum Games 2026) | 400 / 1400 | Deposit | B |
| **Config B, balance-only (no deposit)** | `Garden State Futures:Fall Tournament 2026` | 0 / 350 | PIF | B |
| **Small amounts (easy math)** | `Top Threat Tournaments:Merry Laxmas South 2026` | 400 / 600 | Deposit | B |
| Alt PIF/Config B | `Big Dawgs Classic 2026`, `Carolina Clash 2026`, `Top Threat Championship 2026` | 500 / 1300–1700 | PIF | B |

*(Config C "fees on both" — no live 2026 job carries it; if you want to test C, set `bApplyProcessingFeesToTeamDeposit=1` on a Config-B copy, or ask Todd to flag one.)*

---

## PART A — Phase settings behavior (LADT editor)  *(PL-060 / PL-062)*

- [ ] **A1. PIF migrated correctly.** Open **Fall Draw 2026** → Age Group Details → **"Require full payment now" is ON** (8/8 age groups). *(Was the PL-060 bug — OFF.)*
- [ ] **A2. Toggle persists.** Flip an age group's phase → **Convert** → reload the panel → the flag stays (no revert). Works even with **0 registrations**.
- [ ] **A3. No "apply to all age groups" option.** Flip a phase in Age Group Details → the confirm shows **Convert / Cancel only** (with a count) — **no scope choice**. **Cancel** reverts the radio, saves nothing.
- [ ] **A4. Age-group-only convert.** Flip + Convert **one** age group → only that AG changes; the **League card's down-arrow note names it**; set the AG back to **"Use league setting"** → the note disappears.
- [ ] **A5. League-wide via League card.** Change the phase **on the League card** → every non-overriding age group follows ("Currently: … — set at league level").
- [ ] **A6. League grid phase cell.** League grid **PAYMENT PHASE** column reads **"PIF"** for full-payment and **"Deposit"** for deposit-first *(this is a known open edit — PL-062 #1: currently shows "See age group level" for Deposit; confirm once Todd fixes it)*.
- [ ] **A7. Disclosure counts.** League Player card override count **excludes WAITLIST / Dropped** buckets (Fall Draw = **8**, not 14); all-clear green line when nothing overrides.
- [ ] **A8. Phase radio only where a deposit can engage.** On a **player** fee card (structurally single-payment) the phase radio is hidden and reads "Single payment: pay $X in full…"; on a **team/club-rep** card with a deposit, the radio shows.

## PART B — Processing fee vs DEPOSIT (the core fix)

Do each as a **Club Rep** registering/paying a team, **by Credit Card** (proc fee only shows on CC).

- [ ] **B1. Config B — deposit carries NO proc fee.** On **Five Star 2026** (Config B, deposit) register a team → **Pay deposit ($400) by CC** → the charge is **$400 flat, no processing fee added**. Owed after = balance ($1400).
- [ ] **B2. Config B — balance DOES carry proc fee.** Same team → **Pay Balance Due ($1400) by CC** → processing fee **is** added (3.5% ⇒ ~$1400 × 3.5% ≈ $49). Owed → $0.
- [ ] **B3. Config B — PIF pays deposit+balance at once, fee on the BALANCE portion only.** On **Fall Draw 2026** (Config B, PIF) → **Pay in Full by CC** → proc fee is charged on the **balance portion only** (not the deposit portion), per Config B. *(Cross-check the amount vs B1+B2 logic.)*
- [ ] **B4. Config A — no proc fee anywhere.** On **The Knockout 2026** (Config A) → pay **deposit** by CC → no fee; pay **balance** by CC → **still no fee**. Owed math is clean base amounts.
- [ ] **B5. Balance-only job.** On **Garden State Futures:Fall Tournament 2026** (Dep $0) → the full **$350 is due at once**; CC adds the proc fee on the whole $350 (no deposit portion to exempt).
- [ ] **B6. (Optional) Config C.** If a Config-C job is available → deposit CC charge **does** carry a proc fee (contrast with B1).

## PART C — Payment scenarios by method

Run on a **Config B deposit-phase** job (Five Star 2026) unless noted.

- [ ] **C1. Credit Card — deposit then balance.** Deposit (no fee) → balance (with fee) → Owed $0; ledger shows two payment rows with correct fee lines.
- [ ] **C2. Check — deposit then balance.** Pay by **check**: team stamped **active**, **no proc fee** on either deposit or balance; Owed reflects the base amounts; "Check Owed" reads correctly.
- [ ] **C3. eCheck — deposit then balance.** Pay by **eCheck**: proc fee at the **eCheck rate** (not the CC rate) where applicable; the payment books as **PAID** with the **settlement-pending** treatment (optimistic strategy); no CC-rate fee.
- [ ] **C4. Pay in Full (PIF job).** On **Fall Draw 2026** → PIF by CC → single charge for deposit+balance+fee(Config B on balance portion); Owed $0.
- [ ] **C5. Mixed cart (multiple teams).** Register **2+ teams** in one cart, pay together → each team's owed/fee is correct; the payment-table **Payment Phase** badge reads the **site phase** (never "Mixed" — PL-052 #3); no horizontal scroll on a normal cart.
- [ ] **C6. Partial check.** Pay a **partial** check amount → remaining balance correct; check allocation across teams is right.

## PART D — Corrections & refunds

- [ ] **D1. Correction (negative).** Admin adds a **negative** Correction on a team → owed increases correctly; Correction is **non-revenue** (doesn't roll into CC revenue totals).
- [ ] **D2. Correction (positive / reduce owed).** Reduces balance correctly, bounded to [0, FeeTotal].
- [ ] **D3. Refund with comment.** Refund a settled CC payment **from the ledger** with a **typed reason** → the refund row + hover show the reason (PL-058); blank → "Admin refund".
- [ ] **D4. Refund does not double-charge fees.** After a refund, owed/paid/fee math reconciles (no orphaned proc fee).

## PART E — Cross-checks / invariants

- [ ] **E1.** Owed shown to the club rep = FeeTotal − PaidTotal, per team, for the **method** they're paying (CC-owed vs Check-owed can differ by the proc fee).
- [ ] **E2.** A team on **Deposit phase** with only the deposit paid reads **Pending/partial**, not Registered-complete.
- [ ] **E3.** Changing the League-card phase **after** some teams paid converts existing registrations correctly (amounts re-resolve; nobody is mischarged retroactively).
- [ ] **E4.** WAITLIST / Dropped teams owe **$0** and are excluded from the amount due and from phase/override counts.
- [ ] **E5.** No job reads **PIF on the League card while age groups say Deposit** (the illegible state PL-062 killed).

---

**Report findings** as PL- items (payment punchlist). Flag anything where a **deposit charge shows a processing fee on a Config-B job** (B1/B3) or where **owed math is off by the proc-fee amount** — those are the regressions this pass is guarding.
