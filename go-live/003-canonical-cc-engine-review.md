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
- [x] Issue 5 — `BActive=true` no longer set on parent CC success — **VERIFIED HIGH (catastrophic-class), FIXED `7226210c`**
- [x] Issue 6 — SyncRep drops PaidTotal when paid team goes Active=false — **PLAUSIBLE MEDIUM**, reachability probe pending
- [x] Issue 7 — Same-family concurrent submit → unguarded race — **PLAUSIBLE LOW**, bounded by ADN dedup window
- [x] Issue 8 — RegSaver stamping no longer atomic with charge — **VERIFIED MEDIUM**
- [x] Issue 9 — CancellationToken not propagated through engine saves — **VERIFIED LOW** (operational hygiene)
- [x] Issue 10 — Batched invoice → P1 orphan detector misattributes — **REFUTED as unreachable** (EF Core atomic save)
- [x] Issue 11 — Admin error wrapper drops `ErrorCode` — **VERIFIED LOW** (UX only)
- [x] Issue 12 — Placeholder `Dueamt` records wire amount, not resolver amount — **REFUTED** (parent path is server-computed, same as Issue 2)
- [x] Issue 13 — RegSaver stamping uses `DateTime.Now` while engine uses `UtcNow` — **VERIFIED LOW**
- [x] Issue 14 — Test suite gaps — **VERIFIED LOW** (resilience observation)
- [x] Issue 15 — ALTITUDE: `SynchronizeClubRepFinancialsAsync` at 15+ call sites — **VERIFIED MEDIUM** (architecture concern, deferred)

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

## Issue 5 — `BActive=true` no longer set on parent CC success (VERIFIED → FIXED `7226210c`) — **HIGH (catastrophic-class)**

`PaymentService.cs:1444-1465` — the new engine's success branch set `PaidTotal`, `OwedTotal`, `Modified`, `LebUserId` but did NOT set `reg.BActive = true`. The old parent path went through `UpdateRegistrationsForCharge` at `:1770` which sets `BActive=true`. eCheck (uses `UpdateRegistrationsForCharge` at `:1145`) and ARB (`ApplyArbSuccessToRegistration` at `:1784`) still set it; parent CC silently regressed in `893618de` (2026-05-26).

**Failure scenario.** Parent self-pays $250 by CC for first time. Charge succeeds; `PaidTotal` advances; `OwedTotal=0`. But `BActive` stayed at `false` (the creation default at `PlayerRegistrationService.cs:459, 495`). Every "active registration" surface excluded the paid player: `RegistrationRepository` (8+ queries at lines 49, 129, 154, 179, 207, 241, 267, 284, 311), `ArbSubscriptionRepository.cs:29`, `DivisionRepository.cs:79`, `JobFilterTreeRepository.cs:76`, `AdministratorRepository`, `AdultRegistrationRepository`. Coach roster, batch email, pulse counts, division summary — all silently filter the paid player out.

**Fix.** `7226210c` adds `reg.BActive = true` to the canonical engine success branch and a regression test `CcSuccess_FlipsBActiveTrue` in `PlayerCcPaymentServiceTests`.

---

## Issue 6 — SyncRep drops PaidTotal when paid team goes Active=false (PLAUSIBLE) — **MEDIUM** (reachability probe pending)

`RegistrationRepository.cs:805-848` — `SynchronizeClubRepFinancialsAsync` aggregates ONLY `t.Active == true` teams (line 809) and non-WAITLIST/DROPPED agegroups, then unconditionally overwrites `rep.PaidTotal` at line 843. A paid team transitioning to `Active=false` would drop its PaidTotal from the rep's aggregate while the money still sits in its RA rows.

**Failure scenario.** Rep has 3 paid teams ($1000 each, `rep.PaidTotal=$3000`). A paid team goes Active=false (deletion, edit, or otherwise). SyncRep aggregate now excludes it: `rep.PaidTotal=$2000`. The $1000 still sits in the original team's RA rows but is no longer reflected on the rep.

**Reachability.** Quick grep: `TeamRegistrationService.UnregisterTeamFromEventAsync` guards at `:856` (`if (team.PaidTotal > 0) throw`). `TeamRegistrationService:1024` writes `entity.Active = false` on a different path — guard unverified. `TeamSearchService` and `LadtService` paid-team guards unverified. **Bug is live IF any non-Unregister path admits a paid team to Active=false.** Full audit of every `team.Active = false` write site is needed to close this.

