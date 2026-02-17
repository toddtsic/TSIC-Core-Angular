# Wizard Deep-Dive Audit Report — 2026-02-17

> Comprehensive audit of Player Registration Wizard, Team Registration Wizard, Shared Infrastructure, Design System, Accessibility, and Error Handling.

---

## Executive Summary

| Dimension | Grade | Key Finding |
|-----------|-------|-------------|
| **Architecture** | A | Excellent component decomposition; 100% OnPush; full signal adoption |
| **Design System** | A- | 95%+ CSS variable compliance; glassmorphic back-button has hardcoded RGBA |
| **Accessibility** | B- | Good semantic structure; CRITICAL gaps in `aria-describedby` + `aria-invalid` |
| **Error Handling** | B- | Most HTTP calls handled; 5 CRITICAL missing handlers/race conditions |
| **Code Quality** | B+ | Clean; subscription cleanup needed in 6 locations |
| **Mobile/Responsive** | A | 44px touch targets; responsive grids; mobile step indicator |

**Total findings: 14 CRITICAL, 18 IMPORTANT, 25+ MINOR**

---

## CRITICAL Findings (Must Fix)

### C1. Unmanaged Subscriptions (Memory Leaks)

| # | File | Line(s) | Issue |
|---|------|---------|-------|
| 1 | `registration-wizard.service.ts` | 220-230, 434-554, 996-1003 | HTTP `.subscribe()` without cleanup (3 locations) |
| 2 | `waivers.component.ts` | 135 | `form.valueChanges.subscribe()` — no `takeUntilDestroyed` |
| 3 | `credit-card-form.component.ts` | 149 | `form.valueChanges.subscribe()` — no `takeUntilDestroyed` |
| 4 | `family-account-wizard.component.ts` | 76-109 | `getMyFamily().subscribe()` — no error handler AND no cleanup |

**Fix pattern:**
```typescript
// For one-shot HTTP calls in services:
await firstValueFrom(this.http.get(...));

// For component subscriptions:
private destroyRef = inject(DestroyRef);
obs.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(...);
```

### C2. `alert()` Calls Instead of Accessible Toasts

| File | Line(s) |
|------|---------|
| `player-registration-wizard.component.ts` | 336, 345 |

Blocking `alert()` is inaccessible and breaks UX. Replace with `ToastService.show()`.

### C3. Payment Double-Click Race Condition

| File | Line(s) | Issue |
|------|---------|-------|
| `payment.component.ts` (player) | 318-341 | `submitting` flag set AFTER async validation; rapid clicks bypass guard |
| `payment.component.ts` (team) | 443-532 | 90-line method, same pattern |

**Fix:** Set `submitting = true` as FIRST line of `submit()`.

### C4. Insurance Widget Infinite Retry Loop

| File | Line(s) | Issue |
|------|---------|-------|
| `payment.component.ts` (player) | 257-261 | `tryInitVerticalInsure()` retries with `setTimeout(..., 150)` forever if offer never arrives |

**Fix:** Add max retry counter (5-10), emit error state on exhaustion.

### C5. Hardcoded RGBA in Glassmorphic Back Button

| File | Line(s) | Current | Should Be |
|------|---------|---------|-----------|
| `wizard-action-bar.component.scss` | 51, 54, 58, 59, 55, 60, 66 | `rgba(255,255,255,0.1)`, `rgba(0,0,0,0.1)` | `rgba(var(--bs-body-color-rgb), 0.08)`, `var(--shadow-xs)` |

These fail contrast in dark palettes — back button becomes invisible.

### C6. Missing `aria-describedby` on Form Inputs

**Scope:** ~50+ form inputs across ALL wizard forms have validation error messages NOT linked to their inputs.

| Wizard | Files Affected |
|--------|---------------|
| Player | credit-card-form, player-forms, family-check |
| Team | club-rep-login-step, team-registration-modal, team-edit-modal |
| Family | account-info, credentials |

**Pattern needed:**
```html
<input id="email" formControlName="email"
       [attr.aria-describedby]="form.get('email')?.errors ? 'email-error' : null"
       [attr.aria-invalid]="form.get('email')?.invalid && form.get('email')?.touched">
@if (err('email')) {
  <div id="email-error" class="form-text text-danger" role="alert">{{ err('email') }}</div>
}
```

### C7. Missing `aria-invalid` on Validated Inputs

Same scope as C6. No inputs use `[attr.aria-invalid]` binding. Screen readers cannot communicate validation state.

