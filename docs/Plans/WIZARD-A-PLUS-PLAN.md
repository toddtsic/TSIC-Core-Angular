# Registration Wizard A+ Plan

> **Goal**: Bring player and team registration wizards to A+ across all UI/UX categories while
> building reusable shared infrastructure for future role-specific wizards (unassigned adult,
> referee, store admin, etc.).
>
> **Key constraint**: Player and team payment/insurance have intentional business-logic differences.
> We extract only truly identical utility code — never merge business logic.
>
> **Last updated**: 2026-02-17

---

## Current State Summary

| Category | Player | Team | Cross-Wizard |
|----------|:------:|:----:|:------------:|
| Template Quality | B+ | B+ | B |
| Accessibility (WCAG) | C | C- | C- |
| Responsive/Mobile | B- | C+ | C |
| Design System Compliance | B- | B | B- |
| Error/Loading UX | B- | B+ | C |
| Form Validation UX | B | B- | C+ |
| Modal Management | D | C+ | C |

### Key Existing Assets
- `TsicDialogComponent` — gold-standard modal using native `<dialog>`, focus trap, ESC, backdrop click. **Currently only used in admin views — not in any wizard.**
- `FocusTrapDirective` (`[tsicFocusTrap]`) — manual Tab/Shift+Tab trapping. Used by `TsicDialogComponent`.
- `AutofocusDirective` (`[appAutofocus]`) — focuses element on init.
- `@angular/cdk` v21.0.5 installed but unused.
- `WizardActionBarComponent` — shared action bar (glassmorphic, responsive, signal-based). Used by both wizards.
- `StepIndicatorComponent` — shared step indicator in `shared-ui/`. Used by both wizards.
- `vi-dark-mode.service.ts` — shared VerticalInsure dark-mode service.
- `credit-card-utils.ts` — shared sanitizer functions (player only, should expand).
- `IdempotencyService` — misplaced in player folder, cross-imported by team.

### Orphaned/Dead Code
1. `shared/tw-action-bar.component.html` + `.scss` — no `.ts` file, never imported
2. `team-registration-wizard/step-indicator/tw-step-indicator.component.*` — superseded by shared `StepIndicatorComponent`
3. `player-registration-wizard/action-bar/rw-action-bar.component.*` — superseded by `WizardActionBarComponent`
4. `src/styles/_action-bar.scss` — 214 lines, only consumed by dead `rw-action-bar`
5. Duplicate `wizard-layout.scss` in `src/styles/` and `src/app/shared/styles/` (diverged)

---

## Phase 0: Cleanup & Consolidation (Foundation)

**Purpose**: Remove dead code, fix misplacements, consolidate duplicates. Zero functional changes — pure hygiene.

### 0.1 Delete orphaned components
- [ ] Delete `shared/tw-action-bar.component.html` and `.scss`
- [ ] Delete `team-registration-wizard/step-indicator/tw-step-indicator.component.ts`, `.html`, `.scss`
- [ ] Delete `player-registration-wizard/action-bar/rw-action-bar.component.ts`, `.html`, `.scss`
- [ ] Delete or gut `src/styles/_action-bar.scss` (only used by dead `rw-action-bar`)
- [ ] Remove any imports of these components if they exist anywhere
- [ ] Build verification

### 0.2 Move misplaced shared services
- [ ] Move `player-registration-wizard/services/idempotency.service.ts` → `shared/services/idempotency.service.ts`
- [ ] Update imports in player payment component (`./services/idempotency.service` → `../../shared/services/idempotency.service`)
- [ ] Update imports in team payment component (currently cross-imports from `../../player-registration-wizard/services/idempotency.service`)
- [ ] Build verification

### 0.3 Consolidate duplicate wizard-layout.scss
- [ ] Audit both `wizard-layout.scss` files for differences
- [ ] Keep one canonical version in `src/app/shared/styles/wizard-layout.scss`
- [ ] Update any `@import` / `@use` references
- [ ] Delete the `src/styles/wizard-layout.scss` duplicate
- [ ] Build verification

### 0.4 Expand credit-card-utils to team wizard
- [ ] Audit team payment component for expiry/phone sanitization
- [ ] If same logic exists inline, replace with imports from `shared/services/credit-card-utils.ts`
- [ ] Build verification

**Estimated scope**: ~10 file deletions, ~6 file edits, zero functional changes.

---

## Phase 1: Shared Modal Infrastructure (Accessibility — WCAG Level A)

