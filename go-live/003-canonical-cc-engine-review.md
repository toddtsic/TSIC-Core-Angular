# 003 — Canonical CC Engine Refactor Review

**Status:** open (started 2026-05-28)
**Risk class:** catastrophic — same shape as 002. The newly-landed player-CC canonical engine (`PaymentService.ChargeRegistrationsCcAsync`) consolidates parent self-pay AND admin admin-charge into one path. Any regression here misroutes real money on the parent's first impression of the system.

## Scope

Max-effort `/code-review` of commits **893618d..bfae003** on 2026-05-28:

| Commit | Subject | Reviewed? |
|---|---|---|
| `893618de` | refactor(payments): route player CC charge through canonical engine | yes |
| `db611fb5` | chore(logging): tidy alignment | no — cosmetic |
| `c43dc37b` | refactor(payments): hoist player eCheck proc credit into PaymentState | yes |
| `6ff3a6f8` | fix(team-reg): sync club rep financials after team add/remove/edit | yes |
| `2f62bf6b` | docs(go-live): 002 sweep-through | no — docs only |
| `bfae0032` | fix(team-reg, frontend): refresh ledger after CC/eCheck charge | yes |

Diff: 1042 insertions / 202 deletions across 21 files. Heaviest weight in `PaymentService.cs` (+303), new tests `PlayerCcChargeTests.cs` (+338) and `RegisterTeamSyncTests.cs` (+198), `RegistrationSearchService.cs` (-129), and `payment-step.component.ts` (+35).

## Method

`/code-review max` (9 parallel angles: line-by-line / removed-behavior / cross-file / language-pitfalls / wrapper-correctness / reuse / simplification / efficiency / altitude) + verifier + sweep. ~70 candidate findings deduplicated to 15.

Verification tags:
- **VERIFIED** — confirmed by reading the cited `file:line` this session.
- **PLAUSIBLE** — mechanism is real; trigger scenario uncertain. A probe is named.

No fixes applied. Each finding below is its own decision.

## Triage status

- [ ] Issue 1 — PIF upgrade persists on declined CC
- [ ] Issue 2 — AMOUNT_MISMATCH only catches OVER, not stale-LOW
- [ ] Issue 3 — OwedTotal recompute over-credits prior-eCheck combo
- [ ] Issue 4 — No try/catch around ADN_Charge or credentials lookup
- [ ] Issue 5 — `BActive=true` no longer set on parent CC success
- [ ] Issue 6 — SyncRep drops PaidTotal when paid team goes Active=false
- [ ] Issue 7 — Same-family concurrent submit → unguarded race
- [ ] Issue 8 — RegSaver stamping no longer atomic with charge
- [ ] Issue 9 — CancellationToken not propagated through engine saves
- [ ] Issue 10 — Batched invoice → P1 orphan detector misattributes
- [ ] Issue 11 — Admin error wrapper drops `ErrorCode`
- [ ] Issue 12 — Placeholder `Dueamt` records wire amount, not resolver amount
- [ ] Issue 13 — RegSaver stamping uses `DateTime.Now` while engine uses `UtcNow`
- [ ] Issue 14 — Test suite gaps: no throw test, empty-state stub, no `BActive` assertion
- [ ] Issue 15 — ALTITUDE: `SynchronizeClubRepFinancialsAsync` at 15+ call sites

---

## Issue 1 — PIF upgrade persists on declined CC (VERIFIED) — **HIGH**

`PaymentService.cs:1373` — `_acct.SaveChangesAsync()` is called BEFORE the ADN call to commit the placeholder RA rows. Because `_acct` and `_registrations` share the same scoped `SqlDbContext`, that save also flushes the `UpgradeRegistrationsToPifAsync` mutations (FeeBase/FeeTotal/OwedTotal swap from Deposit to PIF) that `ExecutePrimaryChargeAsync` made earlier in the request.

**Failure scenario.** Parent picks PIF on a $500 deposit reg (becomes a $1900 PIF). `UpgradeRegistrationsToPifAsync` mutates the tracked entity. `ChargeRegistrationsCcAsync` placeholder-save at `:1373` commits the PIF state. ADN declines. The failure branch at `:1415` flips RA `Active=false` but does NOT revert the PIF upgrade. Caller returns failure; registration permanently shows `FeeTotal=$1900 OwedTotal=$1900 PaidTotal=$0`. Old code only saved after a successful charge.

