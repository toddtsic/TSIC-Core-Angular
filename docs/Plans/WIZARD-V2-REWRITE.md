# Registration Wizards v2 — Complete Rewrite Plan

**Keyword**: `CWCC Implement WIZARD-V2-REWRITE`
**Status**: COMPLETE (2026-02-18)
**Decision**: Abandon incremental refactoring. Full rewrite using composition pattern, side-by-side deployment.
**Phases**: Shell → Family → Player → Team → Route Swap

## Completion Summary

All 5 phases complete. v2 wizards are now the primary routes.

| Phase | Status | Details |
|---|---|---|
| 1. Wizard Shell + Types | Done | `WizardShellComponent` composition shell + `WizardStepDef`/`WizardShellConfig` types |
| 2. Family Wizard | Done | 5-step wizard, `FamilyStateService`, full signal encapsulation |
| 3. Player Wizard | Done | 9-step wizard, 5 decomposed services (was 1,314-line monolith), bridge to old TeamService |
| 4. Team Wizard | Done | 4-step wizard, `ClubRepStateService`, reuses existing team services |
| 5. Route Swap + Cleanup | Done | Routes point to v2, 76 old files deleted, 12 reusable services kept |

**Quality grade**: A (was C- before rewrite)
- Zero `any` types, zero public writable signals, all subscriptions managed
- ~2,900 lines of v2 replacing ~5,000+ lines of monolith

---

## Context

The existing wizard codebase (Player, Team, Family) totals ~5,000+ lines with a C- composite grade:
- Player wizard service: 1,314-line monolith managing 25+ signals across 8 domains
- 60+ explicit `any` usages, 20+ public writable signals, 12+ unmanaged subscriptions
- 11+ swallowed errors, 4 duplicate code patterns

A clean rewrite lets us apply gold-standard patterns from day one instead of fighting spaghetti.

---

## Shared Infrastructure — KEEP AS-IS (do NOT rewrite)

These are already good quality and will be imported by the new wizards:

| Component/Service | Location | Lines |
|---|---|---|
| `WizardActionBarComponent` | `@views/registration/wizards/shared/wizard-action-bar/` | 57 |
| `WizardModalComponent` | `@views/registration/wizards/shared/wizard-modal/` | 139 |
| `WizardLoadingComponent` | `@views/registration/wizards/shared/wizard-loading/` | 62 |
| `StepIndicatorComponent` | `@shared-ui/components/step-indicator/` | 75 |
| `wizard.types.ts` | `@views/registration/wizards/shared/types/` | 163 |
| `vi-dark-mode.service.ts` | `@views/registration/wizards/shared/services/` | 136 |
| `idempotency.service.ts` | `@views/registration/wizards/shared/services/` | 32 |
| `credit-card-utils.ts` | `@views/registration/wizards/shared/services/` | 19 |
| `property-utils.ts` | `@views/registration/wizards/shared/utils/` | 61 |
| `error-utils.ts` | `@views/registration/wizards/shared/utils/` | 22 |
| `color-class.util.ts` | `@views/registration/wizards/shared/utils/` | 24 |
| Wizard SCSS (`_wizard.scss`, `_wizard-globals.scss`, `_wizard-themes.scss`) | `src/styles/` + `@shared-ui/styles/` | 563 |

---

## Signal Pattern Contract (ALL new services)

Every state service follows `InsuranceStateService` gold standard:

```typescript
private readonly _foo = signal<T>(initial);  // PRIVATE backing
readonly foo = this._foo.asReadonly();        // PUBLIC readonly
setFoo(value: T): void { this._foo.set(value); }  // CONTROLLED mutator
```

No public writable signals. No `signal.update()` from outside the owning service.

---

## Directory Structure

