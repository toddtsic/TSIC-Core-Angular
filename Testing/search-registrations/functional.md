# Testing Plan: Registration Search & Management

## Context

The Registration Search page is the **most-used, most-important interface** in TSIC. It handles multi-criteria search across thousands of registrations, financial management (CC charges, checks, corrections, refunds, voids), profile viewing/editing, batch email, and ARB subscription management. A bug here can mean incorrect financial totals, failed credit card charges, orphaned accounting records, or lost payment history.

This testing plan covers both Phase 1 (search, detail panel, refund, email) and Phase 2 (CC charge, check/correction, edit record, void logic, subscription). Every test guards against a specific, realistic failure mode.

---

## Philosophy: What NOT to Test

- ❌ That Angular renders templates (framework's job)
- ❌ That `signal.set(x)` makes `signal()` return `x` (framework's job)
- ❌ That auto-generated API models have correct properties (generator's job)
- ❌ That Syncfusion Grid renders columns (vendor's job)
- ❌ That `ejs-multiselect` shows options (vendor's job)
- ❌ That Bootstrap icons render (CSS's job)
- ❌ Detail panel label text (visual, not logical)
- ❌ CSS variable color values per palette (visual, not logical)
- ❌ Mobile viewport layout positioning (visual, not logical)

**Guiding principle**: If a test would pass even with a serious financial data bug, it's not worth writing.

---

## Layer 1: Backend Integration Tests (HIGHEST VALUE — Start Here)

### Why This Layer Matters Most

The backend enforces financial integrity and Authorize.Net gateway logic. If `ChargeCc` creates an accounting record but doesn't update `OwedTotal`, the admin sees stale financials. If `ProcessRefund` doesn't distinguish void vs refund, ADN rejects the transaction. The repository pattern makes these tests clean: seed data → call endpoint → assert database state + financial totals.

### Setup

- Use `WebApplicationFactory<Program>` with a test database (or in-memory SQLite)
- Call real HTTP endpoints through `HttpClient`
- Assert on **database state** (financial totals, accounting record counts), not just HTTP status codes
- Seed realistic data per test (registrations with/without accounting records, with/without ADN subscription)
- Mock `IAdnApiService` for gateway calls (charge, refund, void, subscription)

### Test Cases

#### 1.1 Search — Multi-Criteria Filtering (CRITICAL)

| # | Test | Seed State | Action | Assert |
|---|------|-----------|--------|--------|
| 1 | Name search — single term | 3 registrations: "Smith", "Johnson", "Smithson" | `POST /search` with `name="smi"` | Returns 2 results (Smith, Smithson) |
| 2 | Name search — first + last | Registrations: "John Smith", "Jane Smith", "John Doe" | `POST /search` with `name="John Smith"` | Returns 1 result (John Smith) |
| 3 | Multi-select role filter | 5 Players, 3 Coaches, 2 Staff | `POST /search` with `roleIds=["player-guid","coach-guid"]` | Returns 8 results |
| 4 | Active status filter | 10 active, 5 inactive registrations | `POST /search` with `activeStatuses=["True"]` | Returns 10 results |
| 5 | Pay status — UNDER PAID | 3 regs: OwedTotal=0, OwedTotal=100, OwedTotal=-50 | `POST /search` with `payStatuses=["UNDER PAID"]` | Returns 1 result (OwedTotal > 0) |
| 6 | Pay status — multi-select | Same as above | `POST /search` with `payStatuses=["PAID IN FULL","OVER PAID"]` | Returns 2 results (OwedTotal=0 and OwedTotal<0) |
| 7 | Combined AND logic | 5 active players, 3 active coaches, 2 inactive players | `POST /search` with `activeStatuses=["True"]`, `roleIds=["player-guid"]` | Returns 5 (active AND player) |
| 8 | LADT tree OR logic | Reg A in Team1 (Div1), Reg B in Team2 (Div2), Reg C unassigned | `POST /search` with `teamIds=["team1"]`, `divisionIds=["div2"]` | Returns 2 (A + B, OR logic across LADT levels) |
| 9 | Date range filter | Regs: Jan 1, Feb 15, Mar 30 | `POST /search` with `regDateFrom="Feb 1"`, `regDateTo="Mar 1"` | Returns 1 (Feb 15 only) |
| 10 | Empty result set | No registrations in job | `POST /search` | Returns 0 results, aggregates all zero |
| 11 | Result cap at 5,000 | 6,000 registrations | `POST /search` | Returns max 5,000 rows |
| 12 | Aggregates across full result set | 3 regs: FeeTotal 100/200/300 | `POST /search` | `TotalFees=600`, `TotalPaid` and `TotalOwed` correct |

```csharp
[Fact]
public async Task Search_MultiSelectRoles_ReturnsUnion()
{
    // Arrange: seed 5 players, 3 coaches, 2 staff
    // Act: POST /search with roleIds containing player + coach GUIDs
    // Assert: returns 8 results (5+3), staff excluded
}
```

#### 1.2 Filter Options — Count Accuracy (CRITICAL)

| # | Test | Seed State | Action | Assert |
|---|------|-----------|--------|--------|
| 13 | Role counts | 5 Players, 3 Coaches, 2 Staff | `GET /filter-options` | Role options: Player(5), Coach(3), Staff(2) |
| 14 | Active status counts | 8 active, 4 inactive | `GET /filter-options` | Active(8), Inactive(4); Active has `defaultChecked=true` |
| 15 | Pay status counts | OwedTotal: 5×$0, 3×$100, 1×-$50 | `GET /filter-options` | PAID IN FULL(5), UNDER PAID(3), OVER PAID(1) |
| 16 | Team counts | Team A: 3 regs, Team B: 5 regs, Team C: 0 regs | `GET /filter-options` | Team A(3), Team B(5); Team C excluded (zero count) |
| 17 | Parallel execution performance | 1,000 registrations across all categories | `GET /filter-options` | Returns within 2 seconds (all 14 categories via Task.WhenAll) |

#### 1.3 Registration Detail — Profile & Financial Loading

| # | Test | Seed State | Action | Assert |
|---|------|-----------|--------|--------|
| 18 | Full detail load | Registration with user, team, 3 accounting records, family | `GET /{registrationId}` | All fields populated: profileValues, accountingRecords (3), familyContact, userDemographics |
| 19 | Profile metadata parsing | Job with PlayerProfileMetadataJson (5 fields) + Registration with values | `GET /{registrationId}` | `profileValues` dictionary has 5 entries matching metadata field names |
| 20 | Dynamic parent labels | Job with MomLabel="Mother", DadLabel="Father" | `GET /{registrationId}` | `momLabel="Mother"`, `dadLabel="Father"` |
| 21 | HasSubscription flag | Reg A: AdnSubscriptionId="123", Reg B: AdnSubscriptionId=null | `GET /{regAId}` and `GET /{regBId}` | A: `hasSubscription=true`, B: `hasSubscription=false` |
| 22 | Cross-job access blocked | Reg in Job A | `GET /{registrationId}` (with Job B token) | 404 or validation error |

```csharp
[Fact]
public async Task GetDetail_IncludesHasSubscriptionFlag()
{
    // Arrange: registration with AdnSubscriptionId = "ARB-123"
    // Act: GET /{registrationId}
    // Assert: response.HasSubscription == true
}
```

#### 1.4 CC Charge — Authorize.Net Integration (CRITICAL — Financial)

| # | Test | Seed State | Action | Assert |
|---|------|-----------|--------|--------|
| 23 | Successful CC charge | Reg with OwedTotal=$250, mock ADN returns success + txId | `POST /{regId}/charge-cc` amount=$100 | New RA record: Payamt=$100, AdnTransactionId set, AdnCc4 = last 4; Registration: PaidTotal += $100, OwedTotal = $150 |
| 24 | Full balance CC charge | Reg with OwedTotal=$250 | `POST /{regId}/charge-cc` amount=$250 | OwedTotal = $0, PaidTotal += $250 |
| 25 | Amount exceeds owed | Reg with OwedTotal=$100 | `POST /{regId}/charge-cc` amount=$150 | Rejected: `success=false`, error mentions exceeds owed; OwedTotal unchanged |
| 26 | Zero amount rejected | Reg with OwedTotal=$100 | `POST /{regId}/charge-cc` amount=$0 | Rejected: `success=false`; no RA record created |
| 27 | ADN gateway failure | Reg with OwedTotal=$250, mock ADN returns failure | `POST /{regId}/charge-cc` amount=$100 | `success=false`, RA record marked inactive (Payamt=0); Registration financials unchanged |
| 28 | Invoice number format | Customer AI=42, Job AI=7, Reg AI=1234 | `POST /{regId}/charge-cc` | RA record has invoice matching `42_7_1234` pattern (max 20 chars) |
| 29 | Invoice number truncation | Customer AI=123456789, Job AI=9876, Reg AI=5432 | `POST /{regId}/charge-cc` | Invoice truncated to 20 chars using fallback logic |

```csharp
[Fact]
public async Task ChargeCc_Success_UpdatesFinancialsAndCreatesRecord()
{
    // Arrange: reg with OwedTotal=$250, mock ADN_Charge returns success
    // Act: POST /{regId}/charge-cc with amount=$100
    // Assert: new RA record with Payamt=$100, AdnTransactionId populated
    // Assert: reg.PaidTotal increased by $100, reg.OwedTotal decreased to $150
}

[Fact]
public async Task ChargeCc_GatewayFailure_MarksRecordInactive()
{
    // Arrange: reg with OwedTotal=$250, mock ADN_Charge returns failure
    // Act: POST /{regId}/charge-cc with amount=$100
    // Assert: RA record exists but BActive=false, Payamt=0
    // Assert: reg.PaidTotal and reg.OwedTotal unchanged
}
```

#### 1.5 Check & Correction — Payment Recording (CRITICAL — Financial)

| # | Test | Seed State | Action | Assert |
|---|------|-----------|--------|--------|
| 30 | Record check payment | Reg with OwedTotal=$200 | `POST /{regId}/record-payment` type=Check, amount=$75, checkNo="1234" | New RA record: Payamt=$75, CheckNo="1234", PaymentMethodId=CheckMethodId; PaidTotal += $75, OwedTotal = $125 |
| 31 | Check with comment | Reg with OwedTotal=$200 | `POST /{regId}/record-payment` type=Check, amount=$50, comment="Partial payment" | RA record has Comment="Partial payment" |
| 32 | Check — zero amount rejected | Reg with OwedTotal=$200 | `POST /{regId}/record-payment` type=Check, amount=$0 | Rejected, no RA record |
| 33 | Correction — positive amount (credit) | Reg with OwedTotal=$200 | `POST /{regId}/record-payment` type=Correction, amount=$50, comment="Scholarship" | RA Payamt=$50, PaymentMethodId=CorrectionMethodId; OwedTotal = $150 |
| 34 | Correction — negative amount (penalty) | Reg with OwedTotal=$200 | `POST /{regId}/record-payment` type=Correction, amount=-$30, comment="Late fee" | RA Payamt=-$30; OwedTotal = $230 |
| 35 | Correction — zero amount rejected | Reg with OwedTotal=$200 | `POST /{regId}/record-payment` type=Correction, amount=$0 | Rejected, no RA record |
| 36 | Invalid payment type rejected | Reg | `POST /{regId}/record-payment` type="Invalid" | 400 Bad Request |
| 37 | Payment method GUID mapping | N/A | Record Check → assert PaymentMethodId | PaymentMethodId = `32ECA575-...` (CheckMethodId constant) |

```csharp
[Fact]
public async Task RecordCheckPayment_CreatesRecordAndUpdatesFinancials()
{
    // Arrange: reg with FeeTotal=$500, PaidTotal=$300, OwedTotal=$200
    // Act: POST /{regId}/record-payment with type=Check, amount=$75, checkNo="1234"
    // Assert: new RA record with Payamt=$75, CheckNo="1234"
    // Assert: reg.PaidTotal = $375, reg.OwedTotal = $125
}

[Fact]
public async Task RecordCorrection_NegativeAmount_IncreasesOwedTotal()
{
    // Arrange: reg with FeeTotal=$500, PaidTotal=$300, OwedTotal=$200
    // Act: POST /{regId}/record-payment with type=Correction, amount=-$30
    // Assert: RA Payamt=-$30; reg.PaidTotal = $270, reg.OwedTotal = $230
}
```

#### 1.6 Edit Accounting Record

| # | Test | Seed State | Action | Assert |
|---|------|-----------|--------|--------|
| 38 | Edit comment on check record | RA record (Check, Comment="old") | `PUT /accounting/{aId}` comment="new comment" | Comment updated, CheckNo unchanged |
| 39 | Edit check number | RA record (Check, CheckNo="111") | `PUT /accounting/{aId}` checkNo="222" | CheckNo updated to "222" |
| 40 | Edit both fields | RA record | `PUT /accounting/{aId}` comment="note", checkNo="333" | Both updated |
| 41 | Financial fields NOT changed | RA record (Payamt=$100) | `PUT /accounting/{aId}` comment="edit" | Payamt still $100 (financial fields immutable via edit) |
| 42 | Cross-job edit blocked | RA in Job A | `PUT /accounting/{aId}` (with Job B token) | 400 error, record unchanged |

```csharp
[Fact]
public async Task EditAccountingRecord_OnlyUpdatesCommentAndCheckNo()
{
    // Arrange: RA record with Payamt=$100, Comment="old", CheckNo="111"
    // Act: PUT /accounting/{aId} with comment="new", checkNo="222"
    // Assert: Comment="new", CheckNo="222", Payamt still $100
}
```

#### 1.7 Refund — Void vs Refund Logic (CRITICAL — Financial + ADN)

| # | Test | Seed State | Action | Assert |
|---|------|-----------|--------|--------|
| 43 | Refund settled transaction — full | CC RA (Payamt=$100), mock ADN status=`settledSuccessfully`, ADN_Refund returns success | `POST /refund` amount=$100 | New negative RA: Payamt=-$100, PaymentMethodId=CcCreditMethodId; PaidTotal -= $100, OwedTotal += $100 |
| 44 | Refund settled transaction — partial | CC RA (Payamt=$100), mock ADN status=`settledSuccessfully` | `POST /refund` amount=$40 | New negative RA: Payamt=-$40; PaidTotal -= $40, OwedTotal += $40 |
| 45 | Void pending transaction — full reversal | CC RA (Payamt=$100), mock ADN status=`capturedPendingSettlement`, ADN_Void returns success | `POST /refund` amount=$100 | Original RA: Comment suffixed "VOIDED", Payamt set to $0; PaidTotal -= $100, OwedTotal += $100; NO new negative RA created |
| 46 | Void — original record marked | Same as above | `POST /refund` | Original RA Comment contains "VOIDED", BActive remains true |
| 47 | Unsupported transaction status | CC RA, mock ADN status=`expired` | `POST /refund` | `success=false`, error mentions "does not support refund/void"; financials unchanged |
| 48 | ADN status lookup failure | CC RA, mock ADN_GetTransactionDetails throws | `POST /refund` | `success=false`, error mentions "Could not look up"; financials unchanged |
| 49 | ADN refund gateway failure | CC RA, mock status=`settledSuccessfully`, ADN_Refund returns failure | `POST /refund` | `success=false`, error with ADN message; no negative RA created; financials unchanged |
| 50 | Non-CC record rejected | RA with PaymentMethod="Check" (no AdnTransactionId) | `POST /refund` | Rejected — only CC records can be refunded |
| 51 | Refund exceeds original | CC RA (Payamt=$100) | `POST /refund` amount=$150 | Rejected, amount > original |

```csharp
[Fact]
public async Task ProcessRefund_SettledTransaction_CreatesNegativeRecord()
{
    // Arrange: CC RA with Payamt=$100
    // Mock: ADN_GetTransactionDetails returns settledSuccessfully
    // Mock: ADN_Refund returns success with transactionId
    // Act: POST /refund with amount=$100
    // Assert: new RA with Payamt=-$100, PaymentMethodId=CcCreditMethodId
    // Assert: reg.PaidTotal decreased by $100, reg.OwedTotal increased by $100
}

[Fact]
public async Task ProcessRefund_PendingTransaction_VoidsOriginalRecord()
{
    // Arrange: CC RA with Payamt=$100, Comment="CC charge"
    // Mock: ADN_GetTransactionDetails returns capturedPendingSettlement
    // Mock: ADN_Void returns success
    // Act: POST /refund with amount=$100
    // Assert: original RA: Payamt=$0, Comment contains "VOIDED"
    // Assert: NO new negative RA record created
    // Assert: reg.PaidTotal decreased by $100, reg.OwedTotal increased by $100
}
```

#### 1.8 Subscription View & Cancel

| # | Test | Seed State | Action | Assert |
|---|------|-----------|--------|--------|
| 52 | View subscription details | Reg with AdnSubscriptionId="ARB-123", mock ADN returns details | `GET /{regId}/subscription` | Returns SubscriptionDetailDto with status, amount, occurrences, startDate |
| 53 | View — no subscription | Reg with AdnSubscriptionId=null | `GET /{regId}/subscription` | 404 |
| 54 | Cancel subscription | Reg with AdnSubscriptionId="ARB-123", mock ADN cancel returns success | `POST /{regId}/cancel-subscription` | Registration.AdnSubscriptionStatus = "canceled"; ADN_CancelSubscription called |
| 55 | Cancel — already canceled | Reg with AdnSubscriptionStatus="canceled" | `POST /{regId}/cancel-subscription` | Graceful handling (idempotent or error) |
| 56 | Cancel — ADN failure | Mock ADN_CancelSubscription throws | `POST /{regId}/cancel-subscription` | Error returned, status unchanged |

```csharp
[Fact]
public async Task CancelSubscription_UpdatesStatusToCanceled()
{
    // Arrange: registration with AdnSubscriptionId = "ARB-123"
    // Mock: ADN_CancelSubscription returns success
    // Act: POST /{regId}/cancel-subscription
    // Assert: registration.AdnSubscriptionStatus == "canceled"
}
```

#### 1.9 Profile Update — Dynamic Field Mapping

| # | Test | Seed State | Action | Assert |
|---|------|-----------|--------|--------|
| 57 | Update text profile field | Reg with metadata field "SchoolName" → dbColumn "SchoolName" | `PUT /{regId}/profile` with `{"SchoolName":"Lincoln High"}` | Registration.SchoolName = "Lincoln High" |
| 58 | Update multiple fields | Reg with 3 metadata fields | `PUT /{regId}/profile` with 3 key-value pairs | All 3 columns updated |
| 59 | Invalid field name rejected | No metadata field "FakeColumn" | `PUT /{regId}/profile` with `{"FakeColumn":"value"}` | Validation error, no column written |
| 60 | Modified timestamp updated | Reg with old Modified date | `PUT /{regId}/profile` | Registration.Modified = now, LebUserId = caller |

#### 1.10 Family & Demographics Update

| # | Test | Seed State | Action | Assert |
|---|------|-----------|--------|--------|
| 61 | Update family contact | Family linked to reg | `PUT /{regId}/family` with updated mom email | Family.MomEmail updated |
| 62 | Update demographics | User linked to reg | `PUT /{regId}/demographics` with new DOB | User.DateOfBirth updated |
| 63 | Cross-job update blocked | Reg in Job A | `PUT /{regId}/family` (with Job B token) | 400/403, no update |

#### 1.11 Change Job

| # | Test | Seed State | Action | Assert |
|---|------|-----------|--------|--------|
| 64 | Job options — same customer only | Job A (Customer 1), Job B (Customer 1), Job C (Customer 2) | `GET /change-job-options` (from Job A) | Returns Job B only, not Job C |
| 65 | Change job — team match found | Reg in Job A on "Storm U14"; Job B has team "Storm U14" | `POST /{regId}/change-job` to Job B | Reg.JobId = Job B, Reg.AssignedTeamId = Job B's "Storm U14" team |
| 66 | Change job — no team match | Reg in Job A on "Storm U14"; Job B has no "Storm U14" | `POST /{regId}/change-job` to Job B | Reg.JobId = Job B, Reg.AssignedTeamId = null (cleared) |
| 67 | Change job — cross-customer blocked | Reg in Job A (Customer 1) | `POST /{regId}/change-job` to Job C (Customer 2) | Rejected |

#### 1.12 Delete Registration (CRITICAL — Data Integrity)

| # | Test | Seed State | Action | Assert |
|---|------|-----------|--------|--------|
| 68 | Delete clean Player registration | Player reg with no accounting, no store, no insurance; caller=Director | `DELETE /{regId}` | Registration removed from DB, device records cleaned up |
| 69 | Delete blocked — accounting records | Reg with 1 RegistrationAccounting record | `DELETE /{regId}` | 409 Conflict, reg still exists, message mentions "accounting records" |
| 70 | Delete blocked — store records | Reg with StoreCartBatchSkus.DirectToRegId | `DELETE /{regId}` | 409 Conflict, reg still exists, message mentions "store" |
| 71 | Delete blocked — insurance | Reg with RegsaverPolicyId set | `DELETE /{regId}` | 409 Conflict, reg still exists, message mentions "insurance" |
| 72 | Unassigned Adult — Superuser can delete | Unassigned Adult reg, caller=Superuser | `DELETE /{regId}` | Success, registration deleted |
| 73 | Unassigned Adult — Director blocked | Unassigned Adult reg, caller=Director | `DELETE /{regId}` | 403, reg still exists |
| 74 | Device cleanup on delete | Reg with 2 DeviceRegistrationId records | `DELETE /{regId}` | Both device records removed, then registration removed |

```csharp
[Fact]
public async Task DeleteRegistration_WithAccountingRecords_Returns409()
{
    // Arrange: registration with 1 RegistrationAccounting record
    // Act: DELETE /{regId}
    // Assert: 409 Conflict, registration still in DB
    // Assert: response message mentions "accounting records"
}

[Fact]
public async Task DeleteRegistration_UnassignedAdult_RequiresSuperuser()
{
    // Arrange: "Unassigned Adult" registration, no blocking records
    // Act: DELETE /{regId} with Director role in JWT
    // Assert: 403, registration still in DB

    // Act: DELETE /{regId} with Superuser role in JWT
    // Assert: 200, registration removed
}
```

#### 1.13 Batch Email

| # | Test | Seed State | Action | Assert |
|---|------|-----------|--------|--------|
| 75 | Send batch email — all succeed | 3 regs with valid emails | `POST /batch-email` with template | Sent=3, Failed=0 |
| 76 | Token substitution — !PERSON | Reg: "John Smith" | `POST /email-preview` with body="Dear !PERSON" | Rendered body contains "Dear John Smith" |
| 77 | Token substitution — !AMTOWED | Reg: OwedTotal=$250 | `POST /email-preview` with body="You owe !AMTOWED" | Rendered body contains "You owe $250.00" |
| 78 | Partial failure | 3 regs: 2 valid emails, 1 invalid | `POST /batch-email` | Sent=2, Failed=1, failedAddresses contains the invalid one |
| 79 | Cross-job reg IDs blocked | RegistrationIds from Job B | `POST /batch-email` (with Job A token) | Validation error or filtered out |

#### 1.14 Authorization — Job Scoping

| # | Test | Seed State | Action | Assert |
|---|------|-----------|--------|--------|
| 80 | All endpoints require AdminOnly policy | Non-admin user | Any endpoint | 403 Forbidden |
| 81 | Job scoping — all queries | Reg in Job A, Reg in Job B | Search from Job A | Only Job A regs returned |
| 82 | Detail — cross-job blocked | Reg in Job B | `GET /{regId}` (with Job A token) | 404 |
| 83 | Charge CC — cross-job blocked | Reg in Job B | `POST /{regId}/charge-cc` (with Job A token) | Validation error |

**Estimated effort**: ~83 tests, 5-7 days

---

## Layer 2: Frontend State Machine Tests (MEDIUM VALUE)

### Why This Layer Matters

The registration search component has complex signal orchestration: filter state → search → grid update → detail panel → accounting actions → financial refresh. If `onPaymentRecorded()` doesn't trigger a detail re-fetch, the admin sees stale OwedTotal. These tests verify the **orchestration logic**, not HTTP calls.

### Setup

- Angular `TestBed` with `RegistrationSearchService` mocked (return canned `of(...)` observables)
- `fakeAsync` + `tick()` for async signal propagation
- Assert on signal values, not DOM

### Test Cases

#### 2.1 Search & Grid State

| # | Test | Mock Setup | Action | Assert |
|---|------|-----------|--------|--------|
| 1 | Search populates results signal | `search()` returns 5 results | `component.doSearch()` | `searchResults()` has 5 items, `isSearching()` is false |
| 2 | Clear filters resets to defaults | Active filters | `component.clearFilters()` | `searchRequest()` matches defaults, `activeStatuses=["True"]` |
| 3 | Filter chip removal triggers re-search | Active "Role: Player" chip | `component.removeFilterChip(roleChip)` | `searchRequest().roleIds` is empty, search triggered |
| 4 | Dirty state detection | `lastSearchedRequest` set, then filter changes | Change a filter value | `isDirty()` returns true, "Update Results" button visible |

#### 2.2 Detail Panel State

| # | Test | Mock Setup | Action | Assert |
|---|------|-----------|--------|--------|
| 5 | Row click opens panel | `getRegistrationDetail()` returns detail | Click row | `isPanelOpen()` true, `selectedDetail()` populated |
| 6 | Panel close clears detail | Panel open with detail | `component.closePanel()` | `isPanelOpen()` false |
| 7 | Tab switch — accounting lazy-loads subscription | Detail with `hasSubscription=true` | `component.setActiveTab('accounting')` | `loadSubscription()` called |
| 8 | Tab switch — no subscription load when absent | Detail with `hasSubscription=false` | `component.setActiveTab('accounting')` | `loadSubscription()` NOT called |

#### 2.3 Payment Modal State

| # | Test | Mock Setup | Action | Assert |
|---|------|-----------|--------|--------|
| 9 | Open payment modal | Detail loaded with OwedTotal=$250 | `component.openPaymentModal()` | `showPaymentModal()` true, amount pre-filled to $250 |
| 10 | CC pre-fill from family contact | Detail with familyContact.momEmail="mom@test.com" | Open modal, select CC | `ccEmail()` = "mom@test.com" |
| 11 | CC pre-fill fallback to demographics | Detail without familyContact, demographics.email="user@test.com" | Open modal, select CC | `ccEmail()` = "user@test.com" |
| 12 | canSubmit — CC requires all fields | CC mode, some fields empty | Call `canSubmit()` | Returns false until card#, expiry, CVV, firstName, lastName filled |
| 13 | canSubmit — Check requires positive amount | Check mode, amount=0 | Call `canSubmit()` | Returns false; set amount=$50 → returns true |
| 14 | canSubmit — Correction allows negative | Correction mode, amount=-$30 | Call `canSubmit()` | Returns true (non-zero) |
| 15 | paymentRecorded event triggers refresh | `recordPayment()` returns success | Submit check | `paymentRecorded` emitted, parent calls `refreshAfterChange()` |

#### 2.4 Edit Accounting Record State

| # | Test | Mock Setup | Action | Assert |
|---|------|-----------|--------|--------|
| 16 | isEditable — Check record | RA with paymentMethod="Check" | `component.isEditable(record)` | Returns true |
| 17 | isEditable — CC record | RA with paymentMethod="Credit Card" | `component.isEditable(record)` | Returns false |
| 18 | Start edit populates signals | RA with Comment="old", CheckNo="123" | `component.startEditRecord(record)` | `editingAId()` = record.aId, `editComment()` = "old", `editCheckNo()` = "123" |
| 19 | Cancel edit clears state | Editing in progress | `component.cancelEditRecord()` | `editingAId()` = null |

#### 2.5 Subscription State

| # | Test | Mock Setup | Action | Assert |
|---|------|-----------|--------|--------|
| 20 | Load subscription populates signal | `getSubscription()` returns detail | `component.loadSubscription()` | `subscription()` populated, `isLoadingSubscription()` false |
| 21 | Cancel subscription updates status | `cancelSubscription()` returns success | `component.cancelSubscription()` | `subscription()` set to null or status="canceled", success toast |

#### 2.6 Refund Flow

| # | Test | Mock Setup | Action | Assert |
|---|------|-----------|--------|--------|
| 22 | Refund button emits record | CC RA with CanRefund=true | Click Refund | `refundRequested` emits the RA record |
| 23 | Refund complete triggers refresh | `processRefund()` returns success | Refund modal emits `refunded` | `refreshAfterChange()` called, grid re-searched, detail re-loaded |

#### 2.7 CC Input Formatting

| # | Test | Setup | Action | Assert |
|---|------|-------|--------|--------|
| 24 | Card number strips non-digits | N/A | `formatCcNumber("4111-1111-1111-1111")` | `ccNumber()` = "4111111111111111" |
| 25 | Card number max 16 digits | N/A | `formatCcNumber("41111111111111119999")` | `ccNumber()` = "4111111111111111" (truncated) |
| 26 | Expiry formats MM / YY | N/A | `formatExpiry("1225")` | `ccExpiry()` = "12 / 25" |
| 27 | CVV max 4 digits | N/A | `formatCvv("12345")` | `ccCvv()` = "1234" |

**Estimated effort**: ~27 tests, 2-3 days

---

## Layer 3: E2E Smoke Tests (HIGH VALUE — Do After Layer 1)

### Why This Layer Matters

These verify the full stack works together for the most critical admin journeys. If these pass, the financial management system works end-to-end.

### Setup

- Playwright
- Test database with known seed data (reset between tests)
- Run against real Angular app + real .NET backend
- ADN sandbox for CC operations (or mocked gateway)

### Test Cases

| # | Journey | Steps | Key Assertions |
|---|---------|-------|----------------|
| 1 | **Search and browse** | Navigate to Registration Search → enter name → click Search → results appear → click a row → detail panel slides in | Grid populated with matching results, detail panel shows correct registration |
| 2 | **Record check payment** | Open detail panel for reg with OwedTotal > 0 → click "Add Payment Record" → select Check → enter amount + check# → Submit | Accounting record appears in table, OwedTotal decremented, toast shown |
| 3 | **Record correction** | Same as above but select Correction → enter negative amount → Submit | OwedTotal increases, accounting record shows negative Payamt |
| 4 | **CC charge** (sandbox) | Open payment modal → select Credit Card → fill card form with test card → Submit | ADN sandbox charge succeeds, accounting record with TransactionId appears |
| 5 | **CC refund** (sandbox) | Select a CC payment row → click Refund → enter amount → Process | Negative accounting record appears, OwedTotal increases |
| 6 | **Edit accounting record** | Select a Check row → click Edit icon → modify comment → Save | Comment updated, toast confirms |
| 7 | **Batch email send** | Search → select 3 rows → click Email Selected → compose email with !PERSON token → Preview → Send | Preview shows rendered names, send result shows 3 sent |
| 8 | **Change job** | Open detail panel → click Change Job → select target job → confirm | Panel shows new job, grid refreshes |
| 9 | **Delete registration** | Open detail panel for clean Player reg → click Delete → confirm | Panel closes, registration no longer in search results |
| 10 | **Delete blocked** | Open detail panel for reg with accounting records → click Delete | Error message shown, registration still present |

### Smoke Test Variants (run in CI)

| # | Scenario | Purpose |
|---|----------|---------|
| 11 | Load grid with 2,000+ registrations | Performance sanity — grid loads within 5 seconds, paging works |
| 12 | Mobile viewport (375px) | Quick Lookup mode activates, desktop grid hidden |
| 13 | Subscription view + cancel | Reg with ARB → Accounting tab shows subscription → Cancel → status updates |
| 14 | All 8 palettes — modal rendering | Open payment modal → switch palettes → no broken colors |

**Estimated effort**: 14 tests, 3-4 days (including Playwright setup)

---

## Implementation Priority

```
Phase 1 (Week 1):  Backend tests 1.4–1.5     → 15 tests  (CC charge + check/correction — financial core)
Phase 2 (Week 1):  Backend tests 1.7          → 9 tests   (void vs refund — ADN integration)
Phase 3 (Week 1):  Backend tests 1.1–1.2      → 17 tests  (search + filter options)
Phase 4 (Week 2):  Backend tests 1.3, 1.6     → 10 tests  (detail loading + edit record)
Phase 5 (Week 2):  Backend tests 1.8–1.14     → 32 tests  (subscription, profile, family, job, delete, email, auth)
Phase 6 (Week 2):  Frontend tests 2.1–2.7     → 27 tests  (signal orchestration)
Phase 7 (Week 3):  E2E tests 3.1–3.14         → 14 tests  (full-stack journeys)
```

**Total: ~124 tests across 3 layers**

---

## Infrastructure Decisions (To Be Made Before Implementation)

| Decision | Options | Recommendation |
|----------|---------|----------------|
| Backend test DB | In-memory SQLite vs SQL Server LocalDB | SQLite for speed; SQL Server if testing complex joins |
| ADN mocking | Mock `IAdnApiService` interface vs sandbox | Mock for unit tests (deterministic); sandbox for E2E |
| Test data seeding | Per-test setup vs shared fixtures | Per-test for isolation (financial tests MUST be isolated) |
| E2E framework | Playwright vs Cypress | Playwright (consistent with 004 decision) |
| E2E test CC data | ADN sandbox test cards | Use ADN sandbox card numbers (4111111111111111) |
| CI integration | Every PR vs nightly | Backend + frontend tests on PR; E2E nightly |

---

## Files That Will Be Created

| File | Layer | Purpose | Est. LOC |
|------|-------|---------|----------|
| `Tests/Integration/RegSearch/SearchFilterTests.cs` | Backend | Multi-criteria search + filter options | 300 |
| `Tests/Integration/RegSearch/CcChargeTests.cs` | Backend | CC charge via ADN | 200 |
| `Tests/Integration/RegSearch/CheckCorrectionTests.cs` | Backend | Check + correction payment recording | 180 |
| `Tests/Integration/RegSearch/RefundVoidTests.cs` | Backend | Refund (settled) vs void (pending) logic | 250 |
| `Tests/Integration/RegSearch/EditRecordTests.cs` | Backend | Edit accounting comment/checkNo | 100 |
| `Tests/Integration/RegSearch/SubscriptionTests.cs` | Backend | ARB subscription view + cancel | 120 |
| `Tests/Integration/RegSearch/ProfileUpdateTests.cs` | Backend | Dynamic profile field mapping | 100 |
| `Tests/Integration/RegSearch/ChangeJobTests.cs` | Backend | Cross-job transfer with team matching | 120 |
| `Tests/Integration/RegSearch/DeleteRegistrationTests.cs` | Backend | Role-based delete with pre-conditions | 180 |
| `Tests/Integration/RegSearch/BatchEmailTests.cs` | Backend | Batch email with token substitution | 120 |
| `Tests/Integration/RegSearch/AuthorizationTests.cs` | Backend | AdminOnly policy + job scoping | 80 |
| `registration-search.component.spec.ts` | Frontend | Search + detail + modal signal state tests | 300 |
| `add-payment-modal.component.spec.ts` | Frontend | Payment modal validation + formatting | 150 |
| `e2e/registration-search.spec.ts` | E2E | Full-stack admin payment journeys | 400 |

**Total estimated**: ~2,600 LOC of test code

---

## Success Metrics

- ✅ All 83 backend tests pass against a fresh database
- ✅ All 27 frontend state tests pass with mocked services
- ✅ All 14 E2E smoke tests pass end-to-end
- ✅ Zero false positives (no tests that break on irrelevant changes)
- ✅ Backend tests run in < 60 seconds (fast feedback loop)
- ✅ Financial tests verify BOTH accounting record AND registration totals (dual assertion)
- ✅ Void vs refund path clearly distinguished by transaction status mock
- ✅ E2E tests run in < 3 minutes

---

## Risks & Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| ADN sandbox instability | E2E CC tests flaky | Mock ADN in backend tests; use sandbox only in E2E with retry |
| Financial rounding errors | Penny-off discrepancies | Use `decimal` assertions with exact comparison (not float) |
| Test database schema drift | Tests fail after migrations | Run migrations in test setup; share schema with production |
| Concurrent financial updates | Race condition on PaidTotal/OwedTotal | Test with concurrent requests in Layer 1 (optimistic concurrency) |
| Mock ADN doesn't catch real gateway issues | False confidence | E2E with sandbox as safety net; backend mocks for speed |
| Flaky E2E tests (timing) | False failures | Playwright auto-wait; no `sleep()`; retry once on CI |

---

## Amendments Log

| Date | Amendment | Rationale |
|------|-----------|-----------|
| 2026-02-12 | Initial testing plan created | Comprehensive test coverage for Search/Registrations migration (Phase 1 + Phase 2 accounting management) |