**Blast radius.** Parent declines CC mid-PIF, their registration silently jumps to PIF posture with nothing paid — wizard re-entry now demands $1900 they never agreed to commit to.

---

## Issue 2 — AMOUNT_MISMATCH only catches OVER, not stale-LOW (VERIFIED) — **HIGH**

`PaymentService.cs:1340` — `if (item.Amount > owed.Cc + 0.01m)`. The tripwire is unidirectional. If `owed.Cc` drifted UP between wizard display and submit (late-fee accrual, proc-fee re-stamp, admin adjustment), the stale-lower `item.Amount` passes silently and the engine charges the stale amount.

**Failure scenario.** Wizard snapshots `$250` due. Between display and submit, a late-fee bump raises `reg.OwedTotal` to `$300`. Parent clicks Pay. Tripwire: `250 ≤ 300.01` passes. Engine charges $250; recomputed `OwedTotal = $50` residual. Parent believes they paid in full; the discrepancy never surfaces.

**Blast radius.** Silent under-charge on any path where backend state advanced while the wizard was open. The resolver was wired into the engine for drift protection — but only one direction is guarded.

---

## Issue 3 — OwedTotal recompute over-credits prior-eCheck combo (PLAUSIBLE) — **HIGH**

`PaymentService.cs:1449` — new engine sets `reg.OwedTotal = reg.FeeTotal - reg.PaidTotal`. The verifier agent confirmed: the player eCheck partial-pay path at `PaymentService.cs:1099-1107` decrements `FeeProcessing` and `OwedTotal` without touching `FeeTotal`. Subsequent CC charge via the new engine then recomputes against the STALE `FeeTotal`, undoing the prior eCheck credit. (Admin-check path is safe — `RegistrationSearchService.cs:413` recomputes `FeeTotal` alongside `FeeProcessing`.)

**Failure scenario.** Reg FeeBase=500, FeeProcessing=19, FeeTotal=519, OwedTotal=519, PaidTotal=0. Parent pays $100 deposit via eCheck where `deposit > principalRemaining` (so `ProcCreditForCharge` at `PaymentState.cs:118` returns > 0): PaidTotal=100, FeeProcessing=15.2, OwedTotal=415.20, FeeTotal stays 519. Parent then CCs the $415.20 balance. New engine: `OwedTotal = 519 − 515.20 = $3.80` residual. Old `UpdateRegistrationsForCharge` (`OwedTotal -= charge`) would have resolved to $0.

