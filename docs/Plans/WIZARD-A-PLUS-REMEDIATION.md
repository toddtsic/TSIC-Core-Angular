# Wizard A+ Remediation Plan

**Created:** 2026-02-17
**Status:** Ready to implement
**Reference Name:** "WIZARD-A-PLUS-REMEDIATION"
**Starting Grade:** B-
**Target Grade:** A+

## How to Use This Plan

Tell Claude: **"CWCC Implement WIZARD-A-PLUS-REMEDIATION phase N"** (where N = 1-8).
Each phase is self-contained. Build & verify after each phase before moving to the next.
Commit after each phase with `git commit --no-verify`.

---

## Phase 1: Critical Bugs

### 1.1 — Fix TeamEditModal runtime crash
**File:** `TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/registration/wizards/team-registration-wizard/team-edit-modal/team-edit-modal.component.ts`
**Problem:** Lines 121, 162-163, 226 call `this.teamService.updateClubTeam()`, `inactivateClubTeam()`, `activateClubTeam()`, `deleteClubTeam()` — these methods do NOT exist on `TeamRegistrationService`. This causes a runtime crash when any edit operation is attempted.
**Fix:** Either add stub methods to `TeamRegistrationService` that delegate to proper API calls, or wire the component to the correct service that has these methods. Check if there's an existing `ClubTeamService` or similar. If these are genuinely unimplemented features, add proper `throw new Error('Not implemented')` with a toast notification.

### 1.2 — Convert `submitting` boolean to signal in player PaymentComponent
**File:** `TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/registration/wizards/player-registration-wizard/steps/payment.component.ts`
**Problem:** Line ~218: `submitting = false` is a plain boolean on a `ChangeDetectionStrategy.OnPush` component. The Pay button's `[disabled]="!canSubmit()"` reads this non-reactively, so the button may not disable when clicked.
**Fix:**
1. Change `submitting = false` → `readonly submitting = signal(false)`
2. Change all `this.submitting = true/false` → `this.submitting.set(true/false)`
3. Update `canSubmit()` to read `this.submitting()` (with parentheses)
4. Update template references: `submitting` → `submitting()` if used directly in template
5. Also convert these related plain booleans to signals: `lastError`, `showViChargeConfirm`, `ccValid`, `modalOpen` — check each for template usage under OnPush

### 1.3 — Add submitting guard to `proceedToPayment()`
**File:** `TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/registration/wizards/player-registration-wizard/player-registration-wizard.component.ts`
**Problem:** Lines ~327-347: `proceedToPayment()` is async but has no guard. Double-clicking Continue fires two `preSubmitRegistration()` calls simultaneously.
**Fix:**
1. Add `readonly proceedingToPayment = signal(false)` to the component
2. Guard at top of method: `if (this.proceedingToPayment()) return;`
3. Set `this.proceedingToPayment.set(true)` before the try block
4. Set `this.proceedingToPayment.set(false)` in the finally block
5. Wire into `canContinue` computed or the action bar's disabled state so the button disables during the async call

### 1.4 — Fix `doInlineLogin()` unhandled promise rejection
**File:** `TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/registration/wizards/player-registration-wizard/steps/family-check.component.ts`
**Problem:** Lines ~275-308 and ~311-325: `signInThenProceed()` and `signInThenGoFamilyAccount()` both `await this.doInlineLogin()` inside `try/finally` with NO `catch` block. `doInlineLogin()` can reject its promise, causing an unhandled promise rejection.
**Fix:** Add a `catch` block to both methods:
```typescript
try {
    const result = await this.doInlineLogin();
    // ... existing logic
} catch (err) {
    // Error is already displayed via this.inlineError set in doInlineLogin
    console.warn('Login failed:', err);
} finally {
    this.submittingAction = null;
}
```