```
src/app/views/registration/wizards-v2/
├── shared/
│   ├── wizard-shell/
│   │   ├── wizard-shell.component.ts        ← composition shell
│   │   ├── wizard-shell.component.html
│   │   └── wizard-shell.component.scss
│   └── types/
│       └── wizard-shell.types.ts            ← WizardStepDef, WizardShellConfig
├── family/
│   ├── family-wizard.component.ts
│   ├── family-wizard.component.html
│   ├── family-wizard.component.scss
│   ├── state/
│   │   └── family-state.service.ts
│   └── steps/
│       ├── credentials-step.component.ts
│       ├── contacts-step.component.ts
│       ├── address-step.component.ts
│       ├── children-step.component.ts
│       └── review-step.component.ts
├── player/
│   ├── player-wizard.component.ts
│   ├── player-wizard.component.html
│   ├── player-wizard.component.scss
│   ├── state/
│   │   ├── player-wizard-state.service.ts   ← thin orchestrator (~100 lines)
│   │   ├── job-context.service.ts           ← job identity + metadata (~150 lines)
│   │   ├── family-players.service.ts        ← family user + players (~200 lines)
│   │   ├── eligibility.service.ts           ← constraints + per-player (~120 lines)
│   │   └── player-forms.service.ts          ← per-player form values (~250 lines)
│   └── steps/
│       ├── family-check-step.component.ts
│       ├── player-selection-step.component.ts
│       ├── eligibility-step.component.ts
│       ├── team-selection-step.component.ts
│       ├── player-forms-step.component.ts
│       ├── waivers-step.component.ts
│       ├── review-step.component.ts
│       ├── payment-step.component.ts
│       └── confirmation-step.component.ts
└── team/
    ├── team-wizard.component.ts
    ├── team-wizard.component.html
    ├── team-wizard.component.scss
    ├── state/
    │   ├── team-wizard-state.service.ts     ← thin orchestrator
    │   └── club-rep-state.service.ts        ← clubs + rep session
    └── steps/
        ├── login-step.component.ts
        ├── teams-step.component.ts
        ├── payment-step.component.ts
        └── review-step.component.ts
```

---

## Phase 1: Wizard Shell + Shared Types

### 1A. `wizard-shell.types.ts`

```typescript
export interface WizardStepDef {
  id: string;
  label: string;
  enabled: boolean;  // false = skip (conditional step)
}

export interface WizardShellConfig {
  title: string;
  theme: 'player' | 'team' | 'family';
  badge?: string | null;  // e.g. family last name
}
```

### 1B. `WizardShellComponent` (composition, NOT base class)

**Inputs** (all signal-based):
- `steps: WizardStepDef[]` — full list (shell filters to enabled)
- `currentIndex: number` — 0-based into active steps
- `config: WizardShellConfig` — title, theme, badge
- `canContinue: boolean`
- `continueLabel: string` (default: `'Continue'`)
- `showContinue: boolean` (default: `true`)
- `detailsBadgeLabel: string | null` (default: `null`)
- `detailsBadgeClass: string` (default: `'badge-danger'`)

**Outputs**: `back`, `continue`

**Computed**:
- `activeSteps` — `steps().filter(s => s.enabled)`
- `stepDefinitions` — mapped to `StepDefinition[]` for `StepIndicatorComponent`
- `progressPercent` — `(currentIndex + 1) / activeSteps.length * 100`

**Template structure**:
```html
<main class="container-fluid px-3 py-2 rw-wizard-container"
      [class]="'wizard-theme-' + config().theme">
  <div class="wizard-fixed-header">
    <!-- Title + optional badge (responsive) -->
    <app-step-indicator [steps]="stepDefinitions()" [currentIndex]="currentIndex()" />
    <div class="wizard-action-bar-container">
      <app-wizard-action-bar
        [canBack]="currentIndex() > 0"
        [canContinue]="canContinue()"
        [continueLabel]="continueLabel()"
        [showContinue]="showContinue()"
        [detailsBadgeLabel]="detailsBadgeLabel()"
        [detailsBadgeClass]="detailsBadgeClass()"
        (back)="back.emit()"
        (continue)="continue.emit()" />
    </div>
  </div>
  <div class="wizard-scrollable-content">
    <ng-content />
  </div>
</main>
```