### C8. Missing `aria-modal="true"` on Dialog Element

| File | Issue |
|------|-------|
| `tsic-dialog.component.ts` | Native `<dialog>` missing `aria-modal="true"` |

### C9. Hardcoded Shadow Colors

| File | Line(s) | Count |
|------|---------|-------|
| `wizard-action-bar.component.scss` | 55, 60, 66, 76, 83, 89 | 6 instances |
| `add-club-form.component.scss` | 11 | 1 instance |

All use `rgba(0,0,0,...)` instead of `var(--shadow-*)` tokens.

---

## IMPORTANT Findings (Should Fix)

### I1. Duplicate Age Group Filtering Logic
- `teams-step.component.ts:378-410` and `team-registration-modal.component.ts:187-212` contain identical `getFilteredAgeGroups()` and `sortAgeGroups()` logic.
- **Fix:** Extract to shared utility.

### I2. Legacy `@Input/@Output` Still Used in Key Components
- `wizard-modal.component.ts` — still uses `@Input()`, `@Output()`, `@ViewChild()`, `ngOnChanges`
- `credit-card-form.component.ts` — dual outputs (`ccValidChange` AND `validChange`)
- `bottom-nav.component.ts` — legacy API
- **Fix:** Migrate to `input()`, `output()`, `viewChild()`.

### I3. Unused Imports
- `team-registration-wizard.component.ts:4-5` — `EventEmitter`, `Output` imported but unused
- `payment.component.ts:208` — unused `userChangedOption` property
- `credit-card-form.component.ts:164` — `noop()` method exists only for template form submit

### I4. `submitPayment()` Too Complex
- Team `payment.component.ts:443-532` — 90-line method handling insurance, idempotency, payment, and state
- **Fix:** Break into `purchaseInsurance()`, `processPayment()`, `handlePaymentResult()`.

### I5. Wizard Modal Focus Not Reliably Restored
- `wizard-modal.component.ts:130-134` — `setTimeout(..., 0)` doesn't guarantee timing after animation
- No fallback if focus restoration fails

### I6. Confirmation Polling Without OnDestroy Cleanup
- `confirmation.component.ts:44-58` — `setInterval` polling without storing reference; no `ngOnDestroy`
- If component destroys before timeout, leaked interval attempts signal access on destroyed component

### I7. `btn-close` Below 44px Touch Target
- Bootstrap `.btn-close` is ~16x16px in all modals and alert dismiss buttons
- 10+ instances across team and player wizards
- **Fix:** Add min-width/min-height via CSS or padding wrapper.

### I8. Auth Token Expiry Not Handled Mid-Wizard
- `registration-wizard.service.ts:215-230` — 401 response from `loadFamilyPlayers()` doesn't redirect to login
- User sees frozen spinner with no recovery path

### I9. RxJS Anti-Pattern in Team Discount Service
- `team-payment.service.ts:148-164` — subscribes internally AND returns the Observable (double subscription)
- **Fix:** Return Observable without internal `.subscribe()`.

### I10. Hardcoded Spacing in `_wizard.scss`
- 15+ locations use rem/px instead of `var(--space-N)` variables
- Lines: 31, 35, 39, 46, 49, 94, 120, 130, 131, 145, 181, 182, 189, 190, 196, 198

### I11. Family Account Wizard Uses Different Layout
- Does NOT use `WizardActionBarComponent` — has custom toolbar
- Inconsistent UX across the three wizards

### I12. Missing `<fieldset>/<legend>` for Radio Groups
- `team-registration-modal.component.html:13` — mode toggle button group
- `club-rep-login-step.component.html:30` — account availability radio group

---

## MINOR Findings (Nice to Have)

| # | Category | Description |
|---|----------|-------------|
| M1 | Dead code | `StartChoiceComponent` references in comments |
| M2 | Dead code | `noop()` method in credit-card-form |
| M3 | Pattern | Polling with `setInterval` instead of signal-based approach (confirmation) |
| M4 | Pattern | Template-driven forms in family-check, player-selection (inconsistent with reactive) |
| M5 | Pattern | Magic numbers: `setTimeout(..., 3000)`, `setTimeout(..., 1500)` |
| M6 | Type safety | `any` parameter in `team-insurance.service.ts:40` (`offerData: any`) |
| M7 | Type safety | `TeamPaymentRequestDto/ResponseDto` defined locally instead of generated |
| M8 | Styling | `var(--bs-gray-300)` in step-indicator could be `var(--bs-secondary-bg)` |
| M9 | Styling | Duplicate wizard-theme-player/team selectors with identical mixin include |
| M10 | Styling | Progress bar `10px` height not on 8px grid |
| M11 | A11y | No `<caption>` on review tables |
| M12 | A11y | `autofocus` on secondary action buttons in modals |
| M13 | A11y | `wizard-action-bar` disabled button missing `aria-disabled` |
| M14 | A11y | Action bar animations missing `prefers-reduced-motion` |
| M15 | Performance | Double `simpleHydrateFromCc()` call in payment `AfterViewInit` |