### 1.5 — Fix `applyDiscount()` synchronous throw
**File:** `TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/registration/wizards/team-registration-wizard/services/team-payment.service.ts`
**Problem:** Lines ~126-131: Guard clauses use `throw new Error(...)` inside a method that returns `Observable`. Callers use `.subscribe()`, but synchronous throws are NOT caught by Observable error handlers.
**Fix:** Replace `throw` with `return throwError(() => new Error('...'))`:
```typescript
import { throwError } from 'rxjs';

applyDiscount(code: string, teamIds: string[]): Observable<...> {
    if (!code || this.discountApplying()) {
        return throwError(() => new Error('Invalid discount code or already applying'));
    }
    if (teamIds.length === 0) {
        return throwError(() => new Error('No teams selected for discount'));
    }
    // ... rest of method
}
```

### 1.6 — Fix InsuranceService error path leaving user stuck
**File:** `TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/registration/wizards/player-registration-wizard/services/insurance.service.ts`
**Problem:** Lines ~199-216: `purchaseInsuranceAndFinish()` error handler does NOT call `onFinish`. The payment component's `submitted.emit()` never fires, leaving the user stuck on the payment step.
**Fix:** Call `onFinish` in the error handler too (or provide a separate error callback):
```typescript
error: (err: HttpErrorResponse) => {
    this.purchasing.set(false);
    this.toast.show('Processing with Vertical Insurance failed', 'danger', 4000);
    console.warn(...);
    onFinish(); // Allow payment flow to continue/show error state
}
```

**Build & test after Phase 1. Commit: "fix: critical wizard bugs — signals, guards, error paths"**

---

## Phase 2: Service Layer Architecture

### 2.1 — Move payment HTTP calls to services
**Player wizard:**
- **From:** `player-registration-wizard/steps/payment.component.ts` line ~446 — `this.http.post<PaymentResponseDto>(...)`
- **To:** Add a `submitPayment(request)` method to `player-registration-wizard/services/payment.service.ts`
- Remove `HttpClient` injection from the component
- Component calls `this.paymentService.submitPayment(request)` instead

**Team wizard:**
- **From:** `team-registration-wizard/payment-step/payment.component.ts` line ~498-500 — `this.http.post<TeamPaymentResponseDto>(...)`
- **To:** Add a `submitPayment(request)` method to `team-registration-wizard/services/team-payment.service.ts`
- Remove `HttpClient` injection from the component

### 2.2 — Extract `ensureJobMetadata()` into JobMetadataService
**From:** `player-registration-wizard/registration-wizard.service.ts` lines ~430-554
**To:** New file `player-registration-wizard/services/job-metadata.service.ts`

Extract these responsibilities:
- Parse metadata JSON options
- Extract ARB flags (arbNumOccurrences, arbInterval, etc.)
- Extract offer flags (offerPlayerRegSaver)
- Iterate metadata keys to find waiver blocks
- Build waiver definitions array
- Call `parseProfileMetadata()` for form schemas

The root service calls `jobMetadata.loadAndParse(response)` and reads results via signals.

### 2.3 — Consolidate InsuranceService duplication
**File:** `player-registration-wizard/services/insurance.service.ts`
**Problem:** `purchaseInsurance()` (~lines 100-156) and `purchaseInsuranceAndFinish()` (~lines 158-217) share 90% identical code.
**Fix:** Create one method with an optional `onComplete` callback:
```typescript
purchaseInsurance(onComplete?: () => void): void {
    // ... shared logic
    // On success: if (onComplete) onComplete();
    // On error: if (onComplete) onComplete(); // still allow flow to continue
}
```

### 2.4 — Add `AuthService.applyNewToken()` public method
**File:** `src/app/infrastructure/services/auth.service.ts`
**Problem:** Both `registration-wizard.service.ts:244` and `team-registration.service.ts:70-71` use `this.auth['setToken'](...)` bracket notation to bypass private access.
**Fix:**
1. Add public method to AuthService: `public applyNewToken(token: string): void { this.setToken(token); this.initializeFromToken(); }`
2. Replace bracket notation in both wizard services with `this.auth.applyNewToken(response.accessToken)`