**Imports**: `StepIndicatorComponent`, `WizardActionBarComponent`
**SCSS**: Minimal — delegates to existing `_wizard.scss` globals.
**Change detection**: OnPush. Standalone.

---

## Phase 2: Family Wizard (validates the shell)

### 2A. `family-state.service.ts`

**Signal inventory** (all private + public readonly + controlled mutator):

| Signal | Type | Default |
|---|---|---|
| `_mode` | `'create' \| 'edit'` | `'create'` |
| `_username` | `string` | `''` |
| `_password` | `string` | `''` |
| `_parent1` | `FamilyContact` | empty |
| `_parent2` | `FamilyContact` | empty |
| `_address` | `FamilyAddress` | empty |
| `_children` | `ChildProfileDraft[]` | `[]` |
| `_submitting` | `boolean` | `false` |
| `_submitError` | `string \| null` | `null` |
| `_submitSuccess` | `boolean` | `false` |

**Local interfaces** (form-only, not backend DTOs):
```typescript
interface FamilyContact {
  firstName: string; lastName: string; phone: string;
  email: string; emailConfirm: string;
}
interface FamilyAddress {
  address1: string; city: string; state: string; postalCode: string;
}
// ChildProfileDraft already exists in current family-account-wizard.service.ts
```

**API calls**: `POST /api/family/register`, `PUT /api/family/update`, `GET /api/family/me`
**Injected**: `HttpClient`, `AuthService`
**Independent**: No dependency on `RegistrationWizardService`

### 2B. `family-wizard.component.ts`

**Steps definition**:
```typescript
steps = computed<WizardStepDef[]>(() => [
  { id: 'credentials', label: 'Credentials', enabled: this.state.mode() === 'create' },
  { id: 'contacts',    label: 'Contacts',    enabled: true },
  { id: 'address',     label: 'Address',     enabled: true },
  { id: 'children',    label: 'Children',    enabled: true },
  { id: 'review',      label: 'Review',      enabled: true },
]);
```

**canContinue** (computed, switch on `currentStepId`):
- `credentials`: username + password non-empty
- `contacts`: parent1 first/last/email filled, emailConfirm matches
- `address`: required fields filled
- `children`: at least 1 child with first + last name
- `review`: false (submit button is separate inside step)

**Deep-link**: `?mode=edit` sets mode, `?returnUrl=...` for post-wizard navigation, `?next=register-player` for chaining.

### 2C. Step Components (5 files)

Each: standalone, OnPush, injects `FamilyStateService`. Navigation handled by shell action bar (not per-step outputs).

| Step | Form fields | Notes |
|---|---|---|
| `credentials-step` | username, password, confirmPassword | Skipped in edit mode. Password match validator. |
| `contacts-step` | parent1 (5 fields), parent2 (5 fields) | Dynamic labels from job metadata. Phone digits-only filter. |
| `address-step` | address1, city, state (dropdown), postalCode | State dropdown from `FormFieldDataService`. |
| `children-step` | List of children (add/remove). Each: firstName, lastName, dob, gender | Array management via state service. |
| `review-step` | Read-only summary. Submit button. | Calls `state.submitFamily()`. Shows login panel for new accounts. |

---

## Phase 3: Player Registration Wizard (most complex)

### 3A. Service Decomposition — What Was 1 Monolith (1,314 lines) Becomes 5 Focused Services

#### `job-context.service.ts` (~150 lines)

**Owns**: Job identity + metadata + payment flags + ARB schedule.

**Private signals**: `_jobPath`, `_jobId`, `_jobProfileMetadataJson`, `_jobJsonOptions`, `_adnArb`, `_adnArbBillingOccurrences`, `_adnArbIntervalLength`, `_adnArbStartDate`, `_jobHasActiveDiscountCodes`, `_jobUsesAmex`, `_offerPlayerRegSaver`, `_regSaverDetails`.

