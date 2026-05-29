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

- [x] Issue 1 — PIF upgrade persists on declined CC (initial fix `bbbfd960` was a no-op; corrected in `e78a4b00`)
- [x] Issue 2 — AMOUNT_MISMATCH only catches OVER, not stale-LOW — **REFUTED** (see below)
- [x] Issue 3 — OwedTotal recompute over-credits prior-eCheck combo — **VERIFIED, demoted to LOW** (parked behind `eCheck_newly_introduced`)
- [x] Issue 4 — No try/catch around ADN_Charge or credentials lookup — **VERIFIED, demoted to MEDIUM** (regression from legacy, not catastrophic-class)
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

## Issue 1 — PIF upgrade persists on declined CC (VERIFIED → FIXED `e78a4b00`) — **HIGH**

`PaymentService.cs:1373` — `_acct.SaveChangesAsync()` is called BEFORE the ADN call to commit the placeholder RA rows. Because `_acct` and `_registrations` share the same scoped `SqlDbContext`, that save also flushes the `UpgradeRegistrationsToPifAsync` mutations (FeeBase/FeeTotal/OwedTotal swap from Deposit to PIF) made earlier in the request.

**Failure scenario.** Parent picks PIF on a $500 deposit reg (becomes a $1900 PIF). `UpgradeRegistrationsToPifAsync` mutates the tracked entity. `ChargeRegistrationsCcAsync` placeholder-save at `:1373` commits the PIF state. ADN declines. The failure branch at `:1415` flips RA `Active=false` but does NOT revert the PIF upgrade. Caller returns failure; registration permanently shows `FeeTotal=$1900 OwedTotal=$1900 PaidTotal=$0`. Old code only saved after a successful charge.

**Blast radius.** Parent declines CC mid-PIF, their registration silently jumps to PIF posture with nothing paid — wizard re-entry now demands $1900 they never agreed to commit to.

**Fix history.**
- `bbbfd960` — **no-op.** Captured the snapshot inside `ExecutePrimaryChargeAsync`, but `UpgradeRegistrationsToPifAsync` runs upstream in `ProcessPaymentAsync` at line 989 BEFORE `ExecutePrimaryChargeAsync` is even called, so the snapshot held POST-PIF values. The failure-branch "restore" wrote the same values back. Bug stayed live.
- `e78a4b00` — **fixed.** Snapshot is now captured in `ProcessPaymentAsync` immediately before `UpgradeRegistrationsToPifAsync`, then passed into `ExecutePrimaryChargeAsync` as a parameter. Failure branch restores from the genuine pre-PIF values. End-to-end regression test (`PlayerCcPaymentServiceTests.PifDecline_RestoresPrePifFeeFields`) stubs `ApplyPifUpgradeAsync` to actually mutate the tracked reg ($500→$1900) and ADN_Charge to decline; asserts `FeeBase==$500` after the failure return.

---

## Issue 2 — AMOUNT_MISMATCH only catches OVER, not stale-LOW — **REFUTED**

**Original concern.** `PaymentService.cs:1340` — `if (item.Amount > owed.Cc + 0.01m)` is unidirectional; a drifted-UP `owed.Cc` between wizard display and submit lets the stale-LOWER `item.Amount` pass silently.

**Why refuted (parent path).** On parent self-pay, `item.Amount` is NOT a wizard-supplied wire value — it is computed server-side in `ComputeChargesAsync` at `PaymentService.cs:1713` from the SAME tracked `Registrations` entities the engine then resolves owed against. By construction `item.Amount == owed.Cc` (mod proc-fee bucket arithmetic the resolver already accounts for). The tripwire on the parent path is structurally tautological — there is no drift window to exploit.

**Why refuted (admin path).** Admin DOES pass a wire amount (`RegistrationCcChargeRequest.AmountCharge`), but the unidirectional tripwire is **intentional**: admin is allowed to undercharge (e.g., partial payment, write-off the residual) but not overcharge. Symmetric tripwire would break that legitimate admin workflow.

**No change required.** The original failure scenario (a wizard-cached $250 against a backend $300) was speculative — there is no path where parent submits a free-text amount and the server trusts it without recomputing.

---

## Issue 3 — OwedTotal recompute over-credits prior-eCheck combo (VERIFIED) — **LOW** (parked behind `eCheck_newly_introduced`)

**Mechanism.** Confirmed by reading the cited lines.
- eCheck credit at `PaymentService.cs:1115-1116` decrements `FeeProcessing` and `OwedTotal` but does NOT mirror to `FeeTotal`.
- `UpdateRegistrationsForCharge` at `PaymentService.cs:1761-1773` then bumps `PaidTotal += echeckCharge` and decrements `OwedTotal` (still does not touch `FeeTotal`).
- A subsequent CC charge through `ChargeRegistrationsCcAsync` at `PaymentService.cs:1461` recomputes `reg.OwedTotal = reg.FeeTotal - reg.PaidTotal` against the stale `FeeTotal` — producing a spurious residual equal to the eCheck-time credit.
- Admin-check path at `RegistrationSearchService.cs:413` is structurally safe: it recomputes `reg.FeeTotal = reg.FeeBase + reg.FeeProcessing - reg.FeeDiscount + reg.FeeDonation + reg.FeeLatefee` after the `FeeProcessing` change.

