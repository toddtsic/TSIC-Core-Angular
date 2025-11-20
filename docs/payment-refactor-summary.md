# Payment Step Refactor Summary

## Goals
- Reduce complexity in the payment step container component.
- Centralize business logic (totals, scenarios, discount, insurance) inside focused services.
- Introduce smaller, purely presentational components for summary, option selection, and credit card entry.
- Make side‑effects (HTTP, widget init) explicit and testable.

## Key Changes
1. Added `PaymentService` (signals + computed):
   - `lineItems`, `totalAmount`, `depositTotal`, scenario detection (`isDepositScenario`, `isArbScenario`), recurring billing helpers.
   - Centralized discount application (`applyDiscount`, `resetDiscount`) with status signals (`appliedDiscount`, `discountMessage`, `discountApplying`).
   - `currentTotal` now deducts applied discount consistently for both Full and Deposit options.
2. Added `InsuranceService`:
   - Encapsulates Vertical Insure widget initialization, quote capture, user response, and purchase calls.
   - Provides derived helpers: `premiumTotal()`, `quotedPlayers()`, and purchase flows (`purchaseInsurance`, `purchaseInsuranceAndFinish`).
3. Decomposed UI:
   - `PaymentSummaryComponent` renders all totals & scenario rows (no logic, only display).
   - `PaymentOptionSelectorComponent` owns payment option radios + discount code input, invoking `PaymentService`.
   - `CreditCardFormComponent` provides reactive form + validation; parent only receives `validChange` and `valueChange` events.
4. `payment.component.ts` simplified to orchestration only:
   - Delegates financial & discount logic to `PaymentService`.
   - Delegates insurance widget + purchases to `InsuranceService`.
   - Handles submit flow (idempotency key, final emission, gating for insurance decision).
   - Removed inline discount / card validation / insurance purchase duplication.
5. Introduced state slice facades:
   - `InsuranceStateService` proxying insurance consent/modal/offer signals (first step toward shrinking `RegistrationWizardService`).
   - `PaymentStateService` proxying `paymentOption` and `lastPayment` summary.
6. Added `IdempotencyService` abstracting localStorage key persistence for retriable payment submissions.
7. Began UI modal decomposition (next component: VI charge confirmation modal) to further reduce container responsibility.

## Submission Flow Overview
1. Validate user has responded to insurance offer if presented.
2. Validate credit card form if payment due (or VI-only flow needs card).
3. If insurance quotes present, show confirmation modal before proceeding.
4. Perform TSIC payment (`submit-payment` endpoint) with idempotency key.
5. On success: persist summary, optionally trigger insurance purchase if quotes exist.
6. For VI-only flow: use `InsuranceService.purchaseInsuranceAndFinish` to finalize without TSIC charge.

## Signals & Responsibilities
- Payment totals & discount: `PaymentService`.
- Insurance quotes & purchase orchestration: `InsuranceService`.
- Insurance consent/modal offer: `InsuranceStateService` (facade over wizard service for progressive extraction).
- Payment option & last payment summary: `PaymentStateService`.
- Idempotency key persistence: `IdempotencyService`.
- Container component now focuses on orchestration (submit gating + wiring child components).

## Testing Added
- `payment.service.spec.ts`: line items, totals, deposit total, discount success & failure.
- `insurance.service.spec.ts`: quoted player formatting, premium calculation, purchase success/failure posting.

## Future Improvements
- Complete extraction of VI confirmation/charge modal into a dedicated `ViModalControllerComponent` (in progress).
- Move remaining legacy helper `isViOfferVisible()` into `InsuranceService` or replace with a widget state flag.
- Expand unit tests for ARB scenarios and edge discount cases (zero / over-amount discount).
- Migrate consent & payment slice signals fully out of `RegistrationWizardService` (facades become owners).
- Tighten typing around quotes and insurance policy responses (introduce dedicated interfaces).
- Add state-machine style test harness for submit flow (TSIC + VI vs VI-only scenarios).

## Migration Notes
- All former local insurance fields (`quotes`, `viHasUserResponse`, purchase methods) removed from `payment.component.ts`.
- Discount application removed from container and handled solely via `PaymentService`.
- Credit card validation no longer duplicated; solely in `CreditCardFormComponent`.

---
Refactor completed: architecture now aligns with separation of concerns, improves testability, and reduces coupling between UI and business logic.
