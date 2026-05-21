# 002 — Money Moves Wrong

**Status:** open (started 2026-05-20)
**Risk class:** catastrophic — a wrong amount charged/refunded, a charge to the wrong merchant, or a payment recorded twice is business-ending. Money is the product; if the books are wrong, customer trust and the tournament cash flow both break.

## The fear

The system charges, refunds, and recurring-bills real credit cards and bank accounts through Authorize.Net. The catastrophic shapes:

1. **Wrong amount charged** — the gateway is sent a number the server never independently computed, so a buggy or tampered client decides how much money moves.
2. **Money recorded twice / lost** — a payment that exists at ADN but not in our books (or exists twice), driving `PaidTotal` / `OwedTotal` wrong.
3. **Recurring billing on the wrong schedule** — an ARB subscription created from fabricated installment counts/intervals quietly charges a card N times wrong.
4. **Refund exceeds the original** — partial refunds that aren't tracked cumulatively, or refunds to the wrong transaction/job.
5. **Charge to the wrong merchant** — per-job ADN credentials resolved incorrectly so customer A's money lands in customer B's merchant account.

## Method

001's discipline applies: **claimed ≠ working — verify by reading source.** Every issue below is tagged:

- **VERIFIED** — confirmed by reading the cited `file:line` this session.
- **OPEN** — flagged by reconnaissance, not yet confirmed; a probe is named.

No fixes in this document. Findings only. Each fix (especially anything touching schema or production money paths) is its own decision with explicit go-ahead.

## The two charge architectures (the crux)

There are two independent payment-amount architectures in the codebase, and they differ on the single most important question — *who decides the amount that hits the card*:

| | Player path | Team (club-rep) path |
|---|---|---|
| Entry | `PlayerRegistrationPaymentController` → `PaymentService.ProcessPaymentAsync` | `TeamPaymentController` → `PaymentService.ProcessTeamPaymentAsync` / `…EcheckPaymentAsync` |
| Amount source | **Server-computed** — `ComputeChargesAsync` (`PaymentService.cs:1549`): PIF uses server-side `reg.OwedTotal`; Deposit goes through `ResolvePerRegistrantAsync`, the single source of truth | **Client-submitted** — `request.TotalAmount` flows straight through; split `totalAmount / teamIds.Count` and sent to the gateway (`PaymentService.cs:105,143`) |
| Server reconciliation vs. owed? | n/a (server owns the number) | **None** |
| Validator | n/a | `TotalAmount > 0` only (`TeamPaymentDtos.cs:26,50`) |

The player path treats the server as the authority on money. The team path treats the client as the authority. That asymmetry is Issue 1.

## Issues found

### Issue 1 — Team-payment amount is whatever the client says (VERIFIED — CC + eCheck paths FIXED 2026-05-20) — **HIGH**

`TeamPaymentController.ProcessTeamPayment` (`TeamPaymentController.cs:35`) and its eCheck sibling (`:88`) take `request.TotalAmount` from the body and pass it to `PaymentService.ProcessTeamPaymentAsync` (`PaymentService.cs:59`) / `ProcessTeamEcheckPaymentAsync` (`:247`). The service:

- splits `perTeamAmount = totalAmount / teamIds.Count` (`:105`);
- sends `Amount = perTeamAmount` to `ADN_Charge` (`:143`) — **the amount charged to the real card is the client number**;
- on overpayment, silently rewrites the books to match: if `team.PaidTotal > team.FeeTotal`, it sets `OwedTotal = 0; FeeTotal = PaidTotal` (`:179-183`) — an overcharge is absorbed as if the fee were higher, hiding it.

The only guard is `TotalAmount > 0` (`TeamPaymentDtos.cs:26,50`). Teams are checked for *existence* (`:95`) but the amount is never reconciled against `sum(team.OwedTotal)`. Contrast the player path, which computes server-side and never trusts a client amount.

