# Frontend Test Catalog

This document summarizes existing Angular unit tests (`*.spec.ts`) in the `tsic-app` frontend.

## Overview
Current tests focus on core shell initialization, registration/payment workflow services, insurance purchase flow, state/idempotency helpers, and a confirmation modal component.

| File | Subject | Key Behaviors Tested | External Dependencies Mocked | Notable Gaps |
|------|---------|----------------------|------------------------------|--------------|
| `app.component.spec.ts` | `AppComponent` | Component creation; title property value (`tsic-app`) | Angular TestBed only | No route/init lifecycle tests; no global layout assertions |
| `insurance.service.spec.ts` | `InsuranceService` | Quoted player name formatting; premium total aggregation; successful purchase POST; failure handling with toast; discount of cents → dollars | `RegistrationWizardService` stub, `ToastService` stub, `HttpTestingController` | No retry/backoff tests; no error path for network failures beyond simple failure response |
| `insurance-state.service.spec.ts` | `InsuranceStateService` | Proxying offer flag; opening modal; confirming purchase; declining; closing without consent | `RegistrationWizardService` stub | No concurrency tests; no validation of state transitions after external updates |
| `idempotency.service.spec.ts` | `IdempotencyService` | Persist/load/clear idempotency key; resilience (does not throw on storage errors); key naming | jsdom `localStorage` | No expiration strategy; no multi-job multi-family collision tests |
| `payment.service.spec.ts` | `PaymentService` | Line item build; total & deposit calculations; discount application; ARB scenario computations; rounding; clamping total at zero | `RegistrationWizardService` stub, `PlayerStateService`, `TeamService` stub, `HttpTestingController` | No multi-team per player tests; no late-fee/processing-fee scenarios; no negative ARB validation (e.g. zero occurrences) |
| `payment-state.service.spec.ts` | `PaymentStateService` | Reading initial option; setting payment option; setting/clearing last payment summary | `RegistrationWizardService` stub | No reactive change propagation tests; no invalid option rejection |
| `vi-charge-confirm-modal.component.spec.ts` | `ViChargeConfirmModalComponent` | Title variations (insurance-only vs combined); rendering player list, premium, email; emits confirmed/cancelled events | Component TestBed only | No accessibility (focus trap) tests; no conditional disabling of buttons |

## Suggested Additions
1. Team Service: caching logic, filtered/grouped derivations, roster full indicator.
2. Registration Wizard: pre-submit payload building (team selections, waiver injection, eligibility field mapping).
3. Fee utilities (extract fee/deposit logic to pure functions and test edge precedence).
4. Error & retry flows (simulate transient HTTP errors for insurance/payment services).
5. Accessibility checks (modal focus management, ARIA attributes).
6. Performance micro-tests (ensure large player lists do not degrade line item generation).

## Running Tests
```powershell
# From repository root or frontend folder
cd TSIC-Core-Angular/src/frontend/tsic-app
npm test
# CI/headless (Vitest runs headless by default)
npm test -- --no-watch
```

## Coverage Strategy (Proposed)
- Target >80% line coverage for service logic (Insurance, Payment, Team, Idempotency).
- Extract pure helpers (fee, discount calculations) for 100% branch coverage.
- Use spies for environment-driven branching (e.g., ARB schedule conditions).

## Prioritization
| Priority | Area | Rationale |
|----------|------|-----------|
| High | PreSubmit payload assembly | Critical for registration correctness; complex mapping rules |
| High | Team filtering/grouping | Drives visible roster choices; depends on constraint types |
| Medium | ARB schedule edge cases | Financial accuracy; rounding and interval validations |
| Medium | Modal accessibility | User experience & compliance |
| Low | LocalStorage eviction strategy | Less critical short-term; still useful for robustness |

## Notes
- Tests currently rely on lightweight stubs instead of full mocks; keep stubs minimal and focused.
- Consider centralizing factory builders for common stub objects (players, teams, quotes) to reduce duplication.
- Integrate Vitest's built-in code coverage (`--coverage`) and publish reports in CI.

---
Generated on: 2025-11-21
