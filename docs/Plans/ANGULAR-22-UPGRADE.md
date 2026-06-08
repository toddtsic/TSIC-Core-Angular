# Angular 21 → 22 Upgrade + Reactive Modernization

> **Status: PROCEEDING in an isolated git worktree (approved 2026-06-07).** All upgrade work runs in this worktree (`C:\Users\Administrator\source\TSIC-Core-Angular-ng22`, branch `feature/angular-22`, its own `node_modules`); the `master` checkout (Angular 21 — the go-live line) and Ann's testing stay **untouched**. **CUTOVER to master is a separate, gated step** — nothing merges to master, pushes, or deploys without explicit approval, and it depends on the open go-live-timing decision (v21 shipped first then upgrade, vs. v22 in the August go-live) + full QA. See *Branch & merge workflow* below.

## Context

Angular **22.0** released 2026-06-03. The frontend (`src/frontend/tsic-app`) is on **21.0.6** and already exceptionally modern: 100% standalone, 229/230 OnPush, ~1,886 `signal()` / 931 `computed()`, 100% new control flow, `inject()` everywhere, Vitest. It is still **zone-based** and still uses decorator `@Input` (133) / `@Output` (69) / `@ViewChild` (54).

Goal: not just bump packages, but **adopt v22's reactive features**. Decisions reached with the user:

- **`httpResource` = the standard for reads** — paced, service-layer pattern; **writes/payments stay imperative `HttpClient`**.
- **Zoneless = adopt THIS cycle** (not deferred). Rationale: the only real risk is *silent stale UI*, which the **August 2026 go-live QA runway** converts from a production risk into a dev-time bug. Syncfusion EJ2 is zoneless-compatible since Essential Studio v31.x; we're on 33.1.44 (Syncfusion benchmarks: 12–22% smaller bundle, 15–28% faster render on Grid/Charts/Document Editor).
- Verified hard facts: Angular 22 requires **TypeScript 6.0.x** (from 5.9.3) and **Node ≥ 24.15.0** (this box is 24.13.0); `@ng-bootstrap` + `@popperjs/core` are **dead** (0 imports; Bootstrap is CSS-only) and ng-bootstrap peer-requires Angular 21, so it blocks `ng update`.

---

## Step 1 — CREATE THE WORKTREE FIRST (DONE 2026-06-07)

**The new git worktree is created before any other step, and 100% of the upgrade work happens inside it. The `master` checkout is NEVER edited in place.**

```
git worktree add ../TSIC-Core-Angular-ng22 -b feature/angular-22   # done
```

Open the sibling folder as its own VSCode window; every change, install, build, and tool call runs there.

## Pre-flight gates (checked next — inside the worktree, before the core bump)

