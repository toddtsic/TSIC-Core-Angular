# Wizard Structural Audit — 2026-02-17

**Keyword**: `CWCC Implement WIZARD-STRUCTURAL-FIX`
**Purpose**: Fix ALL D/D+/C- sub-categories to A/A+. No cosmetic work. Structural problems only.
**Scope**: Player wizard, team wizard, shared services, shared infrastructure.

---

## Current State: Detailed Grade Grid

Three prior remediation passes addressed surface issues (CSS tokens, ARIA attributes, CommonModule).
The deep structural problems were never touched. This audit is the definitive source of truth.

---

### TYPE SAFETY

| Sub-Category | Grade | Location | Evidence |
|---|---|---|---|
| `any` in registration-wizard.service.ts | D+ | Lines 126, 131, 288, 320, 367, 369, 471, 545, 588, 677, 684, 698, 793, 813, 947 | 20+ explicit `any` usages. `debugFamilyPlayersResp = signal<any>(null)`, `norm: any = {}`, `player: any`, `value: any`, `source: Record<string, any>` |
| `(resp as any).Prop` fallback chains | D | Lines 298, 311, 340, 358, 367 | 5 instances of `resp.prop \|\| (resp as any).Prop` — case-insensitive property access without helper |
| `any` in payment.service.ts | D+ | Lines 186, 207, 222 | `mergeUpdatedFinancials(updatedFinancials: Record<string, any>)`, `getAmountFromFinancials(financials: any)`, `getAmount(team: any)` |
| `any` in insurance.service.ts | D+ | Lines 24, 41, 54, 73, 176, 182, 195 | `initWidget(hostSelector: string, offerData: any)`, callback params `(st: any)`, all extract methods take `any` |
| `any` in form-schema.service.ts | D+ | Lines 26, 36, 54, 81 | `fields: any[]`, `getOptionSetInsensitive(): any[] \| null`, `mapFieldType(raw: any)`, `direct.map((o: any) => ...)` |
| `any` in waiver-state.service.ts | D+ | Lines 125, 150, 154 | `getMetaString(obj: any, key: string)`, `hasAcceptanceField(predicate: (labelL: string, nameL: string, f: any) => boolean)`, `fields: any[]` |
| `any` in team-selection.component.ts | D+ | Lines 245, 382, 391, 398, 424, 427, 438, 448 | 9 Syncfusion event handler params all typed `any`. `syncFields = { ... } as any` |
| `any` in player-forms.component.ts | C- | Lines 356, 425, 432, 534 | `setValue(playerId, field, val: any)`, `modalData: any = null`, `prettyJson(obj: any)`, unsafe cast `(t as any)?.perRegistrantFee` |
| `any` in payment.component.ts | C | Lines 22, 463, 612 | `Window { VerticalInsure?: any }`, `rs: any` param, `onCcValidChange(valid: any)` |
| `any` in family-check.component.ts | C | Line 268 | `catch (err: any)` |

**Total: ~60+ explicit `any` usages across wizard codebase**

**FIX STRATEGY**:
1. Extract interfaces for API response shapes (case-insensitive property helper)
2. Type Syncfusion event params with SF event interfaces
3. Replace `Record<string, any>` with typed records
4. Type all callback/handler params
5. Type all error catches with `unknown` + type narrowing

---

### SIGNAL ARCHITECTURE

| Sub-Category | Grade | Location | Evidence |
|---|---|---|---|
| Public writable signals in payment.service.ts | D+ | Lines 30-33 | `appliedDiscount`, `discountMessage`, `discountApplying`, `appliedDiscountResponse` — all public, mutable from any consumer |
| Public writable signals in insurance.service.ts | D+ | Lines 31-34 | `quotes`, `hasUserResponse`, `error`, `widgetInitialized` — all public writable |
| Public writable signals in team-payment.service.ts | D+ | Lines 35-49 | 9 public writable: `teams`, `paymentMethodsAllowedCode`, `bAddProcessingFees`, `bApplyProcessingFeesToTeamDeposit`, `jobPath`, `selectedPaymentMethod`, `appliedDiscountResponse`, `discountMessage`, `discountApplying` |
| Plain booleans on OnPush: family-check | D+ | Lines 132-141 | 8+ plain properties: `username`, `password`, `loginError`, `submitting`, `submittingAction`, `inlineError`, `usernameTouched`, `passwordTouched`, `_pendingFocusPassword` |
| Plain state on OnPush: payment.component | C- | Lines 193-236 | `creditCard` object (nested mutations invisible), `verticalInsureError`, `pendingSubmitAfterViConfirm` |
| Plain state on OnPush: player-forms | C- | Lines 424-425 | `modalOpen = false`, `modalData: any = null` |
| Non-computed template methods | C- | Multiple | `selectedPlayerIds()` in service (called 6+ times), `canSubmit()` / `canInsuranceOnlySubmit()` in payment, 7+ methods per row in player-forms and team-selection `@for` loops |
| `_offerPlayerRegSaver` plain boolean in service | D | Line 477 | Private boolean, not a signal — not reactive |

