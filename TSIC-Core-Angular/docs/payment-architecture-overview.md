## Payment Architecture Overview

### Scope
This document summarizes the current payment processing flow in `PaymentService`, recurring billing (ARB) behavior, idempotency guarantees, insurance (VerticalInsure) persistence, and supporting test coverage.

### High-Level Flow
1. Validate the requested `PaymentOption` (PIF, Deposit, ARB) against job flags.
2. Load family registrations for the job and normalize fee fields (base, total, owed) via `IFeeResolverService`.
3. Branch:
   - ARB: Build schedule from job metadata; compute per-occurrence charge; create subscription via Authorize.Net; persist subscription metadata onto each registration.
   - PIF/Deposit: Compute per-registration charge map; perform early idempotency check (prevents duplicate charges by invoice key); send charge request to Authorize.Net; update registrations and accounting entries; optionally persist RegSaver (VerticalInsure) policy identifiers.
4. Save changes and return a `PaymentResponseDto` including either a `TransactionId` or an `SubscriptionId`.

### Idempotency
Frontend now persists a generated idempotency key in `localStorage` (see `payment.component.ts`) using a composite key of job + family. On successful payment the key is cleared; on failure it is retained so retry uses the same invoice number. Backend pre-check joins `RegistrationAccounting` with `Registrations` filtering by job, family, and `AdnInvoiceNo`. If found, returns success with a duplicate prevention message and skips charging.

### ARB Schedule Logic
`BuildArbSchedule` safeguards defaults:
* Occurrences: defaults to 10 if missing or <= 0.
* Interval length (months): defaults to 1 if missing or <= 0.
* Start date: defaults to tomorrow if not provided.

Per-occurrence amount = (Sum of `OwedTotal` across selected registrations) / occurrences, rounded to 2 decimals AwayFromZero. All registration rows receive subscription metadata (`AdnSubscriptionId`, amounts, interval, status=active) in a single update pass.

### Accounting Entries
For PIF/Deposit, each positive charged registration creates a `RegistrationAccounting` row with:
* `Paymeth`: "Credit Card Payment - {Option}".
* `AdnInvoiceNo`: idempotency key if provided.
* `AdnTransactionId`: Authorize.Net transaction id.
The static payment method GUID is a legacy constant pending centralization.

### VerticalInsure
If the request sets `ViConfirmed` and supplies a new `ViPolicyNumber`, registrations without a policy ID are updated with policy metadata (policy number + create date). Done post-charge to avoid blocking on insurance persistence.

### Recent Refactor Highlights
* Removed unused parameter from registration update (converted to `UpdateRegistrationsForCharge`).
* Converted helper methods to static / synchronous where async was unnecessary.
* Added early idempotency check before charging card.
* Implemented frontend idempotency key persistence and reuse on retry.
* Deleted placeholder `Class1.cs` files to remove empty class warnings.
* Removed obsolete commented-out code from `AdnApiService`.
* Added pragma suppression for hard-coded external URIs in `TsicConstants` (documented justification).

### Test Coverage
`PaymentServiceTests` currently validate:
* Policy persistence with VerticalInsure data.
* Idempotency duplicate call returns expected "Duplicate prevented" style message / no double charging.
* Deposit capping logic (deposit <= owed remainder).
* ARB subscription creation populates all subscription fields and per-occurrence amount calculation.

### Extension / Future Improvements
* Centralize payment method GUID into configuration.
* Enhance ARB error handling (map common Authorize.Net failure codes to user-friendly messages similar to one-off charge path).
* Consider moving fee normalization into a dedicated domain service for reuse by registration flows.
* Add integration tests around invoice key reuse across process restarts.
* Capture basic metrics (count of payments, duplicate prevented events).

### Edge Cases Considered
* Empty registrations set: returns "No registrations found" early.
* Zero total for selected PIF/Deposit: returns "Nothing due".
* Idempotent retry: bypasses charge, returns success message.
* Invalid job or disabled payment option: descriptive validation messages.
* ARB with non-positive or missing schedule parameters: safe defaults applied.

### Error Messaging Normalization
`AdnApiService` centralizes translation of Authorize.Net error codes (e.g., 2, 11, 45, 65) to user-friendly text, minimizing leakage of raw gateway phrasing.

---
Maintained by: Automated cleanup pass (November 2025). Update this document alongside significant PaymentService changes or new payment options.