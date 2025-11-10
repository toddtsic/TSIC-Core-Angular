# Payment Step Implementation

## Overview
The Payment step in the Angular player registration wizard allows users to select a payment option (Pay in Full, Deposit, or Automated Recurring Billing) and submit payment via Authorize.Net integration.

## UI Components

### PaymentComponent (`steps/payment.component.ts`)
- **Payment Options**: Radio buttons for PIF, Deposit, ARB with dynamic totals
- **Line Items Table**: Per-player breakdown showing player name, team, and amount
- **Credit Card Form**: Fields for card number, expiry (MMYY), CVV, name, address, zip
- **Reactive Totals**: Computed signals for total amount, deposit (50%), and current total based on selection

### Key Features
- Uses Angular signals for reactive state management
- Integrates with `RegistrationWizardService` for wizard state
- Calls `TeamService.getTeamById()` for team data and fee calculation
- Emits `submitted` event on successful payment

## Backend Integration

### PaymentService (`Services/PaymentService.cs`)
- Processes payment requests via Authorize.Net API
- Supports PIF, Deposit, and ARB payment flows
- Updates registrations with PaidTotal/OwedTotal and AdnSubscriptionId
- Creates accounting records for payment tracking

### AdnApiService (`Services/AdnApiService.cs`)
- Wraps Authorize.Net SDK calls for charges, authorizations, ARB subscriptions
- Handles transaction processing and error mapping
- Supports sandbox and production environments

### API Endpoints
- `POST /api/registration/submit-payment`: Accepts PaymentRequestDto and processes payment

## Data Flow
1. User selects payment option and enters credit card details
2. Component submits request with jobId, familyUserId, paymentOption, creditCard
3. Backend retrieves registrations for the job/family
4. Calculates total based on payment option (PIF: full amount, Deposit: 50%, ARB: full amount)
5. Processes payment via Authorize.Net
6. Updates database with payment details and subscription info (for ARB)
7. Returns success/failure response

## Fee Calculation Logic
- Hierarchical: Team > Age Group > League
- PIF: Full FeeTotal per registration
- Deposit: 50% of FeeBase (configurable)
- ARB: Full amount with monthly subscription setup

## Future Enhancements
- Add payment method selection (multiple cards, saved cards)
- Implement payment plan options beyond ARB
- Add tax calculation and handling
- Integrate with accounting/reporting systems
- Add payment confirmation emails and receipts</content>
<parameter name="filePath">c:\Users\tgree\source\repos\TSIC-Core-Angular\TSIC-Core-Angular\docs\payment-step-implementation.md