**FIX STRATEGY**:
1. Follow `InsuranceStateService` pattern: private signals + public readonly + controlled setters
2. Convert family-check plain properties to signals
3. Convert payment.component `creditCard` to signal
4. Convert remaining template methods to `computed()` signals
5. Make `_offerPlayerRegSaver` a signal

---

### SUBSCRIPTION & LIFECYCLE MANAGEMENT

| Sub-Category | Grade | Location | Evidence |
|---|---|---|---|
| Player service `.subscribe()` without cleanup | C- | Service lines 221, 435, 916 | `loadFamilyPlayers()`, `ensureJobMetadata()`, `loadConfirmation()` — manual `sub?.unsubscribe()` on reassignment but no `OnDestroy` cleanup |
| `applyDiscount()` returns void | D+ | payment.service.ts line 120 | Fires internal `.subscribe()`, caller cannot manage lifecycle. Should return Observable |
| MutationObserver never disconnected | D | vi-dark-mode.service.ts line 62 | `MutationObserver` created, `disconnect()` method exists (line 79) but never called automatically |
| Unmanaged setTimeout in team-reg-modal | C | Line 132 | `setTimeout(() => this.specialCharBlocked.set(false), 3000)` — no cleanup on destroy |
| Unmanaged setTimeout in team-edit-modal | C | Lines 159, 201, 268 | 3 instances of `setTimeout(() => this.close(), 1500)` — no cleanup |
| Team wizard subscriptions | A- | teams-step, review-step | Properly use `takeUntilDestroyed()` |

**FIX STRATEGY**:
1. Add `DestroyRef` + `takeUntilDestroyed()` to all player service subscriptions
2. Change `applyDiscount()` to return Observable
3. Track all `setTimeout` handles and clear in `ngOnDestroy`
4. Add auto-disconnect for MutationObserver via DestroyRef callback

---

### ERROR HANDLING

| Sub-Category | Grade | Location | Evidence |
|---|---|---|---|
| Swallowed errors (11+) | C- | payment.component: 412, 486, 534; team-selection: 337, 354, 544; player-forms: 446; constraint-selection: 164, 187; waivers: 258; family-check: 279, 282 | `catch { }` or `catch { /* ignore */ }` with no logging |
| Console-only error logging | C | Service line 471 | `ensureJobMetadata()` error handler only logs — no UI state update |
| Untyped error params | D+ | Service line 288, family-check line 268 | `handleFamilyPlayersError(err: any)`, `catch (err: any)` |
| Silent promise failures | C | Service line 158, 929 | `applyDiscount` promise `.catch(err => console.warn(...))`, `resendConfirmationEmail` returns `false` on error |

**FIX STRATEGY**:
1. Replace all `catch { }` with `catch { /* focus/localStorage expected to fail */ }` comments or actual logging
2. Type all error params as `unknown` with type narrowing helpers
3. Add error state signals where console-only logging exists
4. Extract shared `handleHttpError(err: unknown): string` utility

---

### CODE DUPLICATION

| Sub-Category | Grade | Location | Evidence |
|---|---|---|---|
| Duplicate field resolution methods | D+ | Service lines 609 vs 1056 | `resolveFieldName()` and `resolveTargetFieldName()` — near-identical logic, different signatures |
| Repeated `hasAll()` pattern | C- | Service lines 267, 667, 881, 1136 | Case-insensitive field matching reimplemented 3+ times |
| Property fallback chains | D | Service lines 298, 311, 340, 358, 367 | `resp.prop \|\| (resp as any).Prop` — 5 instances, should be one `getPropertyCI()` helper |
| US Lax status mutation duplication | C | Service lines 1108, 1111 | Two methods with identical clone-mutate-set pattern |