**API calls**: `GET /api/jobs/{jobPath}` (metadata)
**Key method**: `loadJobMetadata(jobPath)` — fetches, parses, populates all signals. Delegates to `FormSchemaService.parse()` and `WaiverStateService.buildFromMetadata()`.

**Extracted from**: monolith lines ~37-41, 102-120, 460-532, 928-938.

#### `family-players.service.ts` (~200 lines)

**Owns**: Family user + player list + loading + selection + prior registrations.

**Private signals**: `_familyPlayers`, `_familyPlayersLoading`, `_familyUser`, `_hasFamilyAccount`, `_debugFamilyPlayersResp`.

**API calls**: `GET /api/family/players`, `POST /api/player-registration/set-wizard-context`
**Key methods**: `loadFamilyPlayers(jobPath)`, `loadFamilyPlayersOnce(jobPath)`, `setWizardContext(jobPath)`, `selectedPlayerIds()` (computed), `togglePlayerSelection(playerId)`.

**Extracted from**: monolith lines ~42-78, 247-452, 1015-1040, 1122-1124, 1278-1281.

#### `eligibility.service.ts` (~120 lines)

**Owns**: Constraint type/value + per-player eligibility + team selections.

**Private signals**: `_teamConstraintType`, `_teamConstraintValue`, `_selectedTeams`, `_eligibilityByPlayer`.

**Key methods**: `setSelectedTeams()`, `setEligibilityForPlayer()`, `getEligibilityForPlayer()`, `reset()`.

**Extracted from**: `PlayerStateService` (37 lines) + monolith lines ~88-99, 672-727.

#### `player-forms.service.ts` (~250 lines)

**Owns**: Per-player form values + validation + server validation errors.

**Private signals**: `_playerFormValues`, `_usLaxStatus`, `_serverValidationErrors`.

**Key methods**: `setPlayerFieldValue()`, `getPlayerFieldValue()`, `areFormsValid()`, `validateAllSelectedPlayers()`, `buildPreSubmitFormValues()`.

**Depends on** (readonly): `FormSchemaService`, `WaiverStateService`, `EligibilityService`, `FamilyPlayersService`.

**Extracted from**: monolith lines ~121-127, 543-670, 709-728, 838-926, 1142-1280.

#### `player-wizard-state.service.ts` (~100 lines) — THIN ORCHESTRATOR

**Owns nothing** — coordinates across sub-services for cross-cutting operations.

**Key methods**:
- `initialize(jobPath)` — calls `jobContext.loadJobMetadata()`, `familyPlayers.loadFamilyPlayers()`
- `reset()` — calls `reset()` on all sub-services
- `preSubmitRegistration()` — builds payload, calls `POST /api/player-registration/preSubmit`, processes insurance offer
- `submitPayment(...)` — delegates to `PaymentService`
- `loadConfirmation()` / `resendConfirmationEmail()`

### 3B. Existing Services Reused in Player Wizard

These are already well-structured. They currently inject `RegistrationWizardService`. Strategy: **create thin v2 wrappers** that inject the new decomposed services and expose the same interface the existing services expect.

| Service | Lines | V2 Wrapper Needed? |
|---|---|---|
| `FormSchemaService` | ~150 | No — standalone, no wizard dependency |
| `WaiverStateService` | ~357 | Yes — currently reads `reg.jobWaivers()` etc. |
| `InsuranceStateService` | 58 | Yes — currently injects `RegistrationWizardService` |
| `InsuranceService` | ~219 | Minimal — mostly standalone |
| `PaymentService` | ~250 | Yes — reads from `RegistrationWizardService` heavily |
| `PaymentStateService` | 31 | Yes — proxies to `RegistrationWizardService` |
| `TeamService` | ~182 | No — standalone |
| `UslaxService` | varies | No — standalone |
| `FeeUtils` | ~30 | No — pure functions |