**Purpose**: Build a reusable wizard modal that all 8+ current modals can migrate to, and that future wizards get for free.

### Current modal audit (8 modals, all failing WCAG)
| Modal | Focus trap | ESC | aria-modal | aria-labelledby | Returns focus |
|-------|:---------:|:---:|:----------:|:---------------:|:------------:|
| ViChargeConfirmModal (player) | No | No | Yes | Yes | No |
| ViConfirmModal (player) | No | No | Yes | No | No |
| TeamRegistrationModal | No | No | No | No | No |
| TeamEditModal | No | No | No | No | No |
| Club Selection Modal (team) | No | No | No | No | No |
| Club Rep Login Modal (team) | No | No | Yes | No | Partial |
| Duplicate Club Modal (team) | No | No | Yes | No | No |
| Conflict Warning Modal (team) | No | No | Yes | Yes | No |

### 1.1 Create `WizardModalComponent` (shared)
**Location**: `wizards/shared/wizard-modal/wizard-modal.component.ts`

**Strategy**: Wrap `TsicDialogComponent` (which already has native `<dialog>`, focus trap, ESC, backdrop click) with wizard-specific styling.

```
WizardModalComponent
├── Uses TsicDialogComponent internally (native <dialog>)
├── Applies wizard-modal visual styling (glassmorphic backdrop, card-rounded content)
├── Signal inputs:
│   ├── isOpen: InputSignal<boolean>          — controls show/hide
│   ├── title: InputSignal<string>            — modal title (drives aria-labelledby)
│   ├── size: InputSignal<'sm'|'md'|'lg'|'xl'> — maps to max-width
│   ├── closeOnEsc: InputSignal<boolean>      — default true
│   ├── closeOnBackdrop: InputSignal<boolean> — default true
│   ├── showCloseButton: InputSignal<boolean> — default true
│   └── headerClass: InputSignal<string>      — optional custom header styling
├── Signal outputs:
│   └── closed: OutputEmitterRef<void>        — emitted on any close
├── Content projection slots:
│   ├── <ng-content select="[modal-body]">
│   └── <ng-content select="[modal-footer]">
├── Built-in features (from TsicDialogComponent):
│   ├── Focus trap (FocusTrapDirective)
│   ├── ESC key handling
│   ├── Backdrop click handling
│   ├── aria-modal="true" (native <dialog>)
│   └── aria-labelledby (auto-wired to title)
└── Additional features:
    ├── Returns focus to trigger element on close
    ├── prefers-reduced-motion: skip open/close animation
    └── Responsive: full-screen on mobile for 'lg'/'xl' sizes
```

### 1.2 Migrate all 8 modals to `WizardModalComponent`

Each migration replaces the manual `<div class="modal">` + backdrop with:
```html
<app-wizard-modal [isOpen]="showModal()" [title]="'Modal Title'" size="lg" (closed)="onClose()">
  <div modal-body>
    <!-- existing modal body content -->
  </div>
  <div modal-footer>
    <!-- existing footer buttons -->
  </div>
</app-wizard-modal>
```

Migration order (easiest → hardest):
1. [ ] Conflict Warning Modal (team) — simplest, already has best ARIA
2. [ ] Duplicate Club Modal (team) — simple confirmation
3. [ ] ViChargeConfirmModal (player) — already has good ARIA
4. [ ] ViConfirmModal (player) — simple confirm/deny
5. [ ] TeamEditModal (team) — medium complexity form
6. [ ] TeamRegistrationModal (team) — complex form with mode toggle
7. [ ] Club Rep Login Modal (team) — multi-step form
8. [ ] Club Selection Modal (team root) — largest, most complex

- [ ] Build verification after each migration
- [ ] Delete orphaned modal backdrop CSS after all migrations

### 1.3 Add `aria-required` to all required form inputs
- [ ] Player wizard: family-check, player-forms, credit-card-form
- [ ] Team wizard: club-rep-login-step, team-registration-modal, team-edit-modal
- [ ] Add `aria-describedby` linking error messages to their inputs
- [ ] Add `aria-describedby` linking help text to their inputs

### 1.4 Fix color-only feedback
- [ ] Add icons alongside color for validation states (success checkmark, error X)
- [ ] Ensure disabled options explain WHY they're disabled (e.g., "(Full - 15/15 teams)")

**Estimated scope**: 1 new component, 8 modal migrations, ~20 form input a11y fixes.