**FIX STRATEGY**:
1. Unify `resolveFieldName` + `resolveTargetFieldName` into single method
2. Extract `hasAll()` to shared utility
3. Extract `getPropertyCaseInsensitive<T>()` helper
4. Extract `updateUsLaxStatus()` consolidated mutation

---

### COMMONMODULE (Team Wizard)

| Component | Grade | Evidence |
|---|---|---|
| team-registration-wizard.component.ts | D | Line 61: `imports: [CommonModule, ...]` |
| teams-step.component.ts | D | Line 68: `imports: [CommonModule, ...]` |
| review-step.component.ts | D | Line 22: `imports: [CommonModule]` |
| team-registration-modal.component.ts | D | Line 18: `imports: [CommonModule, ...]` |

**FIX**: Same pattern as Phase 8.4 — replace with granular `NgClass`, `NgIf`, pipe imports.

---

### INLINE STYLES

| Component | Grade | Lines | Evidence |
|---|---|---|---|
| payment.component.ts | C- | 52, 53, 115, 144 | 4 inline `style="..."` attributes |
| player-forms.component.ts | C- | 87, 115, 271 | 3 inline styles |
| team-selection.component.ts | C- | 127-129, 199 | 2 inline styles + `[ngStyle]` |
| family-check.component.ts | C- | 45, 101 | 2 inline styles with `animation: slideIn` |

**FIX**: Move to component `.scss` files or use class bindings.

---

### ACCESSIBILITY GAPS (Remaining)

| Sub-Category | Grade | Location | Evidence |
|---|---|---|---|
| `aria-describedby` on player-forms inputs | C- | player-forms.component.ts | `[id]="helpId(...)"` set on help text but never wired to input's `aria-describedby` |
| `aria-describedby` on team-reg-modal | C- | team-registration-modal.component.html line 92 | Team name input has no `aria-describedby` for validation pattern |
| Missing `aria-label` on select | C | team-registration-modal.component.html line 99 | `<select>` for team name suggestions has no `aria-label` |
| Missing `aria-label` on close button | C | team-registration-wizard.component.html line 143 | Alert dismiss button missing `aria-label` |
| Icon buttons without labels | C | review-step: print/resend buttons | Have text but no `aria-label` for icon portion |

---

## Composite Honest Grades (Current)

| Dimension | Grade | Primary Blocker |
|---|---|---|
| Architecture / Decomposition | A | — |
| Type Safety | D+ | 60+ `any`, untyped API responses, missing interfaces |
| Signal Encapsulation | D+ | 20+ public writable signals, 8+ plain booleans on OnPush |
| Subscription Management | C- | 3 unmanaged in player service, void applyDiscount, MutationObserver |
| Error Handling | C- | 11+ swallowed, console-only, untyped error params |
| Code Duplication | D+ | 4 duplicate patterns in service layer |
| CommonModule (Team) | D | 4 components still importing CommonModule |
| Inline Styles | C- | 11 inline styles across 4 components |
| Accessibility (Remaining) | C+ | aria-describedby gaps on forms, missing labels on a few elements |
| Design System Colors | A | Fixed in prior remediation |
| Design System Spacing | B+ | Fixed in prior remediation |
| Mobile / Responsive | A | No issues |

**Composite: C-** (Structural problems dominate despite surface polish)

---

## Implementation Order (Recommended)

**Phase A**: Type Safety — extract interfaces, eliminate all `any` (~60 fixes)
**Phase B**: Signal Encapsulation — private signals + setters, convert plain booleans (~30 fixes)
**Phase C**: Subscription & Lifecycle — `takeUntilDestroyed`, Observable returns, timer cleanup (~10 fixes)
**Phase D**: Code Deduplication — unify service helpers (~8 fixes)
**Phase E**: Error Handling — type errors as `unknown`, add logging, extract utility (~15 fixes)
**Phase F**: CommonModule + Inline Styles + Remaining ARIA (~20 fixes)

Each phase builds + verifies before committing. No cosmetic detours.