---

## Recommended Fix Order

### Phase 5A: Critical Safety (Estimated: 2-3 hours)
1. **C3** — Payment double-click prevention (set `submitting` first)
2. **C4** — Insurance retry limit
3. **C1** — Add `takeUntilDestroyed` / `firstValueFrom` to 6 subscriptions
4. **C2** — Replace `alert()` with toast service
5. **I8** — Handle 401 mid-wizard (redirect to login)

### Phase 5B: Accessibility Critical (Estimated: 3-4 hours)
1. **C6** — Add `aria-describedby` linking errors to inputs (~50 fields)
2. **C7** — Add `aria-invalid` binding to all validated inputs
3. **C8** — Add `aria-modal="true"` to `<dialog>`
4. **I7** — Expand `.btn-close` touch targets

### Phase 5C: Design System Polish (Estimated: 1-2 hours)
1. **C5** — Fix glassmorphic back-button RGBA → CSS variables
2. **C9** — Replace hardcoded shadow values with tokens
3. **I10** — Convert spacing to `--space-N` variables in `_wizard.scss`

### Phase 5D: Code Quality (Estimated: 2-3 hours)
1. **I1** — Extract duplicate age group logic
2. **I3** — Remove unused imports/properties
3. **I4** — Break apart `submitPayment()` method
4. **I9** — Fix RxJS anti-pattern in discount service
5. **I2** — Signal migration for wizard-modal (can defer)

---

## Strengths Worth Preserving

- **100% OnPush change detection** across all components
- **Modern Angular 21 syntax** — `@if/@for/@switch`, `signal()`, `computed()`, standalone components
- **No BehaviorSubjects** — all state is signal-based
- **Strong service layer** — proper separation of concerns
- **Excellent print stylesheet** — wizard chrome hidden, content flows
- **44px touch targets** on main action buttons
- **8 color palette support** — nearly all CSS uses variables
- **`prefers-reduced-motion`** — global animation suppression
- **Idempotency key** for payment deduplication

---

## Architecture Scores

| Component | Architecture | Code Quality | Standards Compliance |
|-----------|-------------|-------------|---------------------|
| Player Wizard | 8.5/10 | 7.5/10 | 8/10 |
| Team Wizard | 8/10 | 7.5/10 | 8/10 |
| Shared Infrastructure | 8/10 | 8/10 | 7.5/10 |
| Design System | — | — | 9/10 |
| Accessibility | — | — | 6.5/10 |
| Error Handling | — | 6.5/10 | — |

**Overall: B+ (Good) — A- after fixing CRITICALs**

---

## Deferred Items (Future Sessions)

### I8: Handle 401 Token Expiry Mid-Wizard
- **Scope:** HTTP interceptor + wizard state serialization + login redirect + state restoration
- **Why deferred:** Architectural feature requiring cross-cutting changes (interceptor, sessionStorage persistence, return-URL routing)
- **Impact:** Currently a frozen spinner if token expires mid-wizard — no recovery path

### I11: Align Family Account Wizard to WizardActionBarComponent
- **Scope:** Family wizard uses custom toolbar + child `(next)`/`(back)` events; player/team wizards use `WizardActionBarComponent`
- **Why deferred:** Significant structural refactor with regression risk; family wizard works correctly as-is
- **Impact:** UX inconsistency — different navigation patterns across 3 wizards

### I4: Break Apart `submitPayment()` in Team Payment
- **Scope:** 90-line method handling insurance, idempotency, payment, and state
- **Why deferred:** Functional correctness is fine; refactoring is code quality improvement only

### I6: Confirmation Polling `setInterval` Without Cleanup
- **Scope:** `confirmation.component.ts:44-58` — `setInterval` without `ngOnDestroy` cleanup
- **Impact:** Potential leaked interval if component destroys before timeout

### Minor Items (M1-M15)
- See MINOR Findings table above — all are nice-to-have improvements
- M14 (`prefers-reduced-motion` on action bar animations) is the most impactful minor item