For services needing v2 wrappers: create `payment-v2.service.ts`, `insurance-state-v2.service.ts`, etc. in the player `state/` directory. Each ~50-80 lines, injecting the new decomposed services instead of the monolith.

### 3C. `player-wizard.component.ts`

**Steps**:
```typescript
steps = computed<WizardStepDef[]>(() => [
  { id: 'family-check', label: 'Account',     enabled: true },
  { id: 'players',      label: 'Players',     enabled: true },
  { id: 'eligibility',  label: 'Eligibility', enabled: !!this.eligibility.teamConstraintType() },
  { id: 'teams',        label: 'Teams',       enabled: true },
  { id: 'forms',        label: 'Forms',       enabled: true },
  { id: 'waivers',      label: 'Waivers',     enabled: this.waiver.waiverDefinitions().length > 0 },
  { id: 'review',       label: 'Review',      enabled: true },
  { id: 'payment',      label: 'Payment',     enabled: true },
  { id: 'confirmation', label: 'Done',        enabled: true },
]);
```

**canContinue** (computed switch):
- `family-check`: false (has own CTAs)
- `players`: selectedPlayerIds().length > 0
- `eligibility`: all selected players have eligibility set
- `teams`: all selected players have team assigned
- `forms`: playerForms.areFormsValid()
- `waivers`: waiver.allRequiredWaiversAccepted()
- `review`: true
- `payment`: complex (delegates to payment/insurance state)
- `confirmation`: false (end)

**Override actions**: `review → next` calls `orchestrator.preSubmitRegistration()` instead of simple navigation.

### 3D. Step Components (9 files)

| Step | Injects | Key behavior |
|---|---|---|
| `family-check-step` | `AuthService`, `FamilyPlayersService`, Router | Login/create CTAs. Auto-advance if authenticated. |
| `player-selection-step` | `FamilyPlayersService` | Checkbox list. Toggle selection. Prior reg badges. Color coding. |
| `eligibility-step` | `EligibilityService`, `FamilyPlayersService`, `JobContextService` | Per-player dropdown. Constraint type detection from metadata. |
| `team-selection-step` | `EligibilityService`, `TeamService` | Grouped team lists. Multi-select when no constraint. Capacity guard. |
| `player-forms-step` | `PlayerFormsService`, `FormSchemaService`, `FamilyPlayersService` | Dynamic form fields. Conditional visibility. US Lacrosse validation. |
| `waivers-step` | `WaiverStateService`, `FamilyPlayersService` | Accordion with exclusive open. Auto-advance to next unchecked. Signature. |
| `review-step` | All state services (readonly) | Summary. Server validation errors. |
| `payment-step` | `PaymentServiceV2`, `InsuranceService`, `InsuranceStateServiceV2`, `IdempotencyService` | CC form. ARB/PIF/Deposit options. Discount. VI widget. |
| `confirmation-step` | `PlayerWizardStateService` | Confirmation HTML. Resend email. "Finish" CTA. |

---

## Phase 4: Team Registration Wizard

### 4A. Service Design

#### `club-rep-state.service.ts` (~60 lines)

**Private signals**: `_availableClubs`, `_selectedClub`, `_clubInfoCollapsed`, `_clubRepInfoAlreadyRead`, `_metadataError`.

**Key methods**: `loadClubs()`, `selectClub()`, `reset()`.
**Injected**: `ClubRepWorkflowService` (existing, keep), `AuthService`.

#### `team-wizard-state.service.ts` (~80 lines)

Thin orchestrator.
**Injected**: `ClubRepStateService`, `TeamRegistrationService` (existing), `TeamPaymentService` (existing), `TeamInsuranceStateService` (existing).
**Key methods**: `initializeForClub()`, `reset()`.

### 4B. Existing Team Services — ALL Reused As-Is