### 2.5 — Move waiver-building logic to WaiverStateService
**From:** `registration-wizard.service.ts` lines ~479-554 (waiver definition building, iteration over metadata keys)
**To:** `waiver-state.service.ts` — add a `buildFromMetadata(metadata: Record<string, any>)` method
**Root service calls:** `this.waiverState.buildFromMetadata(parsedMetadata)` instead of inline logic

**Build & test after Phase 2. Commit: "refactor: extract service layer — payment HTTP, job metadata, auth token, waivers"**

---

## Phase 3: Subscription & Lifecycle Cleanup

### 3.1 — TeamsStepComponent: add DestroyRef + takeUntilDestroyed
**File:** `team-registration-wizard/teams-step/teams-step.component.ts`
**Problem:** No OnDestroy, 4 untracked subscriptions: `getTeamsMetadata().subscribe()`, `unregisterTeamFromEvent().subscribe()`, `registerTeamForEvent().subscribe()`, `acceptRefundPolicy().subscribe()`
**Fix:**
1. `private readonly destroyRef = inject(DestroyRef);`
2. Add `.pipe(takeUntilDestroyed(this.destroyRef))` before every `.subscribe()` call
3. Also add a duplicate-request guard to `unregisterTeam()` (it's missing one, unlike `registerTeam()`)

### 3.2 — ReviewStepComponent (team): add cleanup
**File:** `team-registration-wizard/review-step/review-step.component.ts`
**Problem:** No OnDestroy, 2 subscriptions in ngOnInit: `getConfirmationText().subscribe()` and `sendConfirmationEmail().subscribe()`
**Fix:** Same pattern — inject `DestroyRef`, pipe `takeUntilDestroyed()` before each subscribe.

### 3.3 — Fix nested subscribe in team PaymentComponent.applyDiscount()
**File:** `team-registration-wizard/payment-step/payment.component.ts` lines ~428-434
**Problem:** Nested `.subscribe()` inside `.subscribe()` — inner subscribe has no error handler and no cleanup.
**Fix:** Flatten with `switchMap`:
```typescript
this.paymentSvc.applyDiscount(code, teamIds).pipe(
    switchMap(() => this.teamReg.getTeamsMetadata(true)),
    takeUntilDestroyed(this.destroyRef)
).subscribe({
    next: (response) => this.metadata.set(response),
    error: (err) => { this.toast.show('Failed to apply discount', 'danger'); }
});
```

### 3.4 — Store subscriptions for root service HTTP calls
**File:** `player-registration-wizard/registration-wizard.service.ts`
**Problem:** `loadFamilyPlayers()` and `ensureJobMetadata()` create fire-and-forget subscribes.
**Fix:** Store subscription references and cancel previous on re-call:
```typescript
private metadataSub?: Subscription;

ensureJobMetadata(): void {
    this.metadataSub?.unsubscribe();
    this.metadataSub = this.http.get(...).subscribe({...});
}
```

### 3.5 — Fix FamilyCheckComponent.doInlineLogin()
**File:** `player-registration-wizard/steps/family-check.component.ts`
**Problem:** Lines ~255-273: `.subscribe()` wrapped in `new Promise()` — anti-pattern that can't be cleaned up.
**Fix:** Replace with `firstValueFrom()`:
```typescript
async doInlineLogin(): Promise<string> {
    if (!this.username || !this.password || this.submitting) return 'ok';
    this.submitting = true;
    this.inlineError = '';
    try {
        const response = await firstValueFrom(
            this.auth.login(this.username, this.password)
        );
        // ... handle success
        return 'ok';
    } catch (err) {
        this.inlineError = err?.message || 'Login failed';
        throw err;
    }
}
```

### 3.6 — Store `continueFromClubSelection` subscription
**File:** `team-registration-wizard/login-step/club-rep-login-step.component.ts` line ~456
**Fix:** Assign to `this.workflowSubscription` (which already exists and is cleaned up in ngOnDestroy).

### 3.7 — Clear setTimeout calls in player PaymentComponent
**File:** `player-registration-wizard/steps/payment.component.ts`
**Problem:** `setTimeout(() => this.tryInitVerticalInsure(), 0)` and `setTimeout(() => this.simpleHydrateFromCc(...), 300)` in ngAfterViewInit with no cleanup.
**Fix:** Store timeout IDs, clear in ngOnDestroy:
```typescript
private viInitTimeout?: ReturnType<typeof setTimeout>;
private hydrateTimeout?: ReturnType<typeof setTimeout>;

ngOnDestroy(): void {
    clearTimeout(this.viInitTimeout);
    clearTimeout(this.hydrateTimeout);
}
```

**Build & test after Phase 3. Commit: "fix: subscription cleanup — takeUntilDestroyed, flatten nested subscribes"**

---

## Phase 4: Design System — Colors

### 4.1 — Replace `bg-white` with `bg-body`
**Files & lines:**
- `player-forms.component.ts` line ~248: `bg-white` → `bg-body`
- `player-selection.component.ts` line ~43: `bg-white bg-opacity-75` → use `style="background: rgba(var(--bs-body-bg-rgb), 0.75)"` or `bg-body bg-opacity-75`

### 4.2 — Replace `text-dark` / `text-white` with emphasis variants
**Pattern:** `text-dark` → `text-body-emphasis` or specific `text-primary-emphasis`, `text-danger-emphasis` etc.
**Files (8+ instances):**
- `player-forms.component.ts`: lines ~35, 88, 148, 218 — badges with `text-dark` or `text-white`
- `player-selection.component.ts`: line ~33 — `bg-warning text-dark` → `bg-warning-subtle text-warning-emphasis`
- `team-selection.component.ts`: lines ~76, 85 — `bg-primary-subtle text-dark` → `bg-primary-subtle text-primary-emphasis`
- `payment-summary.component.ts`: line ~46 — `bg-secondary-subtle text-dark` → `bg-secondary-subtle text-secondary-emphasis`
- `payment.component.ts` (player): lines ~47, 135, 149 — same pattern

### 4.3 — Fix wizard-modal hardcoded colors
**File:** `shared/wizard-modal/wizard-modal.component.ts`
- Line ~81: `border-top: 1px solid rgba(0, 0, 0, 0.1)` → `border-top: 1px solid var(--bs-border-color-translucent)`
- Line ~82: `background: var(--bs-body-bg, #fff)` → `background: var(--bs-body-bg)` (remove `#fff` fallback)

### 4.4 — Fix non-palette-adaptive gradient
**File:** `player-registration-wizard/verticalinsure/vi-charge-confirm-modal.component.ts`
- Line ~10: `var(--bs-indigo)` → `var(--brand-primary-dark)` or the project's gradient-end token

### 4.5 — Replace `bg-light` / `text-bg-light`
**File:** `player-forms.component.ts`
- Line ~272: `bg-light` → `bg-body-secondary`
- Line ~150: `text-bg-light` → `bg-body-secondary text-body-secondary`

### 4.6 — Fix Balance Due banner contrast
**Files:** Both payment components (player and team)
- `style="background: var(--bs-primary); color: var(--neutral-0);"` → `style="background: var(--bs-primary); color: var(--bs-white);"` or better: use `class="bg-primary text-white"` (Bootstrap ensures contrast)

### 4.7 — Replace hardcoded shadows with tokens
**File:** `shared/wizard-action-bar/wizard-action-bar.component.scss` lines ~76, 83, 89
**File:** `src/styles/_wizard.scss` line ~204
Replace raw `box-shadow: 0 4px 12px rgba(...)` with `box-shadow: var(--shadow-sm)` / `var(--shadow-md)` from the design system. Check `_tokens.scss` for available shadow tokens.

**Build & test after Phase 4. Commit: "fix: design system color compliance — dark mode safe"**

---

## Phase 5: Type Safety

### 5.1 — Type `playerFormValues`
**File:** `registration-wizard.service.ts`
- Replace `signal<Record<string, Record<string, any>>>({})` with a typed interface
- Define: `type PlayerFormValues = Record<string, Record<string, string | number | boolean | null>>`
- This is the minimum — ideally the form field names would be typed too, but that requires the schema to be typed first (5.2)

### 5.2 — Type FormSchemaService.fields
**File:** `player-registration-wizard/services/form-schema.service.ts`
- Replace `fields: any[]` with a `FormFieldDefinition` interface:
```typescript
interface FormFieldDefinition {
    name: string;
    label: string;
    type: 'text' | 'number' | 'date' | 'select' | 'multiselect' | 'checkbox' | 'textarea';
    required: boolean;
    options?: { value: string; label: string }[];
    helpText?: string;
    // ... other known properties from mapFieldType
}
```
- Also type `optionSets: Record<string, any>` → `Record<string, SelectOption[]>`

### 5.3 — Remove `(t: any)` cast on typed array
**File:** `team-payment.service.ts` line ~54
- Change `teams.map((t: any) => ({` → `teams.map((t) => ({`
- The `teams` signal is `RegisteredTeamDto[]`, so `t` is already typed

### 5.4 — Fix `return null as any`
**File:** `player-registration-wizard/steps/constraint-selection.component.ts` line ~166
- Change return type of the method to `... | null` and return `null` directly

### 5.5 — Type VI quotes
**New file or inline in:** `insurance.service.ts` / `team-insurance.service.ts`
```typescript
interface VerticalInsureQuote {
    id: string;
    quote_id: string;
    total: number;
    policy_attributes?: {
        teams?: { team_name: string }[];
        players?: { first_name: string; last_name: string }[];
    };
}
```
Replace `signal<any[]>([])` → `signal<VerticalInsureQuote[]>([])` in both insurance services.

### 5.6 — Remove dead code
**File:** `registration-wizard.service.ts`
- Remove `formData = signal<Record<string, any>>({})` (dead signal, set in reset but never read)
- Remove `deriveConstraintTypeFromJsonOptions()` at bottom of file (never called)
- Remove duplicate `mapFieldType()` at module level (duplicates the one in FormSchemaService)

### 5.7 — Remove dead no-op methods
**File:** `player-forms.component.ts` — remove `canShowInlineTeamSelect()`, `inlineTeamsFor()`, `inlineSelectedTeam()`, `onInlineTeamChange()`
**File:** `team-selection.component.ts` — remove `onSingleChange()`, `onMultiChange()` (marked as legacy adapters)

### 5.8 — Fix `buildFamilyPlayersList()` any normalization
**File:** `registration-wizard.service.ts` lines ~366-404
- Check if the generated `FamilyPlayerDto` matches the actual API response
- If mismatched, run `.\scripts\2-Regenerate-API-Models.ps1` first
- If the backend response truly has inconsistent casing, create a one-time normalizer typed against the DTO

**Build & test after Phase 5. Commit: "refactor: type safety — typed form models, VI quotes, dead code removal"**

---

## Phase 6: Accessibility

### 6.1 — Fix duplicate `id="rw-title"`
**File:** `player-registration-wizard/player-registration-wizard.component.html` lines ~9, 20
- Mobile header: `id="rw-title-mobile"`
- Desktop header: `id="rw-title-desktop"`
- Update `aria-labelledby` on the wizard container to reference both: `aria-labelledby="rw-title-mobile rw-title-desktop"`

### 6.2 — Fix `aria-labelledby="tw-title"` missing target
**File:** `team-registration-wizard/team-registration-wizard.component.html`
- The `tw-title` element only renders when `clubName()` is truthy
- Add a fallback heading that always renders (even if hidden): `<h1 id="tw-title" class="visually-hidden">Team Registration</h1>` as a base, with the visible title overlaying when clubName is available

### 6.3 — Add focus trap to USA Lacrosse modal
**File:** `player-forms.component.ts` lines ~247-278
- Best: Replace the hand-rolled modal with `<app-wizard-modal>` which already has proper focus management
- Minimum: Add `cdkTrapFocus` directive from `@angular/cdk/a11y`, add focus-return logic on close

### 6.4 — Add `aria-invalid` to player form dynamic fields
**File:** `player-forms.component.ts`
- For each input type (text, select, date, etc.), add:
  `[attr.aria-invalid]="isFieldInvalid(player.userId, field.name)"`
- Create helper: `isFieldInvalid(playerId: string, fieldName: string): boolean`

### 6.5 — Add `aria-invalid` to club rep registration fields
**File:** `team-registration-wizard/login-step/club-rep-login-step.component.html` lines ~99-193
- Add `[attr.aria-invalid]="registrationForm.get('fieldName')?.invalid && registrationForm.get('fieldName')?.touched"` to each input

### 6.6 — Include player name in checkbox labels
**File:** `player-selection.component.ts` line ~63
- Change: `'Select player'` → `'Select ' + p.firstName + ' ' + p.lastName`
- Change: `'Deselect player'` → `'Deselect ' + p.firstName + ' ' + p.lastName`
- Change: `'Already registered'` → `'Already registered: ' + p.firstName + ' ' + p.lastName`

### 6.7 — Wire `aria-describedby` on waiver checkboxes
**File:** `player-registration-wizard/steps/waivers.component.ts`
- Add `id` to error div: `[id]="'waiver-err-' + w.id"`
- Add `[attr.aria-describedby]="(submitted() && w.required && controlInvalid(w.id)) ? 'waiver-err-' + w.id : null"` to checkbox

### 6.8 — Add `aria-pressed` to mode toggle buttons
**File:** `team-registration-wizard/teams-step/modals/team-registration-modal/team-registration-modal.component.html` lines ~14-27
- Add `[attr.aria-pressed]="mode() === 'existing'"` and `[attr.aria-pressed]="mode() === 'new'"` to respective buttons

### 6.9 — Fix spinner contradiction
**File:** `player-selection.component.ts` line ~44
- Remove `aria-hidden="true"` from the spinner div (it conflicts with `role="status"`)
- Add `<span class="visually-hidden">Loading...</span>` inside the spinner

**Build & test after Phase 6. Commit: "fix: accessibility — ARIA attributes, focus management, screen reader support"**

---

## Phase 7: Design System — Spacing

### 7.1 — `review-step.component.scss` (9 values)
Convert: `1rem` → `var(--space-4)`, `.75rem` → `var(--space-3)`, `.5rem` → `var(--space-2)`, `.25rem` → `var(--space-1)`, `1.5rem` → `var(--space-6)`

### 7.2 — `teams-step.component.scss` (13 values)
Same token mapping. Keep off-grid values (0.375rem, 0.125rem) that have no token equivalent.

### 7.3 — `wizard-action-bar.component.scss` (8 values)
Same token mapping. Keep `1.75rem` min-height (no token).

### 7.4 — `team-registration-modal.component.scss` (3 values)
Quick pass: `.25rem` → `var(--space-1)`, `.5rem` → `var(--space-2)`

### 7.5 — Inline `style` attributes
- `family-check.component.ts` line ~29: `margin-top: 2rem` → `margin-top: var(--space-8)`
- Various `min-width: 180px` — leave as-is (no token, aesthetic sizing)
- Various `max-height` values — leave as-is (no token, scroll container sizing)

**Token reference (from `_tokens.scss`):**
| Token | Value |
|-------|-------|
| `--space-1` | 0.25rem (4px) |
| `--space-2` | 0.5rem (8px) |
| `--space-3` | 0.75rem (12px) |
| `--space-4` | 1rem (16px) |
| `--space-5` | 1.25rem (20px) |
| `--space-6` | 1.5rem (24px) |
| `--space-8` | 2rem (32px) |
| `--space-10` | 2.5rem (40px) |
| `--space-12` | 3rem (48px) |

**Build & test after Phase 7. Commit: "fix: design system spacing — convert hardcoded rem to --space-N tokens"**

---

## Phase 8: UX Finishing Touches

### 8.1 — Confirmation timeout → error + retry
**File:** `player-registration-wizard/steps/confirmation.component.ts`
- Add `readonly loadError = signal(false)`
- When safety timer fires, set `this.loadError.set(true)`
- Template: show error message with retry button when `loadError()` is true

### 8.2 — Convert template methods to computed signals
**File:** `player-registration-wizard/steps/payment.component.ts`
- Convert `isViCcOnlyFlow()`, `tsicChargeDueNow()`, `showPayNowButton()`, `showCcSection()`, `arbHideAllOptions()` from methods to `computed()` signals

### 8.3 — Extract color helpers to shared utility
**From:** `player-selection.component.ts`, `player-forms.component.ts`, `team-selection.component.ts`
**To:** New file `shared/utils/color-class.util.ts`
```typescript
export function colorClassForIndex(idx: number): string { ... }
export function textColorClassForIndex(idx: number): string { ... }
```
Import in all three components, delete the duplicated methods.

### 8.4 — Replace CommonModule with individual imports
**All 12+ components** that import `CommonModule`:
- If only using `| currency`: import `CurrencyPipe`
- If only using `| date`: import `DatePipe`
- If using `ngClass`: import `NgClass`
- Most components using `@if`/`@for` don't need CommonModule at all — those are built-in

### 8.5 — Team wizard: convert step @if to @switch
**File:** `team-registration-wizard/team-registration-wizard.component.html`
Replace lines ~99-113:
```html
@switch (step()) {
    @case (WizardStep.Login) { <app-club-rep-login-step .../> }
    @case (WizardStep.RegisterTeams) { <app-teams-step .../> }
    @case (WizardStep.Payment) { <app-team-payment-step .../> }
    @case (WizardStep.Review) { <app-review-step .../> }
}
```

### 8.6 — Format raw DOB with date pipe
**File:** `player-selection.component.ts` line ~66
- Change `{{ p.dob || 'DOB not on file' }}` → `{{ p.dob ? (p.dob | date:'mediumDate') : 'DOB not on file' }}`

### 8.7 — Encapsulate TeamPaymentService writable signals
**File:** `team-registration-wizard/services/team-payment.service.ts`
- Change `teams = signal<...>([])` → `private readonly _teams = signal<...>([])`
- Add `readonly teams = this._teams.asReadonly()`
- Add `setTeams(teams: RegisteredTeamDto[]): void { this._teams.set(teams); }`
- Repeat for `paymentMethodsAllowedCode`, `bAddProcessingFees`, `selectedPaymentMethod`
- Update all callers (mainly `TeamsStepComponent.proceedToPayment()`)

### 8.8 — Set jobPath in TeamPaymentService
**File:** `team-registration-wizard/teams-step/teams-step.component.ts`
- In `proceedToPayment()`, add: `this.paymentSvc.jobPath.set(this.jobPath())` alongside the other `.set()` calls
- (Or use the new setter if 8.7 is done first)

**Build & test after Phase 8. Commit: "polish: UX finishing touches — computed signals, shared utils, CommonModule cleanup"**

---

## Post-Implementation Verification

After all 8 phases, run:
```powershell
cd TSIC-Core-Angular/src/frontend/tsic-app
npx ng build --configuration=production
npx vitest run
```

Then do a final deep-dive audit to confirm A+ grade.

---

## Quick Reference: Commit Messages

| Phase | Commit Message |
|-------|---------------|
| 1 | `fix: critical wizard bugs — signals, guards, error paths` |
| 2 | `refactor: extract service layer — payment HTTP, job metadata, auth token, waivers` |
| 3 | `fix: subscription cleanup — takeUntilDestroyed, flatten nested subscribes` |
| 4 | `fix: design system color compliance — dark mode safe` |
| 5 | `refactor: type safety — typed form models, VI quotes, dead code removal` |
| 6 | `fix: accessibility — ARIA attributes, focus management, screen reader support` |
| 7 | `fix: design system spacing — convert hardcoded rem to --space-N tokens` |
| 8 | `polish: UX finishing touches — computed signals, shared utils, CommonModule cleanup` |