**Original failure-scenario numbers were wrong.** `ProcCreditForCharge` at `PaymentState.cs:152` returns 0 when `ccCharge ≤ principalRemaining` (where `principalRemaining = FeeBase - discount + lateFee - PrincipalPaid`). A $100 deposit against a $500 FeeBase therefore yields credit = 0 and does NOT trigger the mechanism. The real triggers are: (a) full-pay eCheck on a job with proc fees, where `ccCharge = OwedTotal` includes proc and exceeds principal; (b) deposit-phase eCheck on a job where `team.deposit ≥ FeeBase` (deposit covers or exceeds principal).

**Corrected failure scenario.** Reg FeeBase=200, FeeProcessing=40, FeeTotal=240, OwedTotal=240, PaidTotal=0, team.deposit=230. Parent pays deposit via eCheck: `ccCharge = min(230, 240) = 230`, `principalRemaining = 200`, `procEmbeddedInCharge = max(0, 230-200) = 30`, `credit = min(rawCredit, 30) > 0` (say $5). Engine gateway-charges $225, then `FeeProcessing = 35`, `OwedTotal = 235 − 225 = 10`, `FeeTotal` stays 240. Parent later CCs the $10 balance. New engine: `PaidTotal = 235`, `OwedTotal = 240 − 235 = $5` residual. Old `UpdateRegistrationsForCharge` would have resolved to $0.

**Direction of error.** Registrant is shown a spurious "$5 still owed" after paying in full at the (intended-discounted) eCheck rate. They either pay the residual (slight OVER-collection on the merchant side; no silent under-charge) or ignore it. Magnitude is bounded by the rate delta × charged principal-overhang — typically single-digit dollars.

**Why demoted to LOW.**
1. Live exposure is currently zero — per `eCheck_newly_introduced`, eCheck just launched with no prod sign-ups, so no registrant has hit this path.
2. The bug favors over-collection, not under-collection or merchant loss — not catastrophic-class for the 003 charter.
3. Admin path (the higher-volume settlement path) is structurally safe.

**Suggested fix (deferred).** Mirror `FeeTotal` to the credit decrement at `PaymentService.cs:1115` (or recompute `FeeTotal = FeeBase + FeeProcessing - FeeDiscount + FeeDonation + FeeLatefee` after the credit, matching the admin pattern at `RegistrationSearchService.cs:413`). No action pre-go-live; revisit when eCheck has live volume or when the canonical recompute is otherwise being touched.

---

## Issue 4 — No try/catch around ADN_Charge or credentials lookup (VERIFIED) — **MEDIUM** (regression from legacy)

**Mechanism.** Confirmed by reading the cited sources.
- Credentials lookup at `PaymentService.cs:1359` (was `:1347` in original draft) and the gateway call at `:1392` (was `:1380`) are both bare — no try/catch wrap.
- `AdnApiService.GetJobAdnCredentials_FromJobId` at `:58` THROWS `InvalidOperationException` when prod creds row is null/empty — it does NOT return null. The engine's null-check at `PaymentService.cs:1360` is therefore unreachable for the prod-misconfig case.
- `AdnApiService.ExecuteTransaction` at `:503` calls `controller.Execute()` bare — the Authorize.Net SDK can throw on network/TLS errors.
- Legacy admin `ChargeCcAsync` (pre-`893618de`) wrapped its body in `try { … } catch (Exception ex) { return $"Charge failed: {ex.Message}" }` at lines 439/533-536 of the pre-refactor file. The new engine and admin wrapper at `RegistrationSearchService.cs:426` dropped this wrap — confirmed regression.

**Failure modes.**
- **Mode A — prod creds misconfig.** Throw fires at `:1359` before the placeholder RA at `:1385` is created. User sees raw HTTP 500. One-time per customer setup error; doesn't recur once creds are configured.
- **Mode B — SDK throw during gateway call.** Placeholder RA at `:1385` is already saved when `:1392` throws. The failure branch at `:1415-1427` only fires on ADN *error responses*, not exceptions, so the row stays permanently `Active=true Payamt=0 AdnTransactionId=null`. User sees raw HTTP 500.

**Orphan-detector probe.** `AdnSweepService.DetectOrphanAsync` at `:718` iterates ADN-side transactions and tests each by `transId` against local RAs. Mode B's local-only orphan (transId=null) is invisible to it — there is no ADN transId to match. So the local placeholder is NOT auto-resolved. Note: the detector still catches the inverse case (money moved at ADN, no local row) if the SDK throw happened after ADN processed the request.

**Why demoted from HIGH to MEDIUM.** Within the 003 charter (catastrophic-class = money-wrong / data-leak / silent corruption / auth bypass / mass comms failure), Mode B does NOT move money wrong — if the SDK threw before reaching ADN, no charge occurred; if after, the ADN-side detector catches it. Real impact is (a) bad UX (raw 500 instead of friendly "Charge failed" message) and (b) orphan placeholder RAs polluting per-registrant payment history in admin views. Both are regressions from legacy's behavior, but neither is business-ending.

**Suggested fix (deferred).** Lift `try { … } catch (Exception ex) { stamp placeholder Active=false Comment="FAILED: {ex.Message}", _logger.LogError, return FailCcResult("CHARGE_GATEWAY_ERROR", $"Charge failed: {ex.Message}", regIds) }` around the body of `ChargeRegistrationsCcAsync` from the credentials lookup onward. Restores legacy contract + audit trail. Same shape should apply at `ExecuteEcheckChargeAsync` (PaymentService.cs:1087) and `ProcessArbAsync` (`:1244`), which share the bare-call pattern.

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