**Blast radius:** a buggy frontend `balanceDue()` charges the wrong amount with no server backstop; a crafted request can under- or over-pay; an overcharge silently inflates the recorded fee. This is the single largest "money moves wrong" surface.

**Deeper finding (max-mode review):** the equal split has a *second* defect independent of client-trust — `totalAmount / teamIds.Count` mis-allocates whenever teams owe **different** amounts. Two teams owing $300 and $500, paid with a correct $800 total, were each charged $400 → one overpays (and the `:179-183` block silently inflates its fee), the other underpays. Correct total in, wrong books out.

**The right shape already exists in-codebase:** `RecordCheckOrCorrectionInternalAsync` (`TeamSearchService.cs:568`) — the Director check/correction handler — validates amount-≤-owed and allocates per-team from `OwedTotal` / `PaymentState.PrincipalRemaining`. The rep CC/eCheck paths should mirror it. Also confirmed: `team.OwedTotal` is the single source of truth — resolver-derived, phase-aware (deposit-only vs full per `Jobs.BTeamsFullPaymentRequired`), CC-proc-inclusive, net of prior payments (`FeeResolutionService.ApplyTeamProcessingAndTotalsAsync`, `:235-370`).

**RESOLUTION — CC + eCheck paths FIXED (2026-05-20).** Both `ProcessTeamPaymentAsync` (CC) and `ProcessTeamEcheckPaymentAsync` (eCheck) are now thin wrappers over one unified per-team engine, `ChargeTeamsAsync`. The engine charges each team its own server-computed amount (CC: `team.OwedTotal`; eCheck: the eCheck gross — see Issue 5 for the model), drops the equal split, removes the `:179-183` fee-absorption block, and applies the `AMOUNT_MISMATCH` tripwire to **both** methods (rejects when the client `totalAmount` disagrees with the server-computed total by >$0.01). Regression tests: `TeamCcPaymentServiceTests` (mixed fee structures → each team charged its own owed; tripwire; nothing-due; team-not-found) and `EcheckTeamPaymentServiceTests` (each team its own eCheck gross, never an equal split; eCheck-gross-not-CC-gross; CC-total-submitted-for-eCheck → `AMOUNT_MISMATCH`). Full suite green (440). **Team-ownership authz gap noted — see Issue 6.**

### Issue 2 — ARB schedule fabricates business values when job config is null (VERIFIED code; OPEN reachability) — **MEDIUM-HIGH**

`BuildArbSchedule` (`PaymentService.cs:1582`) defaults a recurring-billing plan when the job's config columns are null:

```csharp
short o = (short)(occur ?? 10);       // billing occurrences → 10
if (o <= 0) o = 10;
short i = (short)(intervalLen ?? 1);  // interval length → 1
if (i <= 0) i = 1;
var s = start ?? DateTime.Now.AddDays(1); // start → tomorrow
```

If an ARB-enabled job is missing `AdnArbbillingOccurences` / `AdnArbintervalLength` / `AdnArbstartDate`, the system **invents** a 10-occurrence, 1-unit, starts-tomorrow recurring charge instead of refusing. This is exactly the "never infer business defaults" anti-pattern — applied to *recurring* money, so the error compounds across installments. The per-occurrence amount is then `OwedTotal / occur` (`:1465` per recon), so a wrong `occur` also makes every installment the wrong size.

**Probe:** query prod `Jobs` for ARB-enabled rows where any of the three columns is null — that tells us whether this is reachable today or purely theoretical. Then decide: fail-loud vs. keep defaults.

### Issue 3 — ARB sweep double-import: non-atomic dedup, no DB constraint (VERIFIED) — **MEDIUM**

The reconciliation sweep imports settled/declined ARB transactions into `RegistrationAccounting`, bumping `PaidTotal`/`OwedTotal`. Dedup is a read-then-write check, `AnyByAdnTransactionIdAsync(tx.transId)` (`AdnSweepService.cs:234`) — **not atomic**, and there is **no unique index** on `RegistrationAccounting.AdnTransactionId` (`SqlDbContext.cs:5514` maps it as a plain column).