**Probe.** Confirm the deposit-eCheck path is reachable for player self-pay today (Issue 3 may park if deposit eCheck for players isn't wired). Also: identify all entry points that mutate `OwedTotal`/`FeeProcessing` without mirroring to `FeeTotal`.

---

## Issue 4 — No try/catch around ADN_Charge or credentials lookup (VERIFIED) — **HIGH**

`PaymentService.cs:1347` (creds lookup) and `:1380` (ADN_Charge) — neither call is wrapped in try/catch. The old admin `ChargeCcAsync` wrapped the whole flow in `catch (Exception ex)` that stamped placeholder RA `Active=false` with `Comment="FAILED: {ex.Message}"`. New engine and the new admin wrapper at `RegistrationSearchService.cs:426` drop this. A thrown exception leaves the placeholder RA `Active=true Payamt=0` AND propagates as raw HTTP 500.

**Failure scenarios.**
- Prod Customer row has null `AdnLoginId` → `GetJobAdnCredentials_FromJobId` throws `InvalidOperationException` before the engine's null-check at `:1349` ever runs. Friendly `MISSING_GATEWAY_CREDS` never reaches the client.
- Network/TLS hiccup during `ADN_Charge` → SDK throws. Placeholder RA at `:1373` already committed. Failure-branch save at `:1415` never runs. Row sits `Active=true Payamt=0 AdnTransactionId=null` forever. The orphan detector keys on missing-RA-for-transId so this inverse orphan slips past.

---

## Issue 5 — `BActive=true` no longer set on parent CC success (VERIFIED) — **HIGH**

`PaymentService.cs:1448-1452` — the new engine's success branch sets `PaidTotal`, `OwedTotal`, `Modified`, `LebUserId`. It does NOT set `reg.BActive = true`. The old parent path went through `UpdateRegistrationsForCharge` at `:1737` which sets `BActive = true`. eCheck (`:1133`) and ARB (`:1751`) still set it; parent CC regressed.

**Failure scenario.** Parent self-pays $250 by CC for first time. Charge succeeds; `PaidTotal` advances; `OwedTotal=0`. But `BActive` stays at default. Downstream consumers gating on `BActive==true` (active-player rosters, coach views, `myActivePlayers` pulse counts, batch email recipients, the `BActive==true` filter in `GetSuperUserRegistrationsAsync`) silently exclude the paid player. Coach asks "where's my player?" — they exist, they paid, BActive is `null`.

**Test gap (see also Issue 14).** The new 9-test `PlayerCcChargeTests` never asserts `BActive` on success. The test factory `Reg()` at `:100` leaves `BActive` at default (`bool? = null`), divergent from `RegistrationDataBuilder.BuildRegistration` which sets `BActive=false`. CI cannot catch this regression.

---

## Issue 6 — SyncRep drops PaidTotal when paid team goes Active=false (PLAUSIBLE) — **MEDIUM**

`RegistrationRepository.cs:805-848` — `SynchronizeClubRepFinancialsAsync` aggregates ONLY `t.Active == true` teams (line 809) and non-WAITLIST/DROPPED agegroups, then unconditionally overwrites `rep.PaidTotal` at line 843. Any new call site that allows a PAID team to transition to `Active=false` drops that team's PaidTotal from the rep's aggregate even though the money is still settled at ADN.

**Failure scenario.** Rep has 3 paid teams ($1000 each, `rep.PaidTotal=$3000`). Admin soft-deletes team 3 via `LadtService.DeleteTeamAsync`. Old admin code didn't sync; new code does. Aggregate now excludes team 3: `rep.PaidTotal=$2000`. Team 3's $1000 still sits in its RA rows. Rep's stored aggregate underrepresents what they paid by $1000.

**Probe.** `UnregisterTeamFromEventAsync` guards with `PaidTotal>0→throw`. Confirm: does `LadtService.DeleteTeamAsync` block soft-delete of paid teams? Does `TeamSearchService.EditTeamAsync` block toggling Active=false on paid teams? If either path admits a paid team to Active=false, the bug is live.

---

## Issue 7 — Same-family concurrent submit → unguarded race (PLAUSIBLE) — **MEDIUM**

`PaymentService.cs:1307` — no server-side serialization for concurrent CC charges against the same `(jobId, familyUserId)`. The only defenses are the per-tab frontend `submitting` signal (per-component-instance) and ADN's dedup window. Engine doesn't lock the family, doesn't check for unfinalized placeholder RAs created seconds ago, doesn't reject if a sibling request is in flight.

**Failure scenario.** Impatient parent opens wizard in two tabs (slow LB caused first request to look hung). Clicks Pay in tab A, then tab B within ~500ms. Each engine independently loads regs, writes N placeholder RAs (2N total), calls `ADN_Charge`. If ADN dedup catches the second: DB carries `success` + `FAILED` RA sets (audit noise). If dedup misses: card double-charged; `PaidTotal` exceeds `FeeTotal`; the new `OwedTotal < 0` clamp at line 1450 silently hides the over-payment so admin sees no negative-owed alert.

**Probe.** Validate ADN dedup behavior on identical (card, amount, invoice) within seconds. Decide: per-family Postgres advisory lock vs frontend idempotency-key check vs DB unique index on `(LebUserId, AdnTransactionId)`.

---

## Issue 8 — RegSaver stamping no longer atomic with charge (VERIFIED) — **MEDIUM**

`PaymentService.cs:1454` (engine save) + `:1520` (RegSaver save) — two distinct `SaveChangesAsync` calls. Old code did one combined save. If the second fails (concurrency conflict, connection blip, statement timeout) the customer is charged for VI but the registration carries no `RegsaverPolicyId`.

**Failure scenario.** Parent buys VI coverage with CC. Engine's first `SaveChangesAsync` commits charge state + success-stamped RA. `ExecutePrimaryChargeAsync` then mutates `reg.RegsaverPolicyId` and calls the second `SaveChangesAsync`. Transient DB error throws. Customer's card was charged for the VI premium; the registration has no policy id; reconciliation requires manual VI-vs-RA cross-walk.

---

## Issue 9 — CancellationToken not propagated through engine saves (VERIFIED) — **MEDIUM**

`PaymentService.cs:1307` — `ChargeRegistrationsCcAsync` accepts `CancellationToken ct` and propagates it to `_paymentState.ForRegistrationsAsync` and `_registrations.GetByIdsAsync` — but drops it on:
- `_acct.SaveChangesAsync()` at `:1373` and `:1415`
- `_registrations.SaveChangesAsync()` at `:1454`
- `GetJobAdnCredentials_FromJobId` at `:1347`
- `BuildInvoiceNumberForRegistrationAsync` at `:1355`

Also: `ExecutePrimaryChargeAsync` at `:1497` calls the engine without any token. Mixed surface: reads honor wire cancellation, writes don't.

**Failure scenario.** Client cancels mid-charge (tab close, navigate-away). Cancellation arrives between placeholder save (`:1373`) and success save (`:1454`); ADN still charges; engine commits PaidTotal bump without honoring `ct`. Server state advances for a request the caller no longer awaits.

---

## Issue 10 — Batched invoice → P1 orphan detector misattributes (PLAUSIBLE) — **MEDIUM**

`PaymentService.cs:1355` — parent batch charges N kids in ONE ADN tx with ONE invoice number derived from `items[0].RegistrationId`. ALL N placeholder RA rows are stamped with that same `AdnInvoiceNo` at `:1441`. P1's orphan detector (`AdnSweepService.GetByInvoiceAisAsync`) parses the invoice `customer_job_reg` → a SINGLE reg — it cannot represent a batched orphan.

**Failure scenario.** Parent pays $750 = $200 (KID1) + $300 (KID2) + $250 (KID3) in one ADN charge. Invoice = `cust_job_KID1AI`. If placeholder RA inserts for KID2 & KID3 fail to persist (the very scenario P1's detector exists to catch), the detector parses the invoice and resolves the orphan to KID1 alone. Director books $750 against KID1 thinking they over-charged — but $550 actually belongs to siblings.

**Probe.** Confirm `AdnSweepService.GetByInvoiceAisAsync` doesn't handle multi-reg invoices today; design invoice format that encodes the batch (e.g. `cust_job_KID1AI_KID2AI_KID3AI` or a separate `BatchId`).

---

## Issue 11 — Admin error wrapper drops `ErrorCode` (VERIFIED) — **LOW**

`RegistrationSearchService.cs:437` — admin wrapper forwards `result.Message` into `RegistrationCcChargeResponse.Error` and DROPS `result.ErrorCode`. The engine emits `AMOUNT_MISMATCH / INVALID_AMOUNT / REG_WRONG_JOB / NO_ITEMS / MISSING_GATEWAY_CREDS / CHARGE_GATEWAY_ERROR` — admin client sees them all collapsed to one untyped string. Old admin path's per-condition copy is also gone — any admin UI that substring-matched the legacy text silently breaks.

**Blast radius.** Admin UX degradation only — no money loss. But programmatic differentiation (auto-reload on stale, retry button on decline, "wrong job" hard-error) is unreachable.

---

## Issue 12 — Placeholder `Dueamt` records wire amount, not resolver amount (VERIFIED) — **LOW**

`PaymentService.cs:1363` — `Dueamt = item.Amount` snapshots the SUBMIT-TIME wire value, not the canonical `owed.Cc`. Combined with Issue 2 (unidirectional tripwire), any case where `Amount` is within tolerance but the resolver moved permanently records a `Dueamt` the resolver never agreed with.

**Failure scenario.** Wizard cached `owed=$200`. Between display and submit, `owed.Cc` moves to $200.50. Parent submits $200; tripwire allows ($200 ≤ $200.51); engine writes `Dueamt=$200, Payamt=$200`. The RA permanently records `$200` while the resolver at charge time disagreed. Future resolver-vs-RA reconciliation flags drift without obvious cause.

---

## Issue 13 — RegSaver stamping uses `DateTime.Now` while engine uses `UtcNow` (VERIFIED) — **LOW**

`PaymentService.cs:1516-1517` — `RegsaverPolicyIdCreateDate = request.ViPolicyCreateDate ?? DateTime.Now` and `reg.Modified = DateTime.Now`. The canonical engine uses `DateTime.UtcNow` (lines 1444, 1446, 1451). The same registration row therefore lands with `Modified=Local` overwriting the `Modified=UtcNow` set seconds earlier.

**Failure scenario.** Server runs ET (UTC-5). Parent buys VI; engine sets `Modified=2026-05-27 14:00 UTC`; RegSaver stamp overwrites with `Modified=2026-05-27 09:00:00`. Downstream incremental sync `WHERE Modified >= '2026-05-27 12:00 UTC'` skips this row. (Note: eCheck's `UpdateRegistrationsForCharge` at `:1738` also uses `Now`, so the codebase is partially inconsistent; this commit widens the gap by writing Modified twice with different Kinds on the same row.)

---

## Issue 14 — Test suite gaps (VERIFIED) — **LOW (resilience)**

`PlayerCcChargeTests.cs`:
- **No assertion on `BActive`** after a successful charge. Issue 5's regression is invisible to CI.
- **`ForRegistrationsAsync` is always stubbed to an empty dictionary**, forcing the `emptyState` fallback. The real per-reg state branch through `ResolveOwed` is never exercised — any bug where states return CcRate but missing GrossPaid would never trip these tests.
- **No test exercises `ADN_Charge` THROWING** (only the soft-fail "decline response" path). Issue 4's orphan-placeholder scenario is uncovered.
- **`Reg()` test factory at :100** duplicates `RegistrationDataBuilder.BuildRegistration` with different defaults (e.g. `BActive=null` vs `BActive=false`) — tests pass against entities production code would reject.

---

## Issue 15 — ALTITUDE: `SynchronizeClubRepFinancialsAsync` at 15+ sites — **MEDIUM**

`RegistrationRepository.cs:800` — the sync method is now called from LadtService×5, TeamSearchService×4, TeamRegistrationService×3, PaymentService×3, AdnSweepService, PoolAssignmentService, TeamRegistrationController. Commit `6ff3a6f8` added 5 more without questioning the pattern. This is the textbook write-chokepoint smell from `feedback_invariants_belong_at_write_chokepoint`.

**Why it's go-live-relevant.** The next team-mutation path added (split, merge, season-rollover, bulk-import, ARB cancel, refund-team) is one more place to forget the sync — the regression `6ff3a6f8` fixes will recur. The root altitude is a `SaveChangesInterceptor` on `SqlDbContext` that detects modified/added/deleted Teams entities and queues sync for affected `ClubrepRegistrationid` values after `SaveChanges`, OR a `TeamRepository.SaveTeamAndSyncRepAsync` wrapper that owns the invariant. Until then, the fixed bug is one merge away from coming back.

---

## Method notes

- Phase 1 finder agents: 9 parallel via `general-purpose` subagents.
- Phase 2 verification: most candidates verified by direct file reading; one PLAUSIBLE candidate (Issue 3) verified by a dedicated verifier agent which CONFIRMED the eCheck combo trigger and REFUTED the admin-check trigger.
- Phase 3 sweep: added Issues 7, 9, 12, 14 (idempotency/concurrency/Dueamt/test-gaps).
- Cleanup/altitude findings demoted: the 166-line method (`ChargeRegistrationsCcAsync`), `methodRate` redundant param on `ProcCreditForCharge`, unused `total`/`effectiveOption` on `ExecutePrimaryChargeAsync`, hand-rolled test factories, 4× duplicated `getTeamsMetadata` subscribe pattern in `payment-step.component.ts`. Real but subordinate to correctness — not enumerated here.