**Result**: All modals → WCAG Level A compliant. Future wizards get accessible modals for free.

---

## Phase 2: Mobile & Responsive (Responsive → A+)

### 2.1 Mobile step indicator
**Location**: Add to `StepIndicatorComponent` (shared-ui)

Currently hidden on mobile (`d-none d-md-block`). Add a compact mobile variant:

```
Desktop (≥768px): Full step indicator with circles + labels + connectors
Mobile (<768px):  Compact bar: "Step 3 of 7 — Player Forms" + thin progress bar
```

- [ ] Add mobile variant to `StepIndicatorComponent` (show on `d-md-none`)
- [ ] Remove `d-none d-md-block` wrapper from both wizard shells — let the component handle its own responsive behavior internally
- [ ] Build verification

### 2.2 Fix touch targets
- [ ] Audit all interactive elements for 44px minimum touch target
- [ ] Team wizard `teams-step.component.scss`: Fix `.btn-primary` mobile override (padding too small)
- [ ] Team `team-registration-modal.component.scss`: Fix `.modal-footer .btn` font-size-xs override
- [ ] Player wizard: Fix icon-only buttons (add padding to reach 44px)
- [ ] Export button in teams-step: increase tap target or add text label on mobile

### 2.3 Fix modal responsiveness
- [ ] Player forms modal: Replace `max-width: 720px` with `max-width: min(720px, 95vw)`
- [ ] Team registration modal: Fix `margin: 0.5rem` on mobile (keyboard overlap issue)
- [ ] WizardModalComponent (Phase 1): Built-in responsive sizing for all future modals

### 2.4 Fix hardcoded popup heights
- [ ] Team selection: Replace `[popupHeight]="'320px'"` with viewport-aware value
- [ ] Player selection debug panel: Replace `max-height: 240px` inline style with CSS class

### 2.5 Responsive table improvements
- [ ] Payment summary tables: Add sticky first column on mobile
- [ ] Team grid: Verify Syncfusion grid responsive behavior, add column hiding on mobile

**Estimated scope**: ~15 file edits across both wizards + step indicator enhancement.

---

## Phase 3: Design System Compliance (CSS → A+)

### 3.1 Fix hardcoded colors
- [ ] `team-selection.component.ts:40`: Replace `color: #555 !important` → `var(--bs-secondary-color)`
- [ ] Full grep for remaining hex codes in wizard files; fix any found

### 3.2 Fix hardcoded values → CSS variables
- [ ] Replace inline `z-index: 1060` with `var(--wizard-modal-z-index)` (already defined but unused)
- [ ] Replace inline `box-shadow: 0 0.25rem 0.5rem rgba(0,0,0,.1)` → Bootstrap shadow utility or CSS var
- [ ] Replace inline `max-height: 320px`, `max-height: 240px`, `max-height: 500px` → CSS classes with variables
- [ ] Replace inline `border-left: 3px solid` → CSS class using `var(--space-1)`

### 3.3 Fix broken animation
- [ ] `family-check.component.ts:46`: Either add `@keyframes slideIn` or replace with Bootstrap `fade` utility
- [ ] Add `@media (prefers-reduced-motion: reduce)` to global wizard styles

### 3.4 Reduce `::ng-deep` usage
- [ ] Scope all `::ng-deep` with `:host ::ng-deep` to prevent style leaking
- [ ] Investigate Syncfusion `cssClass` input for grid theming (may eliminate some `::ng-deep`)
- [ ] Document which `::ng-deep` are truly unavoidable (Syncfusion limitation)

### 3.5 Reduce `!important` overrides
- [ ] Audit all `!important` in wizard SCSS files
- [ ] Fix specificity issues where possible to remove unnecessary `!important`
- [ ] Document which are necessary (Syncfusion overrides)

### 3.6 Extract inline styles to component SCSS
- [ ] Player wizard: ~15 inline `style=""` attributes to migrate
- [ ] Team wizard: ~8 inline `style=""` attributes to migrate
- [ ] Use CSS classes or Angular `[class]` bindings instead

**Estimated scope**: ~30 inline style fixes, ~5 SCSS file edits.

---

## Phase 4: Error/Loading/Validation UX Coherence (UX → A+)

### 4.1 Shared loading-state component
**Location**: `wizards/shared/wizard-loading/wizard-loading.component.ts`