A manual run (`AdnSweepController` → `POST /api/admin/adn-sweep`, SuperUserOnly, `AdnSweepController.cs:30`) can overlap the nightly `BackgroundService` run; both can pass the existence check before either inserts → **duplicate accounting rows → `PaidTotal` double-counted → `OwedTotal` driven negative**. This does *not* double-charge the card (ADN already charged) — it corrupts the local books.

**Asymmetry confirming the gap is real:** the eCheck settlement path *is* DB-protected — `echeck.Settlement` has `UQ_echeck_Settlement_txID` and `UQ_echeck_Settlement_raID` unique indexes (`SqlDbContext.cs:6030-6032`). The ARB import target has no equivalent.

**Note (SCHEMA-CHANGE-REVIEW):** the obvious fix touches schema (unique index on `RegistrationAccounting.AdnTransactionId`) — but that column is also used by manual CC charges/refunds and may legitimately repeat or be null. **Do not add the index without first auditing every writer of that column.** Discuss before acting.

### Issue 4 — Refund guard is per-call, not cumulative; no refund idempotency (VERIFIED) — **MEDIUM**

`RegistrationSearchService.ProcessRefundAsync` (`:209`) bounds the refund by `request.RefundAmount > 0 && <= original.Payamt` (`:227`). On the settled-refund branch it writes a **new negative `RegistrationAccounting` row** (`:295-311`) and **never decrements `original.Payamt`**. So a second refund against the same `AId` is again bounded by the *full original* amount — cumulative partial refunds can exceed the original charge at the application layer. (The void branch is safe: it sets `original.Payamt = 0`, `:268`.)

ADN itself rejects an over-refund, so the *actual money* is backstopped — but the local books can drift, and there is no idempotency key on the refund endpoint (a double-submit re-attempts). Job scoping is correct (`original.Registration.JobId == jobId`, `:218`; controller is AdminOnly), so cross-job refunds are not possible.

**Probe:** decide whether to track refunded-to-date against the original row (and whether to add an idempotency guard mirroring the player charge path).

### Issue 5 — eCheck proc-fee credit applied but the charge isn't reduced → overcharge (VERIFIED — FIXED 2026-05-20) — **HIGH**

Found while verifying the eCheck amount for Issue 1. In **both** the player (`ExecuteEcheckChargeAsync`) and team (`ProcessTeamEcheckPaymentAsync`) eCheck paths, the EC credit *reduces* `FeeProcessing`/`OwedTotal`, but the gateway is charged the **pre-credit** figure:

- Player: `total = charges.Values.Sum()` (`PaymentService.cs:1073`, from CC-inclusive `OwedTotal`); credit applied (`:1146`); gateway charged `total` (`:1163`); then `OwedTotal -= charges[regId]` (`:1172`).
- Net with $1000 principal + $35 CC proc, EC rate 1%: credit ≈ $25.88, gateway charged **$1035**, `OwedTotal` ends at **−$25.88**. Customer pays the full CC-inclusive amount for an eCheck; books show a negative balance.

The path's own comment (`:1141`) says the charge should "match the now-reduced obligation" — the code contradicts it. `EcheckPartialCredit = principal × (ccRate − ecRate)` (`PaymentRateMath.cs:25`). Team path has the same shape (`:296` reduce, `:313` charge).

**Why invisible:** eCheck tests use `feeProcessing = 0` (`EcheckPaymentServiceTests.Reg`), so the credit is a no-op and CI never exercises it.

**OPEN — reachability:** do eCheck-enabled jobs run with `BAddProcessingFees` on? If never, latent. Cheap probe: a proc-fee-on eCheck test asserting gateway amount + final `OwedTotal`, plus a prod query for eCheck jobs with processing fees enabled.

**Why the eCheck half of Issue 1 was not fixed with the CC half:** the team eCheck amount can't be made correct without fixing this shared overcharge at the source. Deserves its own pass.