**No code action this session.** Pending reachability audit. Suggested fix shape (if confirmed reachable): add a write-side guard in `SynchronizeClubRepFinancialsAsync` itself — if the aggregate `PaidTotal` would go *down* from the stored value, log + alert (the money owe-side direction is the dangerous one).

---

## Issue 7 — Same-family concurrent submit → unguarded race (PLAUSIBLE) — **LOW** (bounded by ADN dedup)

`PaymentService.cs:1319` — no server-side serialization for concurrent CC charges against the same `(jobId, familyUserId)`. Engine doesn't lock the family or reject if a sibling request is in flight. **However**, `AdnApiService.ExecuteTransaction:528` sets `duplicateWindow=120` on every charge/authorize transaction, so ADN itself rejects identical (card, amount, invoice) within 2 minutes. The realistic failure window is thereby ~120s and the realistic failure mode is "tab A success + tab B 'duplicate' error" — audit-noise placeholder RA pairs but no double-charge.

**Why demoted from MEDIUM to LOW.** ADN's 120s duplicateWindow closes the catastrophic-class window (card double-charged). Remaining impact is admin audit noise (two placeholder RAs per stuttered submit). Frontend per-tab `submitting` signal closes the same-tab repeat. Multi-tab concurrent submits are uncommon. No code action pre-go-live.

---

## Issue 8 — RegSaver stamping no longer atomic with charge (VERIFIED) — **MEDIUM**

`PaymentService.cs:1466` (engine save) + `:1553` (RegSaver save) — two distinct `SaveChangesAsync` calls. Old code did one combined save. If the second fails (concurrency conflict, connection blip, statement timeout) the customer is charged for VI but the registration carries no `RegsaverPolicyId`.

**Failure scenario.** Parent buys VI coverage with CC. Engine's first `SaveChangesAsync` commits charge state + success-stamped RA. `ExecutePrimaryChargeAsync` then mutates `reg.RegsaverPolicyId` and calls the second `SaveChangesAsync`. Transient DB error throws. Customer's card was charged for the VI premium; the registration has no policy id; reconciliation requires manual VI-vs-RA cross-walk.