1. **Syncfusion Angular 22 release** — confirm on the [version-compatibility page](https://ej2.syncfusion.com/angular/documentation/upgrade/version-compatibility); bump all `@syncfusion/ej2-angular-*` + `ej2-base` + theme from `33.1.44` → latest A22-listed release. Refresh `registerLicense(...)` in `src/main.ts` if the major changed. **Gates the core `ng update` step only** — not the worktree, not Phase 0, not the zoneless audit. *(As of 2026-06-07 the page still tops out at Angular 21; A22 not yet listed. Their A21 support shipped fast, so likely days away.)*
2. **Node ≥ 24.15.0** on this box AND TSIC-PHOENIX (currently 24.13.0).

---

## Branch & merge workflow (git worktree)

Different dependency tree (A21 vs A22 `node_modules`) ⇒ a **git worktree**, not in-place branch switching — two folders, each its own branch + `node_modules`, open as two VSCode windows.

- **master window** (`…\TSIC-Core-Angular`, A21) = the **go-live line**; Ann's fixes land + commit + push here.
- **ng22 window** (`…\TSIC-Core-Angular-ng22`, A22) = upgrade work; all tooling runs here.
- **Stay current (one-way, continuous):** when master moves, in the ng22 window run `git fetch && git merge origin/master`. The worktree stays a *superset* of master, so conflicts resolve in small, frequent bites. **Merge, never rebase** (don't rewrite a long-lived branch's SHAs).
- **Cutover (gated):** since the branch already contains every master commit, promoting it is a near-fast-forward, not a big merge. Requires explicit approval + full QA; timing per the open decision below.

> **Open decision — go-live timing:** does **v21** ship in August with the upgrade right after (cleanest — upgrade lands against a quiet post-launch master), or is **v22** the August go-live (parallel work, cutover before launch)? This sets *when* we cut over, not *how*.

---

## Phase 0 — Cleanup & pin (separate commits, pre-bump) — UNBLOCKED, doable now

- Remove `@ng-bootstrap/ng-bootstrap` + `@popperjs/core`; reinstall + build + Vitest to prove non-use.
- Add `changeDetection: ChangeDetectionStrategy.OnPush` to `PalettePickerComponent` (`src/app/layouts/components/palette-picker/palette-picker.component.ts`) so the OnPush-default migration doesn't stamp it `Eager`.
- Pin `paramsInheritanceStrategy: 'emptyOnly'` in the existing `withRouterConfig({...})` in `app.config.ts` (not auto-migrated; `jobPath` is safe either way but pin to preserve exact behavior).
- Drop dead `@types/jasmine` + `jasmine-core` (on Vitest).

### Phase 0 — STATUS (2026-06-07, in worktree, build-verified, NOT committed)

Worktree `npm install` done (A21 baseline). Applied:
- ✅ Removed `@ng-bootstrap/ng-bootstrap` + `@popperjs/core` from direct deps (0 `src` usages; `@popperjs/core` correctly remains as bootstrap@5.3.8's transitive dep; ng-bootstrap fully gone from lockfile). `ng build` (development) **green** — the real proof-of-non-use.
- ✅ `ChangeDetectionStrategy.OnPush` on `PalettePickerComponent` (no plain-field mutation; safe).
- ✅ Pinned `paramsInheritanceStrategy: 'emptyOnly'` in `withRouterConfig` (`app.config.ts`).
- ✅ Dropped `@types/jasmine` + `jasmine-core` (on Vitest).

> ⚠️ **Pre-existing frontend-test finding (NOT caused by this upgrade, scoped to a small suite).**
>
> **Two separate suites — don't conflate them:**
> - **Backend `dotnet test`** = 56 `*Tests.cs` files, ~5xx tests ("564/571/550 green" in memory). This is the suite normally watched. **Untouched, green, not in scope here.**
> - **Frontend `ng test`** (Vitest via `@angular/build:unit-test`) = **only 11 spec files / 171 tests.** Small; rarely run; can rot unnoticed.
>
> On the current committed frontend code the **frontend** suite failed to **compile**: 3 spec mock factories (`waiver-state` / `eligibility` / `payment-v2` `.service.spec.ts`) built `FamilyPlayerDto` without the **required** `hasAnyRegistration: boolean` (field added to the DTO, mocks never updated → TS2322, zero tests ran). Verified by running `ng test` on the *unmodified* files before any edit. Fixed the 3 mocks in the worktree (test-only, one line each). `hasAnyRegistration` is read only in `family-players.service.ts:325` (DTO mapping) — **not** in any payment/lineItems logic, so the mock value cannot move a payment number.
>
> After the fix the frontend suite compiles: **152 pass / 19 fail.** The 19 are **pre-existing drift** (test debt from prior feature work — stale tests, not the bump), DECISION 2026-06-07: **documented, not fixed** ("document them and move on"):
> - `payment-v2.service.spec.ts` — 16: stale fee expectations (e.g. `"default to 100 when team fee null/zero"` expects 100, gets 50 — asserts the **$100 fallback that was deliberately killed in `790a0ff7`**) + an outdated `jobCtx` double missing `bPlayersFullPaymentRequired`. Evidence points to **tests stale, production likely correct** — but NOT proven (would need a money-careful review).
> - `player.component.spec.ts` — 2: review-step navigation.
> - `team.service.spec.ts` — 1: BYCLUBNAME substring filter.
>
> **The 3-mock compile fix lives on this branch (`fdbaca46`)** and rides into master at the gated cutover — NOT separately ported to master (decision 2026-06-07; master worktree also had Ann's uncommitted work in flight, so leave it be).

## Phase 1 — The v22 bump, zone-based, to GREEN — BLOCKED on Syncfusion A22

- Bump Node (ops).
- `ng update @angular/core@22 @angular/cli@22 @angular/cdk@22` (pulls `@angular/build`, `compiler-cli`, etc.). Let migrations run; review the diff (auto-added `changeDetection`/`strictTemplates`/router params).
- **TypeScript → 6.0.x**: address `ignoreDeprecations: "5.0"` in `tsconfig.json:6`; keep `experimentalDecorators`; run `tsc -p tsconfig.app.json --noEmit` + `tsconfig.spec.json`; fix surfaced errors.
- **Syncfusion** → A22 release (pre-flight #1).
- **HttpClient = Fetch default**: do **nothing** — Fetch is the v22 default and `withFetch()` is deprecated/removed. No upload-progress code exists to break.
- Migrate the 4 `APP_INITIALIZER` providers → `provideAppInitializer` (deprecated token).
- **Verify:** `ng build` (development/staging/production) + Vitest green. **Milestone: v22 green, still zone-based.**

## Phase 2 — Zoneless flip (this cycle) — audit UNBLOCKED now; flip after Phase 1

- **EJ2 readiness audit** of the ~28 Syncfusion components: find handlers that mutate **plain fields** (not via a template `(event)` binding and not a signal) — those rely on zone today and go stale under zoneless. Convert the field to a `signal` (or `markForCheck`). Known starting points: `gridRef` (`uslax-membership.component.ts`), `pivotDataSource`/`selectedStartDate`/`selectedEndDate` (`customer-job-revenue.component.ts`), `lastColVis` (`registered-teams-grid.component.ts`).
- **Flip:** replace `provideZoneChangeDetection({eventCoalescing:true})` with `provideZonelessChangeDetection()` in `app.config.ts`; remove `"zone.js"` from `angular.json` polyfills; remove the `zone.js` dependency.
- **Syncfusion smoke matrix** (staging): every grid / chart / pivot / diagram / RTE screen — sort, filter, select, paginate, edit — watch for stale UI.

### Phase 2 — Zoneless EJ2 readiness audit RESULTS (2026-06-07, read-only)

33 EJ2 component files scanned. Findings are a **checklist to verify during the flip**, not yet remediated — each fix lands + gets smoke-tested in Phase 2 proper. Some "CRITICAL" sites may already be safe via `[(ngModel)]`/template-event bindings; confirm at the template before converting.

**Category A — likely stale-UI under zoneless (plain field mutated outside a template event binding):**

| # | File | Field | Line(s) | Context | Fix |
|---|------|-------|---------|---------|-----|
| 1 | `views/tools/customer-job-revenue/customer-job-revenue.component.ts` | `pivotDataSource` | 65 / mutated 163 | reassigned in `updatePivotDataSource()` off `loadData()` response | → `signal<IDataOptions>()`, `.set()` |
| 2 | `…/customer-job-revenue.component.ts` | `selectedStartDate` | 60 / 142 | assigned in `loadData` response (also ngModel-bound — verify) | → `signal<string>('')` |
| 3 | `…/customer-job-revenue.component.ts` | `selectedEndDate` | 61 / 143 | assigned in `loadData` response (also ngModel-bound — verify) | → `signal<string>('')` |
| 4 | `views/registration/team/components/registered-teams-grid.component.ts` | `lastColVis` | 286 / mutated 309 | mutated inside EJ2 `dataBound` handler `onDataBound()` | → `signal<{feeAdj:boolean}>()` |
| 5 | `views/tools/uslax-membership/uslax-membership.component.ts` | `gridRef` | 113 / assigned 232 | imperative grid ref set in selection handler (read-only use → MEDIUM) | → `viewChild<GridComponent>()` |

**Category B — reviewed, no plain-field mutation (no action expected, re-confirm during smoke):**
- `views/search/teams/search-teams.component.ts` — `actionComplete` → `refreshRowNumbers()` does DOM-only manipulation, no tracked state.
- `views/search/registrations/search-registrations.component.ts` — row-selection handler already updates **signals** (`.set()`) — the correct pattern.
- `views/reporting/library-editor/library-editor.component.ts` — `onActionBegin/Complete` mutate EJ2-managed `args.data`, not component fields.
- Remaining ~25 files: signal-safe, chart-only (no state-mutating handlers), or correct template bindings.

> No `ChangeDetectorRef` usage exists anywhere today — prefer signal conversion as the fix; `markForCheck()` only as fallback.

## Phase 3 — `httpResource` read migration (paced)

- **Pattern (service-layer):** read services expose `httpResource`-based methods that take the component's param signal(s) and return the resource; the component owns the filter signal and reads `.value()` / `.isLoading()` / `.error()`. *Injection-context gotcha:* create as a component field at construction (`x = this.svc.searchY(this.filter)`), not lazily.
- **Pilot first:** one filter/search screen (e.g. `search-registrations`) + one Syncfusion-grid-bound read screen — prove the pattern and grid binding **under zoneless**, then lock it.
- Roll out screen-by-screen. **Writes/commands stay imperative.** Use `chain()` for dependent reads, `debounced()` for search inputs.

## Phase 4 — Signal authoring migration (its own isolated diff)

- Run Angular's official signal schematics (`signal-input-migration`, `output-migration`, `signal-queries-migration` — confirm exact names via `ng g @angular/core:`). ~256 sites, mostly automated.
- Manual review: two-way bindings, required inputs, inputs read in `ngOnInit`/lifecycle hooks.

### Phase 4 — STATUS (2026-06-08 overnight, PARTIAL, committed + pushed)

All three schematics confirmed **runnable on Angular 21** and run via official tooling (conservative default mode — skips problematic patterns; never hand-migrated). Each ran full-repo, then **money/PII/payment/accounting/registration-entry components were reverted** and left for supervised review; the rest committed after `ng build` green + `ng test` **152/152 unchanged vs baseline** (the 19 pre-existing failures never moved).

- `ff6f2552` — APP_INITIALIZER → provideAppInitializer (4 providers).
- `d8fbd5b6` — @Input → `input()`, **31** components.
- `5f4de7f7` — @Output → `output()`, **27** components.
- `e1023a23` — @ViewChild/@ViewChildren → `viewChild()`, **21** components.

**DEFERRED for supervised migration (14 components — NOT migrated; still decorator-based).** These handle money/PII/payment/accounting/registration entry and (mostly) have no unit-test coverage, so "compiles" would be the only check — left for human review. Run the schematics per-file under supervision when ready:
`credit-card-form`, `bank-account-form`, `vi-charge-confirm-modal`, `player-form-modal`, `team-form-modal`, `add-and-register-team-modal`, `fee-card`, player `payment-step`, team `payment-step`, `family.component`, `clubrep-vi-update`, `player-vi-update`, `customer-job-revenue`, `last-months-job-stats`.

> Still TODO on the migrated files (per the manual-review note above): two-way bindings, required inputs, and inputs read in `ngOnInit`/lifecycle hooks — schematic handles most but a human pass is warranted before cutover. Build+tests give confidence the *types/templates* are consistent; runtime behavior of non-test-covered components still needs the August QA smoke pass.

## Going-forward conventions (no migration; new/changed code only)

- **Signal Forms** for new forms — **not** the 6 existing registration/payment forms (money paths).
- **`@Service`** for new services; **`@defer` + `injectAsync`/`onIdle`** for heavy Syncfusion widgets/services; **`never()`** exhaustive `@switch` on discriminated unions; router `isActive()` signal as screens are touched.

## Optional cleanup (user's call)

- Remove vestigial SSR scaffolding (`@angular/platform-server`, `express`, `@types/express`, `serve:ssr` script) unless SSR is on the roadmap.

---

## Critical files

- `src/app/app.config.ts` — zoneless provider, router pin, http (Fetch default), `provideAppInitializer`.
- `src/main.ts` — Syncfusion `registerLicense`, bootstrap.
- `angular.json` — remove `zone.js` from polyfills.
- `package.json` — Angular/CDK/CLI 22, TS 6, Syncfusion bump, remove ng-bootstrap/popper/zone.js/jasmine.
- `tsconfig.json` / `tsconfig.app.json` / `tsconfig.spec.json` — TS 6 reconciliation.
- `src/app/layouts/components/palette-picker/palette-picker.component.ts` — OnPush tag.
- The ~28 EJ2 components (zoneless audit) + read services/screens (`httpResource` rollout).

## Verification

- **Automated:** `ng build` (development/staging/production), full Vitest suite, `tsc --noEmit`.
- **Manual smoke (critical — especially zoneless × Syncfusion):** auth/jobPath routing; money paths (registration → payment CC/eCheck, invoices, fee display — verify numbers); the full Syncfusion grid/chart/pivot/diagram/RTE matrix (watch for stale UI under zoneless); anonymous public schedule/rosters.
- **Staging deploy** (`dev.teamsportsinfo.com`) for full QA across June–August before the August go-live.

## Sequencing rationale

Bump to green **zone-based first** (automated tests isolate bump/TS6/Syncfusion issues), then flip zoneless + EJ2 audit, then **one** comprehensive manual QA pass over the combined result. `httpResource` and the signal-authoring migration land as their own verifiable diffs on top.
