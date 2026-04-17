# Discount Code Test Coverage — Player Registration

**Related punchlist items:** SP-018, SP-008, SP-009
**Test files:**
- Backend: `src/backend/TSIC.Tests/PlayerRegistration/DiscountCode/DiscountCodeTests.cs`
- Frontend: `src/frontend/tsic-app/src/app/views/registration/player/state/payment-v2.service.spec.ts` (discount section)

---

## Backend Tests (C# / xUnit)

Tests exercise `PlayerRegistrationPaymentController.ApplyDiscount` with real repositories against an in-memory database.

| # | Scenario | Input | Expected Output | What It Proves |
|---|----------|-------|-----------------|----------------|
| 1 | $100 absolute on $595 fee (no processing) | feeBase=$595, code=$100 absolute | fee_discount=$100, owed=$495, no correction row | Partial discount leaves correct remaining balance |
| 2 | $100 absolute on $595 fee (3.5% processing) | feeBase=$595, processing=$20.83, code=$100 | Processing reduced proportionally, owed≈$512 | Processing fee adjusts with discount |
| 3 | $100 absolute on $100 fee (exact match) | feeBase=$100, code=$100 | owed=$0, correction row for $100, paid=$100 | Zero-balance triggers correction accounting |
| 4 | $100 absolute on $50 fee (exceeds fee) | feeBase=$50, code=$100 | discount capped at $50, owed=$0, correction=$50 | Discount can't exceed fee |
| 5 | 100% discount on $595 fee | feeBase=$595, code=100% | fee_discount=$595, owed=$0, correction=$595 | Full percent discount works |
| 6 | 50% discount on $200 fee | feeBase=$200, code=50% | fee_discount=$100, owed=$100, no correction | Partial percent discount |
| 7 | $100 absolute across two players | p1=$400, p2=$200, code=$100 | p1 gets $66.67, p2 gets $33.33, sum=$100 | Multi-player proportional distribution |
| 8 | Already-discounted player | feeDiscount > 0 before apply | Rejected: "Discount already applied" | Guard prevents double-discount |
| 9 | Expired discount code | Code with past end date | Rejected: "Invalid or expired discount code" | Date-range validation works |
| 10 | fee_base = $0 (pathological) | feeBase=$0, code=$100 | Documents actual behavior — pins the bug | SP-018 root cause hypothesis test |

### Key assertions on every test:
- Registration financial state (FeeDiscount, FeeTotal, OwedTotal, PaidTotal)
- BActive flag (does NOT change — stays false for unpaid registrations)
- Accounting row count and shape (correction row only when owed → 0)
- Response DTO matches DB state

---

## Frontend Tests (Angular / Jest)

Tests verify the payment display logic in `PaymentV2Service` — what the parent SEES after a discount is applied.

| # | Scenario | Setup | Expected | What It Proves |
|---|----------|-------|----------|----------------|
| F1 | Partial discount — currentTotal reflects remaining | financials.owedTotal=$495 | currentTotal()=$495 | UI shows correct remaining after discount |
| F2 | Full discount — currentTotal is zero | financials.owedTotal=$0 | currentTotal()=0 | Zero-balance display triggers correctly |
| F3 | New player uses team fee | No financials, team.fee=$595 | currentTotal()=$595 | Pre-discount display is correct |
| F4 | Multi-player sum | p1 owedTotal=$400, p2 owedTotal=$200 | currentTotal()=$600 | Totals aggregate correctly |
| F5 | Mixed: one discounted, one new | p1 owedTotal=$495, p2 team fee=$300 | currentTotal()=$795 | Mixed financial sources work |
| F6 | resetDiscount clears state | After resetDiscount() | appliedDiscount()=0, message=null | Reset doesn't corrupt totals |

### What these tests DON'T cover (future work):
- `showPayNowButton` / `showNoPaymentInfo` toggle (lives in component, not service)
- HTTP round-trip of `applyDiscount()` (backend handles this; tested in C# suite)
- Confirmation email trigger on zero-balance completion
- BActive flip on zero-balance (backend responsibility)

---

## How to run

**Backend:**
```bash
dotnet test src/backend/TSIC.Tests --filter "FullyQualifiedName~DiscountCode"
```

**Frontend:**
```powershell
.\scripts\Ann-Frontend-Testing\PlayerRegistration\run-frontend-tests.ps1
```