**RESOLUTION — FIXED 2026-05-20 (root-cause model change, both paths).** The root cause was that eCheck was modelled **asymmetrically** from CC: CC stores its gross in `Payamt` and reverses the principal out at `ccRate`, but eCheck stored only the principal, so `GrossPaid` counted eCheck as principal-only while `FeeProcessingTarget` still preserved the collected eCheck proc — `OwedTotal` could never reach 0. The fix makes **eCheck CC-symmetric**:

- `PaymentState.EcheckPrincipalPaid` → renamed `EcheckGrossPaid`; principal/proc are now *derived* (`principal = gross / (1 + echeckRate)`, `proc = gross − principal`), exactly mirroring the CC reverse-out. `GrossPaid` now sums eCheck **gross**.
- One shared proc-math helper in `PaymentRateMath`: `ProcCredit(principal, ccRate, methodRate)` (the legacy `EcheckPartialCredit` / `NonProcCheckCredit` now delegate to it). Per-payment credit is `principalRemaining × (ccRate − methodRate)`, **proportional to the payment's principal** — partial payments (corrections, deposits) credit only their share.
- Both eCheck paths debit the gateway the **eCheck gross** = CC charge − credit, store that gross in `Payamt`, book the credit against `FeeProcessing`/`OwedTotal`, and accumulate the gross into `PaidTotal`. A full eCheck now lands `OwedTotal` at 0 (was −proc), `FeeProcessing` at `principal × echeckRate` (the retained eCheck proc). Team path = `ChargeTeamsAsync`; player path = `ExecuteEcheckChargeAsync` (no longer routes through `RegistrationFeeAdjustmentService`).
- Tests now exercise proc-fee-on eCheck (the old `feeProcessing = 0` blind spot is closed): `EcheckTeamPaymentServiceTests` + `EcheckPaymentServiceTests` assert gateway = eCheck gross (not CC gross), `Payamt` = gross, `OwedTotal` → 0, per-reg/per-team proportional credit. `PaymentStateTests` updated to gross inputs. Full suite green (440).

**Follow-up (frontend, OPEN — not code-complete):** the eCheck total is now genuinely **lower** than the CC total (by the proc-rate spread). The team eCheck self-pay UI must display and submit the *eCheck* total, or the `AMOUNT_MISMATCH` tripwire (now active on eCheck too) fails the payment closed. This is a deliberate fail-safe, but it implies a UX choice — *eCheck shown as the cheaper method* — that needs a frontend pass + product confirmation before go-live. The player path computes server-side and submits no client total, so it is unaffected.

### Issue 6 — Team payment doesn't enforce team ownership (VERIFIED) — **MEDIUM (wrong-person, not wrong-amount)**

`ProcessTeamPaymentAsync` / `ProcessTeamEcheckPaymentAsync` accept client `TeamIds` and validate only that the teams exist in the job (`:95`), not that each team's `ClubrepRegistrationid == regId` (the paying rep). The comment at `:223-229` *asserts* this invariant; no code enforces it. A rep could submit another rep's team IDs (same job); the resulting `RegistrationAccounting` would carry `RegistrationId = payer` but `TeamId = other rep's team`, corrupting that team's books. Pre-existing; not introduced by the Issue 1 fix. More "wrong-person touches wrong data" than "money moves wrong" — candidate for investigation 003, flagged here because it's adjacent.

## Open probes (reconnaissance-flagged, not yet verified)