**No code action this session.** Real but bounded — requires VI purchase AND transient DB error in a ~1s window. Suggested fix: stage the RegsaverPolicyId mutation INSIDE the engine success branch (before the engine's SaveChangesAsync) so it shares the same atomic save. Or: wrap the two saves in a single `TransactionScope`. Defer.

---

## Issue 9 — CancellationToken not propagated through engine saves (VERIFIED) — **LOW** (operational hygiene)

`PaymentService.cs:1319` — `ChargeRegistrationsCcAsync` accepts `CancellationToken ct` and propagates it to reads (`_paymentState.ForRegistrationsAsync`, `_registrations.GetByIdsAsync`) but drops it on writes (`_acct.SaveChangesAsync` at `:1385/:1427`, `_registrations.SaveChangesAsync` at `:1466`), credentials lookup at `:1359`, and `BuildInvoiceNumberForRegistrationAsync` at `:1367`. `ExecutePrimaryChargeAsync` calls the engine without forwarding its (non-existent) token.

**Why LOW.** Honoring `ct` on a charge write that has ALREADY hit ADN would arguably be wrong — we'd want server state to advance to match the gateway side, not bail. The reads-honor-writes-don't asymmetry is therefore defensible. The real gap is the missing `ct` parameter on `ExecutePrimaryChargeAsync` itself (the upstream surface). No catastrophic-class money or data impact. Defer.

---

## Issue 10 — Batched invoice → P1 orphan detector misattributes — **REFUTED as unreachable**

**Original concern.** Parent batches N kids in one ADN tx with one invoice derived from `items[0].RegistrationId`; all N placeholder RAs share that invoice. If RAs for KID2/KID3 fail to persist while KID1's persists, the orphan detector's `(custAi, jobAi, regAi)` parse resolves the orphan to KID1 alone, misattributing $550 of siblings' money.

**Why refuted.** All N placeholder RAs are inserted via `_acct.Add(ra)` in a single `foreach` and committed by ONE `_acct.SaveChangesAsync()` at `PaymentService.cs:1385`. EF Core's `SaveChanges` is atomic — either all N inserts commit or none do. There is no scenario where KID1's RA persists but KID2/KID3 do not. Partial-persist requires an unhandled exception during the save, which rolls back the entire transaction.

**Adjacent concern (still real).** If the orphan detector ALSO does `AnyByAdnTransactionIdAsync(tx.transId)` first (at `:723`), a batched tx with ANY local RA shadows the detector — but this is the same "all-or-nothing" property that makes the misattribution unreachable. The batched-invoice concern only matters if some future change splits the per-reg RA inserts across multiple SaveChanges calls. Note for future audit; not actionable now.

---

## Issue 11 — Admin error wrapper drops `ErrorCode` (VERIFIED) — **LOW**

`RegistrationSearchService.cs:437` — admin wrapper forwards `result.Message` into `RegistrationCcChargeResponse.Error` and DROPS `result.ErrorCode`. The engine emits `AMOUNT_MISMATCH / INVALID_AMOUNT / REG_WRONG_JOB / NO_ITEMS / MISSING_GATEWAY_CREDS / CHARGE_GATEWAY_ERROR` — admin client sees them all collapsed to one untyped string. Programmatic differentiation (auto-reload on stale, retry button on decline, "wrong job" hard-error) is unreachable.

**Blast radius.** Admin UX only — no money loss. No code action this session; the admin tools sit on this surface in a small handful of places and a fix is a one-line forward of `result.ErrorCode` when the admin response DTO is touched. Defer.

---

## Issue 12 — Placeholder `Dueamt` records wire amount, not resolver amount — **REFUTED (parent path)**, **LOW (admin path)**

**Original concern.** `PaymentService.cs:1375` (`Dueamt = item.Amount`) records the wire amount; combined with Issue 2's unidirectional tripwire, drift between display and submit would record a `Dueamt` the resolver never agreed with.

**Parent path — REFUTED.** Same reasoning as Issue 2: `item.Amount` on parent self-pay is server-computed in `ComputeChargesAsync` at `PaymentService.cs:1718` from the same tracked `Registrations` entities the resolver reads. By construction `item.Amount == owed.Cc`. No drift window exists.

**Admin path — LOW.** Admin DOES pass a wire amount through `RegistrationCcChargeRequest.AmountCharge`, so `Dueamt` records the admin-entered value rather than the resolver's view. This is arguably correct (audit captures what admin charged) but worth noting if downstream reconciliation expects `Dueamt` to match `owed.Cc`. No code action.

---

## Issue 13 — RegSaver stamping uses `DateTime.Now` while engine uses `UtcNow` (VERIFIED) — **LOW**

`PaymentService.cs:1549-1550` — `RegsaverPolicyIdCreateDate = request.ViPolicyCreateDate ?? DateTime.Now` and `reg.Modified = DateTime.Now`. The canonical engine uses `DateTime.UtcNow` (lines 1458, 1463). Same row writes `Modified` twice with different Kinds within seconds.

**Failure scenario.** Server runs ET (UTC-5). VI purchase: engine sets `Modified=2026-05-27 14:00 UTC`; RegSaver overwrites with `Modified=2026-05-27 09:00:00` (Local). Downstream incremental sync `WHERE Modified >= '2026-05-27 12:00 UTC'` skips this row.

**Why LOW.** The codebase is broadly inconsistent on Now-vs-UtcNow (eCheck's `UpdateRegistrationsForCharge:1771` also uses `Now`; AddEcheckAccountingEntries uses `Now`). Cosmetic to fix locally; the real fix is a project-wide policy that engine + helpers all read from one clock abstraction. Defer; no go-live blocker by itself.

---

## Issue 14 — Test suite gaps (VERIFIED) — **LOW (resilience)**

Original gaps in `PlayerCcChargeTests.cs`:
- **No assertion on `BActive`** after a successful charge — closed by `PlayerCcPaymentServiceTests.CcSuccess_FlipsBActiveTrue` (`7226210c`).
- **`ForRegistrationsAsync` is always stubbed to an empty dictionary**, forcing the `emptyState` fallback. Real per-reg state branch through `ResolveOwed` is never exercised.
- **No test exercises `ADN_Charge` THROWING** (only the soft-fail "decline response" path). Issue 4's orphan-placeholder scenario is uncovered.
- **`Reg()` test factory** duplicates `RegistrationDataBuilder.BuildRegistration` with different defaults — tests pass against entities production code would reject.

**No code action this session.** Test-hardening sweep is a deferred follow-up; would catch a future Issue-5-shaped regression but doesn't fix one.

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
