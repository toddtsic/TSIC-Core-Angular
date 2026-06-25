# Late Fee: Derived-at-Owed, Locked-at-Payment

## The model

A **late fee** is a penalty for *paying late*. Its triggering event is **paying / still owing**,
not signing up. So it is **derived** at the "what do you owe" chokepoint — never frozen onto the
record at registration.

Contrast with the **Early Bird discount**, whose triggering event *is* signup. Early Bird is
correctly **minted at PreSubmit** (registration creation) and frozen — paying late must not strip a
discount you earned by signing up early. **Early Bird is unchanged by this design.** The two
modifiers share a UI (same fee card, same start/end date pickers) but run on different mechanisms
because they have different triggers:

| Modifier | Triggering event | Mechanism |
|---|---|---|
| Early Bird | signup (PreSubmit) | minted once at PreSubmit, frozen |
| Late Fee | paying / still owing | derived at owed-resolution, locked when paid |

### The rule

> A late fee applies while you **owe** and the modifier's window is **open**. It is **editable /
> deletable until paid**. Only **collected dollars lock**. How long the penalty stays in force is
> governed by the modifier's **end date** (mandatory), not by a stamp.

This retires the old "apply to all" retroactive *sweep* — the band-aid that only ever ADDED a fee
where none existed and silently no-op'd outside the date window. The `AssessActiveLateFee` context
flag survives, **repurposed**: it now means "re-derive the effective late fee on this recompute"
(set by the reprice engines and the payment recompute; left off for pure roster/pool swaps so a move
never conjures a penalty). The derivation — gate vs. paid floor — subsumes what the sweep did and
adds correct edit/delete/lock behavior.

## The math (single source of truth)

`PaymentState.EffectiveLateFee(windowedLateFee, configuredLateFee, fullPrice, discount, donation)`
(`TSIC.Contracts/Payments/PaymentState.cs`) returns the greater of:

- **GATE** — `windowedLateFee` (the date-windowed cascade modifier amount resolved as-of-now, `0`
  out of window) **while the BASE principal** (full price, *excluding* the late fee itself) is still
  owed. A record that has paid the full base is **exempt** — a late fee is never billed to a
  paid-in-full record. Out of window ⇒ `0`.
- **FLOOR** — the late fee already **paid** (locked): `PrincipalPaid` beyond the late-free base
  (`fullPrice − discount + donation`), **capped at `configuredLateFee`** (the modifier amount
  *ignoring its window*).

```
gate  = PrincipalRemaining(fullPrice, discount, 0, donation) > 0 ? windowedLateFee : 0   // base-only: PIF-exempt
floor = min(configuredLateFee, max(0, PrincipalPaid − (fullPrice − discount + donation)))
result = max(gate, floor)
```

### Why two late-fee inputs

- `windowedLateFee` (date-gated, from `EvaluateModifiersAsync` at now) drives the **gate** — is the
  fee chargeable *right now*.
- `configuredLateFee` (window-**independent** cascade amount) caps the **floor** — so a window that
  has since closed does **not** erase a late fee the registrant already paid, and so editing the
  modifier **down** (or **deleting** it ⇒ `0`) correctly drops the live charge and refunds the
  surplus via negative owed.

### Behaviors this produces

- New reg in-window, owes → full late fee (matches prior behavior).
- Deposit-paid team pays its balance in-window → late fee charged at payment (**Ann's case**), no
  director action.
- Paid the full base in-window → **no late fee** (PIF-exempt: a paid-in-full record is never billed
  a late fee; the gate measures the base *excluding* the late fee). This is the lenient side of
  "PIF is exempt" — paying *exactly* the base also exempts; the modifier end date and the paid floor
  bound the exposure.
- Paid the late fee, then window closes → late fee stays (locked by the floor).
- Director deletes/reduces the late fee after it was paid → live charge drops; overpayment surfaces
  as negative owed → existing refund/credit path.
- **Accepted tradeoff:** a *short* window the registrant straddles (paid the base before it closed,
  never paid the late fee) lets the unpaid remainder fall off. The end date is the persistence
  control — set it to end-of-registration to keep the penalty in force until paid.

Base-first allocation: a payment retires base principal before the penalty, so the floor only
engages once the base is covered.

## Where it plugs in

The canonical money functions already take `lateFee` as a parameter — they were just being fed the
stamped column. This design feeds them the **derived** value instead:

- `PaymentState.ResolveOwed(...)` — the single per-method (CC/Check/eCheck) owed resolver.
- `PaymentState.PrincipalRemaining / FeeProcessingTarget / FeeAdjustment` — proc rides on the late
  fee, so it derives alongside.

**Write path (recompute + payment):** every financial recompute funnels through
`FeeResolutionService.ApplyRegistrationProcessingAndTotalsAsync` /
`ApplyTeamProcessingAndTotalsAsync` + `RecalcTotals()`. These re-derive the effective late fee and
**stamp `FeeLatefee` as a cache** so raw column reads (grids, search, sort) stay correct between
recomputes. The payment path recomputes at charge time → the late fee is realized and locked when
paid.

**Charge path (realize at payment) — Phase 2:** `PaymentState.ResolveOwed` is *anchored on the stamped
`OwedTotal`* — the `lateFee` arg only sizes the per-method proc credit, it does NOT raise the base
owed. So realizing an **auto-activated** late-fee window (a window whose start date passed with no
director reprice) cannot be a read-side tweak; it requires **re-stamping `OwedTotal`**. Both player
and team charge engines therefore call `IFeeResolutionService.RealizeLateFeeAtChargeAsync(...)` at
charge entry — the read/charge twin of the reprice engine, delegating to the SAME swap applier with
`AssessActiveLateFee = true` — then persist before sizing the charge. It is idempotent for a *paying*
record (positive owed ⇒ its stamped phase already matches, so the applier only moves `FeeLatefee`),
and inert (no SQL) when no window is active or the record is paid in full. Because the re-stamp is
persisted, the `AMOUNT_MISMATCH`/`AMOUNT_CHANGED` tripwires stay honest: if the client's previewed
total was stale, the charge returns the new total and the refreshed page (reading the now-fresh
stamp) matches on retry. Trigger point is **charge entry only** (not the broad family/team GET loads),
so a plain page view never reprices.

**Display grids (admin + accounting):** left reading the stamped column. They do not gate a charge,
so a not-yet-realized late fee shows as base owed until the next recompute — acceptable interim
precision, no tripwire exposure.

## What stays put

- **Early Bird:** untouched — minted at PreSubmit, frozen.
- **`FeeLatefee` column:** kept as a cache + audit of the last-computed late fee. No DB/DDL change,
  no `SqlDbContext` edit, no frontend model regen.
- **Mandatory start/end dates** on every late fee / early bird (enforced in `FeeController.SaveFee`
  and the LADT fee card): a dateless modifier would be an always-on permanent surcharge. The end
  date is the persistence control for the derived model.