```
WizardLoadingComponent
├── Inputs: message (string), size ('sm'|'md'|'lg'), showRetry (bool)
├── Outputs: retry (void)
├── Template: spinner + message + optional retry button
├── States: loading | error (with message + retry) | empty (with message + CTA)
└── Used by: both wizard shells + confirmation/review steps
```

- [ ] Create component
- [ ] Replace team wizard's 3 different loading patterns with it
- [ ] Replace player wizard's minimal loading text with it
- [ ] Build verification

### 4.2 Standardize error display pattern
**Decision**: Inline alerts (team wizard pattern) for all wizard errors. Toasts only for transient success messages.

- [ ] Player payment: Replace toast-based critical errors with inline `alert-danger`
- [ ] Both wizards: Ensure all error alerts have:
  - Icon (bi-exclamation-triangle)
  - Clear message text
  - Dismiss button (alert-dismissible) for non-blocking errors
  - Retry button for recoverable errors
  - `role="alert"` for screen readers

### 4.3 Standardize form validation UX

Create shared validation rules:
- **When to show errors**: On blur (field touched + invalid), AND on submit attempt
- **Required field indicator**: Red asterisk `*` with `aria-required="true"` on input
- **Error message format**: Below input, in `invalid-feedback d-block`, with `aria-describedby` link
- **Success indicator**: Checkmark icon (not just color)

- [ ] Player wizard: Add required field asterisks (currently missing)
- [ ] Team wizard: Add `aria-required="true"` to inputs with asterisks
- [ ] Both: Ensure consistent touched/submitted validation timing
- [ ] Password fields: Add show/hide toggle in both wizards

