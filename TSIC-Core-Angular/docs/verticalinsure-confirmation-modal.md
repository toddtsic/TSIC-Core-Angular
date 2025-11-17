# VerticalInsure Confirmation Modal Plan

## Goal
Add an explicit user confirmation step when opting into VerticalInsure (RegSaver) insurance during registration/payment. This ensures auditable, intentional acceptance separate from simply rendering the embedded offer widget.

## Current State (Baseline)
- The payment step loads the VerticalInsure embedded offer script globally (see `index.html`).
- `payment.component.ts` calls `tryInitVerticalInsure()` and initializes the widget when an offer is present.
- Purchase intent is inferred indirectly (e.g., presence of `ViConfirmed`, policy number after independent purchase). There is no dedicated confirmation UX.

## Problems Without Modal
1. Ambiguous intent: Merely displaying the widget is not consent.
2. Difficult support audits: No timestamped acknowledgment stored separately from policy persistence.
3. Potential accidental opt‑in if future auto-validation behaviors change.
4. Limited opportunity to summarize coverage / exclusions before commit.

## UX Flow
1. User reaches Payment step; widget loads (offer visible but not yet confirmed).
2. User interacts with the VerticalInsure widget (select coverage / enters tokenized payment method if required by VI flow).
3. User clicks a TSIC "Add Insurance" button (outside the widget) which:
   - Requests a pre-validation from VerticalInsure instance (`instance.validate()` already exists).
   - Opens confirmation modal summarizing: Player(s) covered, coverage amount(s), price, policy effective date, cancellation statement, disclaimers.
4. Modal includes:
   - Dynamic details from the `verticalInsureOffer` signal (playerObject response).
   - Checkbox: "I acknowledge this insurance purchase is independent from registration fee payments."
   - Confirm / Cancel buttons.
5. On Confirm:
   - Set `this.state.setInsuranceIntent({ confirmed: true, timestampUtc: new Date().toISOString() })` (new signal/state object).
   - Persist any necessary token or quote IDs for later policy purchase call.
   - Enable the final registration submission button if all other payment requirements met.
6. On Cancel: Close modal; do not set confirmed flag.

## Data Model Additions
Introduce a small DTO/state shape (front-end only initially):
```ts
interface InsuranceIntentState {
  confirmed: boolean;
  timestampUtc: string | null;
  quotes: string[]; // from offer playerObject
  insurableAmountTotal: number; // precomputed
}
```
Persist (optional future) server-side via a lightweight POST (audit trail) before invoking actual purchase.

## Component Changes (`payment.component.ts`)
- Add a `MatDialog` (or custom overlay service) injected.
- Add method `openInsuranceConfirmModal()` that builds the summary from `verticalInsureOffer().data`.
- Guard final submit: require `insuranceIntent.confirmed === true` when user selected insurance; else block or show helper text.

## Modal Content Draft
Title: "Confirm Insurance Purchase"
Body Sections:
1. Players Covered (list names + team/assignment)
2. Coverage Details (benefit description, policy number placeholder until purchase)
3. Cost Summary (premium total, taxes/fees if any)
4. Independence Notice (bold): "This insurance purchase is processed separately from your registration payment and may charge your card independently." 
5. Acknowledgment Checkbox (required).
Buttons: Confirm (primary), Cancel (secondary).

## Accessibility & Internationalization
- Use semantic headings and list markup within modal for screen readers.
- Ensure focus trap inside modal; shift focus to first actionable element.
- All text strings to be placed in a translation-ready resource service (future).

## Error Handling
- If `verticalInsureOffer.data` becomes null while modal open (e.g., widget revalidated and failed), auto-close modal with toast: "Offer expired. Please re-load insurance offer.".
- If confirmation attempt fails due to missing quotes array, log warning and show inline error: "Insurance data incomplete; reload offer.".

## Testing Strategy
1. Unit: Modal state toggles confirmation flag only when checkbox checked.
2. Component: Opening modal renders correct player list given stubbed `verticalInsureOffer`.
3. Integration (future): Simulated user path sets `confirmed` then allows submit.
4. Regression: Without choosing insurance (offer absent) modal path not exposed.

## Future Enhancements
- Persist audit record server-side (`POST /api/insurance/intent`) with user ID, job ID, player registration IDs, timestamp.
- Add cancellation flow prior to final payment if user unchecks confirmation.
- Display policy terms loaded from VerticalInsure API (if endpoint available) inside an expandable section.

## Implementation Steps (Incremental)
1. Create modal component (`vertical-insure-confirm-modal.component.ts`).
2. Inject dialog into payment step; wire button.
3. Introduce `InsuranceIntentState` signal in `registration-wizard.service.ts`.
4. Update final submit logic to require confirmation when insurance selected.
5. Add basic unit tests.
6. (Optional) Add audit POST endpoint.

## Non-Goals (Initial Release)
- Real-time multi-player premium recalculation inside modal.
- Server persistence of intent (can follow).
- Integration with payment gateway (insurance remains decoupled).

---
This plan keeps insurance architecturally independent while adding explicit, verifiable user consent before policy purchase.