| # | Probe | Why it matters | First step |
|---|---|---|---|
| P1 | Charge persisted **after** gateway approval with no compensating action if `SaveChangesAsync` then fails | Money at ADN, no local record → customer charged, system thinks unpaid | Read `ExecutePrimaryChargeAsync` post-gateway block; check for outbox/retry/try-catch around the save |
| P2 | Per-job ADN credential isolation — `GetJobAdnCredentials_FromJobId` assumes job→customer→creds is 1:1 | Charge to wrong merchant = catastrophic | Read the resolver; confirm the join and what happens if a job has no/ambiguous customer creds |
| P3 | eCheck **optimistic credit at submit** + return reconciliation depends on ADN emitting a `returnedItem` tx with `refTransId`; no fallback if absent | Bounced eCheck never reversed → phantom paid balance | Read `ProcessEcheckReturnAsync`; confirm the no-`refTransId` path |
| P4 | Processing-fee credit applied **before** charge in some paths; **not unwound** on refund | Fee drift on failed charges / refunded eChecks | Trace `RegistrationFeeAdjustmentService` callers around charge + refund |
| P5 | Team "capture-what-you-can" partial success keeps already-charged teams on per-team failure (`:226-234`) | Is partial payment an intended UX or a money-state hazard? | Confirm intended behavior with user; check what the client shows on `PARTIAL_SUCCESS` |
| P6 | ARB per-occurrence amount = `OwedTotal / occur` snapshotted at creation; stale if `OwedTotal` later changes (discount/refund) | Recurring charges keep billing the old amount | Verify `CreateArbSubscriptionsAsync`; compare to legacy ARB behavior |

## Probes (original investigation plan)

- **(A)** Map both charge architectures and confirm the player-vs-team amount-authority asymmetry. ✅ done (Issue 1).
- **(B)** Verify recurring-billing schedule + amount derivation. ✅ partial (Issue 2; P6 open).
- **(C)** Verify reconciliation sweep idempotency end-to-end. ✅ partial (Issue 3; P3 open).
- **(D)** Verify refund correctness and bounds. ✅ done (Issue 4).
- **(E)** Verify per-job merchant-credential isolation. ⬜ open (P2).
- **(F)** Verify charge-to-DB durability (no money-at-ADN-but-not-booked). ⬜ open (P1).

## Verification log

- 2026-05-20 — Reconnaissance via three Explore passes (charge/refund, ARB/sweep, amount-resolution). Treated as leads, not findings.
- 2026-05-20 — Issue 1 verified: read `TeamPaymentController.cs`, `PaymentService.cs:59-245`, `:1549-1580`, `TeamPaymentDtos.cs`. Team path client-trusted; player path server-computed.
- 2026-05-20 — Issue 2 verified (code): `PaymentService.cs:1582-1590`. Runtime reachability open (needs prod `Jobs` query).
- 2026-05-20 — Issue 3 verified: `AdnSweepService.cs:234`, `AdnSweepController.cs`, `SqlDbContext.cs:5514` (no RA constraint) vs `:6030-6032` (Settlement constraints).
- 2026-05-20 — Issue 4 verified: `RegistrationSearchService.cs:209-346`.
- 2026-05-20 — Issue 1 CC path FIXED + tested: `ProcessTeamPaymentAsync`, `TeamCcPaymentServiceTests`. Suite green (437).
- 2026-05-20 — Issue 5 FIXED (root-cause: eCheck made CC-symmetric) + Issue 1 eCheck twin FIXED. Edited `PaymentState`, `PaymentStateService`, `PaymentRateMath` (shared `ProcCredit`), and `PaymentService` (unified team engine `ChargeTeamsAsync`; player `ExecuteEcheckChargeAsync` rewritten off `RegistrationFeeAdjustmentService`). New/updated tests: `EcheckTeamPaymentServiceTests`, `EcheckPaymentServiceTests`, `PaymentStateTests`, `TeamCcPaymentServiceTests`. Suite green (440).

## Status

Six issues verified against source; probes open. **Issue 1 (CC + eCheck) and Issue 5 are FIXED and tested (2026-05-20)** — each team/reg charged its own server-computed amount, `AMOUNT_MISMATCH` tripwire on both team methods, equal split + `:179-183` absorption removed, eCheck made CC-symmetric so a full eCheck lands `OwedTotal` at 0, full suite green (440). **One follow-up remains for go-live: the team eCheck self-pay frontend must show/submit the (lower) eCheck total or the fail-closed tripwire rejects it — UX + product confirmation needed.** Issues 2/3/4/6 and the open probes remain for review.
