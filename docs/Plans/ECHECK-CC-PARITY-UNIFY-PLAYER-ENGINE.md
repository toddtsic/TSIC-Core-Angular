# eCheck ↔ CC Parity — Unify the Player Charge Engine

**Status:** Planned (not started)
**Date:** 2026-05-30
**Owner:** Todd

## Principle

eCheck must behave **exactly like CC**, with only these differences:

1. **Gateway object** — `bankAccount` (ADN `ADN_ChargeBankAccount_Result`) instead of `creditCard` (`ADN_Charge_Result`).
2. **Rate** — per-job eCheck processing rate (`EcprocessingFeePercent`, default 1.5%) instead of the CC rate. `PaymentState.ResolveOwed` **already** produces both buckets (`owed.Cc`, `owed.Echeck`).
3. **Inherent ACH addendum** — a `Settlement` row + `isEcheckPending` confirmation flag, because ACH settles asynchronously and can be returned (NSF). This is a small post-success hook, **not** a reason for a separate engine.

Everything else (audit trail, amount tripwire, per-registration transactions, partial success, balance bumps, `BActive` flip, PIF restore-on-failure) is shared and eCheck should inherit it.

## Current state — the divergence

| Engine | CC | eCheck |
|---|---|---|
| **Teams** | `ChargeTeamsAsync(kind: Cc)` | `ChargeTeamsAsync(kind: Echeck)` — ✅ **already unified** via one `TeamChargeKind` switch |
| **Registrations** | `ChargeRegistrationsCcAsync` (canonical; parent self-pay + admin charge) | `ExecuteEcheckChargeAsync` — ❌ hand-cloned, routes through nothing |

The clone (`PaymentService.cs:1091`) silently **regresses three guarantees** the canonical engine (`PaymentService.cs:1321`) provides:

| Canonical CC engine | eCheck clone |
|---|---|
| Placeholder RA row written **before** the gateway hit → declined charge leaves `Active=false`, `Comment="FAILED:…"` audit row | RA rows written **only after success** → a failed eCheck **vanishes with no record** |
| `ResolveOwed` **AMOUNT_MISMATCH** tripwire per item | **No tripwire** |
| **One ADN tx per registration** → granular refunds/NSF | **One bundled bank charge per family** → an NSF return reverses *all* siblings |

The team engine (`PaymentService.cs:135`) is the proven template — it already does the kind switch, the proc-credit booking, and the Settlement hook correctly.

## Target architecture

Extract a private core from the canonical engine and route both methods through it (mirrors the team `kind` switch):

```
ChargeRegistrationsCoreAsync(jobId, items, kind, instrument, userId, ct)   // private — the one engine
   ├── ChargeRegistrationsCcAsync(...)         // PUBLIC, unchanged signature → delegates with kind=Cc
   │       used by: admin charge modal + player CC (ExecutePrimaryChargeAsync)
   └── player eCheck path → delegates with kind=Echeck   (ExecuteEcheckChargeAsync DELETED)
```

- `kind` = new `RegistrationChargeKind { Cc, Echeck }` enum.
- `instrument` carries the `CreditCardInfo?` **or** `BankAccountInfo?` (nullable pair, exactly like `ChargeTeamsAsync`).
- **Public `ChargeRegistrationsCcAsync` keeps its current signature** → admin path is untouched. It just forwards to the core with `kind: Cc`.

The kind switch lives at exactly **3 points** inside the core, copied from the team engine:

1. **Charge sizing** — per item: `credit = state.ProcCreditForCharge(item.Amount, …, methodRate)`; gateway amount = `item.Amount − credit`. (CC: `methodRate == CcRate` → credit 0 → amount unchanged.)
2. **Gateway call** — `kind == Cc ? ADN_Charge_Result(card) : ADN_ChargeBankAccount_Result(bank)`.
3. **Post-success** — RA `Paymeth`/`PaymentMethodId`/`AdnCc4`/`AdnCcexpDate` wording; collect pending Settlement rows; set `isEcheckPending` on the confirmation email.

## Backend work items (ordered)