### 4.4 Align confirmation experience
- [ ] Player confirmation: Add loading/error/retry state machine (matching team wizard's `review-step`)
- [ ] Both: Add print stylesheet
  ```css
  @media print {
    .wizard-action-bar-container,
    .wizard-fixed-header { display: none !important; }
    .confirmation-content { max-height: none; overflow: visible; }
  }
  ```

### 4.5 Standardize empty states
- [ ] Create consistent empty-state pattern: icon + message + CTA button
- [ ] Player wizard: Add explicit empty states where missing
- [ ] Team wizard: Differentiate "no data" vs "error" empty states

**Estimated scope**: 1 new component, ~20 file edits.

---

## Phase 5: Cross-Wizard Visual Parity (Coherence → A+)

### 5.1 Unify card styling
- [ ] Player wizard: Wrap step content in card matching team wizard's `.card.shadow-lg.border-0.card-rounded`
- [ ] OR: Extract card wrapper into shared CSS and apply to both
- [ ] Ensure both use `bg-surface` card body class

### 5.2 Create shared wizard shell pattern
**For future wizard reuse**, extract the common wizard shell into a documented pattern:

```
SharedWizardShell (documented pattern, not necessarily a component)
├── <main class="container-fluid wizard-container">
│   ├── <div class="wizard-fixed-header">
│   │   ├── <h2> Title (uses wizard-title mixin)
│   │   ├── <app-step-indicator> (shared, auto-responsive)
│   │   └── <app-wizard-action-bar> (shared, glassmorphic)
│   └── <div class="wizard-scrollable-content">
│       └── <div class="card shadow-lg border-0 card-rounded wizard-theme-{role}">
│           ├── <div class="card-header"> (optional banners/alerts)
│           └── <div class="card-body bg-surface">
│               └── @switch(currentStep()) { step components }
└── Shared services:
    ├── IdempotencyService
    ├── ViDarkModeService (if insurance)
    └── credit-card-utils (if payment)
```

- [ ] Document this pattern in `docs/Frontend/wizard-shell-pattern.md`
- [ ] Refactor player wizard shell to match pattern exactly
- [ ] Verify team wizard shell matches pattern
- [ ] Add `wizard-theme-{role}` SCSS mixins for future role wizards

### 5.3 Standardize button patterns
- [ ] All primary actions: `btn btn-primary` (gradient via action bar for navigation)
- [ ] All secondary actions: `btn btn-outline-secondary`
- [ ] All danger actions: `btn btn-danger` (cancellations, deletions)
- [ ] Loading state: spinner + "...ing" text change (e.g., "Submitting...")
- [ ] Disabled state: `[disabled]` + tooltip explaining why

### 5.4 Print styles
- [ ] Add `@media print` rules to `_wizard-globals.scss`
- [ ] Hide: action bar, step indicator, all buttons
- [ ] Show: full confirmation content without max-height truncation
- [ ] Add `page-break-inside: avoid` to card sections

**Estimated scope**: ~10 file edits + 1 documentation file.

---

## Phase 6: Future-Proofing (Reusability for New Wizards)

### 6.1 Wizard toolkit documentation
Create `docs/Frontend/wizard-development-guide.md`:

- [ ] Step-by-step guide: "How to create a new role-specific registration wizard"
- [ ] Shared component inventory with usage examples
- [ ] Required vs optional steps (which steps every wizard needs)
- [ ] Payment/insurance integration guide (when to use shared utils vs custom logic)
- [ ] Accessibility checklist for wizard development

### 6.2 Shared service inventory
After all phases, the `wizards/shared/` folder should contain:

```
wizards/shared/
├── services/
│   ├── credit-card-utils.ts          ← Pure functions: sanitizeExpiry, sanitizePhone
│   ├── idempotency.service.ts        ← localStorage idempotency keys
│   └── vi-dark-mode.service.ts       ← VerticalInsure dark-mode DOM manipulation
├── wizard-action-bar/                ← Navigation bar (Back / Continue)
│   ├── wizard-action-bar.component.ts
│   ├── wizard-action-bar.component.html
│   └── wizard-action-bar.component.scss
├── wizard-modal/                     ← Accessible modal (NEW — Phase 1)
│   ├── wizard-modal.component.ts
│   ├── wizard-modal.component.html
│   └── wizard-modal.component.scss
└── wizard-loading/                   ← Loading/error/empty states (NEW — Phase 4)
    ├── wizard-loading.component.ts
    ├── wizard-loading.component.html
    └── wizard-loading.component.scss
```

Plus shared-ui components used by wizards:
```
shared-ui/components/
├── step-indicator/                   ← Step progress (with mobile variant — Phase 2)
├── tsic-dialog/                      ← Native <dialog> base (existing)
└── confirm-dialog/                   ← Confirmation wrapper (existing)

shared-ui/directives/
├── focus-trap.directive.ts           ← [tsicFocusTrap] (existing)
└── autofocus.directive.ts            ← [appAutofocus] (existing)
```

### 6.3 Shared wizard SCSS
```
src/styles/
├── _wizard-globals.scss              ← Card themes, title mixins (existing, cleaned up)
└── wizard-layout.scss                ← ONE canonical version (consolidated in Phase 0)
```

---

## Implementation Order

```
Phase 0: Cleanup          ──── 1-2 hours  (zero risk, pure deletion/moves)
Phase 1: Modal infra      ──── 4-6 hours  (highest impact: WCAG compliance)
Phase 2: Mobile/Responsive ─── 2-3 hours  (high impact: mobile users)
Phase 3: Design system    ──── 2-3 hours  (medium impact: palette/dark-mode correctness)
Phase 4: Error/Loading UX ──── 3-4 hours  (medium impact: user experience polish)
Phase 5: Visual parity    ──── 2-3 hours  (coherence + future wizard template)
Phase 6: Documentation    ──── 1-2 hours  (future-proofing)
```

**Total**: ~15-23 hours of focused work across 6 phases.

Each phase is independently shippable — the wizards get better after every phase, and no phase depends on a later phase.

---

## Success Criteria

After all phases:

| Category | Target | How Measured |
|----------|:------:|-------------|
| Template Quality | A+ | Clean component boundaries, no inline styles, semantic HTML |
| Accessibility (WCAG) | A+ | All modals: focus trap + ESC + aria-modal + aria-labelledby. All forms: aria-required + aria-describedby. All feedback: not color-only |
| Responsive/Mobile | A+ | Mobile step indicator visible. 44px touch targets. No overflow. Modals responsive |
| Design System Compliance | A+ | Zero hardcoded colors. Zero hardcoded z-index/shadow. All spacing via CSS vars. prefers-reduced-motion respected |
| Error/Loading UX | A+ | Consistent inline alerts. Loading states with spinner + message + retry. Print styles |
| Form Validation UX | A+ | Required indicators + aria. Error on blur + submit. Success icons. Password toggle |
| Modal Management | A+ | All modals use WizardModalComponent (native dialog, focus trap, ESC, backdrop, focus return) |
| Cross-Wizard Coherence | A+ | Identical card styling, error patterns, loading states, button styles, empty states |
| Reusability | A+ | New wizard = shell + steps. All infrastructure shared. Guide documented |