Team services are already well-structured (unlike the player monolith):
- `TeamRegistrationService` (261 lines) — all team API calls
- `TeamPaymentService` (210 lines) — line items, totals, discount
- `TeamPaymentStateService` (35 lines) — payment result
- `TeamInsuranceStateService` (91 lines) — VI state
- `TeamInsuranceService` (197 lines) — VI widget + purchase
- `ClubRepWorkflowService` (171 lines) — login + clubs

These do NOT inject `RegistrationWizardService`, so no v2 wrappers needed.

### 4C. Step Components (4 files)

| Step | Injects | Key behavior |
|---|---|---|
| `login-step` | `ClubRepStateService`, `ClubRepWorkflowService`, `AuthService` | Login/register form. Club selection modal. Duplicate detection. |
| `teams-step` | `TeamRegistrationService`, `ClubRepStateService`, `TeamPaymentService` | Register/unregister teams. Age group grid. Refund policy. |
| `payment-step` | `TeamPaymentService`, `TeamInsuranceService`, `IdempotencyService` | CC form. Discount. VI widget. PIF only (no ARB). |
| `review-step` | `TeamRegistrationService` | Confirmation HTML. Send email. Print. |

---

## Phase 5: Route Swap + Cleanup

### Step 1: Add v2 routes side-by-side in `app.routes.ts`

```typescript
{ path: 'family-account-v2',   loadComponent: () => import('...wizards-v2/family/...') },
{ path: 'register-player-v2',  loadComponent: () => import('...wizards-v2/player/...') },
{ path: 'register-team-v2',    loadComponent: () => import('...wizards-v2/team/...') },
```

### Step 2: Test on `-v2` routes. Compare against old routes.

### Step 3: Swap primary routes to point at v2 components.

### Step 4: Delete old directories:
- `wizards/player-registration-wizard/`
- `wizards/team-registration-wizard/`
- `wizards/family-account-wizard/`
- Old root services: `RegistrationWizardService`, `PlayerStateService`, `FamilyAccountWizardService`

Keep `wizards/shared/` (still used by v2).

---

## Implementation Sequence

| Order | Task | Est. Lines | Depends On |
|---|---|---|---|
| 1a | `wizard-shell.types.ts` | 15 | — |
| 1b | `WizardShellComponent` + html + scss | 120 | 1a |
| 2a | `family-state.service.ts` | 140 | — |
| 2b | `family-wizard.component.ts` + 5 steps | 400 | 1b, 2a |
| 3a | `job-context.service.ts` | 150 | — |
| 3b | `family-players.service.ts` | 200 | — |
| 3c | `eligibility.service.ts` | 120 | — |
| 3d | `player-forms.service.ts` | 250 | 3a–3c |
| 3e | `player-wizard-state.service.ts` | 100 | 3a–3d |
| 3f | V2 wrapper services (payment, insurance, waiver) | 200 | 3a–3e |
| 3g | `player-wizard.component.ts` + 9 steps | 700 | 3e–3f, 1b |
| 4a | `club-rep-state.service.ts` | 60 | — |
| 4b | `team-wizard-state.service.ts` | 80 | 4a |
| 4c | `team-wizard.component.ts` + 4 steps | 400 | 4b, 1b |
| 5 | Route swap + cleanup | 50 | All above |

**Total new code**: ~2,985 lines replacing ~5,000+ lines of monolith.

---

## Verification

After each phase:
1. `ng serve` — no compilation errors
2. Navigate to the wizard route — shell renders, steps display
3. Walk through the full flow — each step validates and navigates
4. Test deep-link: `?step=<id>` lands on correct step
5. Test theme: `wizard-theme-player` / `-team` / `-family` classes apply
6. Test mobile: step indicator compacts, action bar responsive
7. Test dark mode: no hardcoded colors, CSS variables respected
8. Test error states: API failures show user-facing error, no swallowed errors

Final validation before Phase 5 swap:
- Full end-to-end registration flow (Family → Player) on v2 routes
- Full team registration flow on v2 route
- Confirm payments process correctly (use test CC)
- Confirm insurance widget loads and purchase completes
- Confirm confirmation emails send