1. Add `RegistrationChargeKind` enum + instrument plumbing.
2. Rename body of `ChargeRegistrationsCcAsync` → `ChargeRegistrationsCoreAsync(…, kind, instrument, …)`; keep the public CC method as a thin `kind: Cc` forwarder.
3. Fold the eCheck branches into the core's existing per-item loop, **on top of** the placeholder-RA + tripwire structure (so eCheck gains all three guarantees).
4. Add the eCheck-only post-success hook: write `Settlement` rows after the RA flush (RA `AId` is identity-generated — same ordering the team engine + clone already rely on).
5. Rewire `ProcessEcheckPaymentAsync` (`PaymentService.cs:1018`) to build `items` from `ComputeChargesAsync` and call the core with `kind: Echeck` — exactly as `ProcessPaymentAsync` → `ExecutePrimaryChargeAsync` does for CC. eCheck thereby inherits the PIF snapshot/restore-on-failure that it currently **lacks**.
6. **Delete** `ExecuteEcheckChargeAsync` and `AddEcheckAccountingEntries` (folded in). Audit for any other callers first.

## The subtle bits (handle precisely)

- **`FeeTotal` reconciliation (highest risk).** `Registrations` has **no `RecalcTotals()`** (Teams does). The canonical engine recomputes `reg.OwedTotal = reg.FeeTotal − reg.PaidTotal`. For an eCheck credit the core must drop **both** `reg.FeeProcessing −= credit` **and** `reg.FeeTotal −= credit` before the recompute, or the registrant keeps a phantom balance equal to the rate delta. The old clone side-stepped this by decrementing `OwedTotal` directly; the unified path cannot. → dedicated test.
- **Per-item credit sizing, not full-owed.** Registrations support deposit/partial pay, so size the credit on `item.Amount` via `ProcCreditForCharge` (with its embedded-proc + `AppliedProcCredit` caps). Do **not** copy the team engine's `owedTotal − owed.Echeck` shortcut — teams always pay full owed; registrations don't.
- **Per-reg Settlement rows** → granular NSF: one registration's return no longer reverses its siblings (the entire point of this change).
- **Failed eCheck now leaves a FAILED placeholder RA** — new, better behavior; confirm the accounting/reporting views tolerate `Active=false` eCheck rows (CC already produces them).

## Frontend parity (player) — display-only, no amount-sending change

The backend already charges the correct (lower) eCheck amount server-side; the player UI just doesn't reflect it. Mirror the team flow:

- **Fee display** — `payment-v2.service.ts:206-223` has no eCheck total. Add an eCheck total + "Save $X by paying with eCheck" callout (today that callout fires only for `isCheck()`). Team flow has `ekOwedTotal` + the savings callout as the reference.
- **Confirmation / review** — player path never passes `isEcheckPending` (team flow does). Add pending-settlement messaging so the player knows the registration is pending 3–5 days.
- **Button visibility** — verify the `showPaymentMethodSelector` ≥2-method gate (`payment-v2.service.ts:198`) doesn't hide eCheck when it's the sole enabled method (`initPaymentMethod` auto-selects it, but confirm the bank form still renders).

## Out of scope (separate items)

- **`responseCode "4"` ("held for review") treated as failure** — lives in the shared `ParseTxnResponse`; affects CC and eCheck **identically**, so it is not a parity gap. Track separately.
- Sweep robustness (settlement timeout, fee-rate drift on late NSF) — inherent-ACH machinery, not CC parity. Revisit only if desired.

## Tests

- Port `EcheckTeamPaymentServiceTests` patterns onto the registration engine.
- Per-reg **partial success**: capture reg 1, decline reg 2 (eCheck) → reg 1 persisted, reg 2 FAILED placeholder.
- **Failed eCheck** leaves `Active=false` + `FAILED:` RA row.
- **Deposit + eCheck** credit sizing (partial pay, credit capped).
- **`FeeTotal` reconciliation**: full eCheck pay drives `OwedTotal` to exactly 0 (the phantom-balance guard).
- **NSF granularity**: returning one reg's tx reverses only that reg.

## Risk / done-criteria

- Public `ChargeRegistrationsCcAsync` signature unchanged → admin path provably untouched.
- `dotnet build && dotnet test` green; existing payment suites unaffected.
- Walk the full player eCheck flow (submit → RA → Settlement → sweep settle / NSF reverse) before declaring done.
   