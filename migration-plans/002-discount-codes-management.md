# Migration Plan: JobDiscountCodes/Admin → Discount Codes Management

## Context

The legacy TSIC-Unify-2024 project has a `JobDiscountCodes/Admin` page that manages discount codes
(promo codes) for player/team registrations within a specific job. It uses jqGrid for inline editing
with immediate server updates. We are modernizing this as an Angular component with a clean API,
following patterns established in the Administrator Management module.

---

## 1. Legacy Pain Points

- **jqGrid dependency** - Heavy jQuery plugin, dated look, poor mobile experience
- **Inline editing with confusing UX** - Edit mode unclear, no validation feedback before save
- **No batch operations** - Must edit codes one at a time
- **No bulk import** - Creating many codes requires manual repetition
- **Percentage vs dollar confusion** - Both use numeric input, easy to enter wrong type
- **Expiration date confusion** - Unclear timezone handling, no date picker
- **No usage tracking visibility** - Can't see if code has been used without SQL query
- **Duplicate code prevention only at save** - User wastes time entering duplicate before error
- **Anti-forgery token plumbing** - Boilerplate in every AJAX call

## 2. Modern Vision

A clean, card-based discount code management page with:
- **Responsive data table** with code, type badge, amount, usage counters, expiration, status
- **Add modal with validation** - Clear form with type selector, date picker, max usage limits
- **Edit modal** - All fields editable except usage counters (historical data preserved)
- **Multi-select for batch operations** - Activate/deactivate/delete multiple codes at once
- **Bulk code generation** - Create multiple similar codes with sequential numbering
- **Real-time duplicate checking** - Warn user before they submit
- **Usage analytics** - Show used count vs max uses, visual indicators for exhausted codes
- **Instant feedback** - Toast notifications, optimistic UI updates

## 3. User Value

- **Fewer errors**: Date picker prevents invalid dates, type selector prevents wrong discount type
- **Faster workflows**: Bulk generation creates 100s of codes in seconds vs manual repetition
- **Better insights**: Usage counters show which codes are popular, which are unused
- **Mobile access**: Manage codes from any device
- **Reduced support burden**: Clear expiration status prevents "why didn't my code work?" calls

## 4. Design Alignment

- Bootstrap table + CSS variables (all 8 palettes)
- `TsicDialogComponent` for modals
- Signal-based state, OnPush change detection
- Toast notifications via existing `ToastService`
- Confirmation dialog pattern from Administrator Management
- WCAG AA compliant (contrast, focus management, ARIA labels)

## 5. UI Standards Created / Employed

### CREATED (new patterns this module introduces)
- **Discount Code Table** - Table with type badges ($ vs %), usage progress bars, expiration chips
- **Code Generation Modal** - Pattern for creating multiple similar items with sequential IDs
- **Duplicate Detection** - Real-time validation pattern that checks server before submit
- **Usage Progress Indicator** - Visual representation of code usage (3/10 uses, etc.)
- **Expiration Status Chip** - Color-coded chip showing expired/active/upcoming expiration

### EMPLOYED (existing patterns reused)
- `TsicDialogComponent` for modals
- `ConfirmDialogComponent` for destructive actions
- `ToastService` for success/error feedback
- Signal-based state management
- Multi-select with contextual toolbar (from Administrator Management)
- CSS variable design system tokens
- `@if` / `@for` template syntax
- OnPush change detection

---

## 6. Security Requirements

**CRITICAL**: This module must derive `jobId` from JWT claims, NOT from route parameters.

- **Route**: `/:jobPath/admin/discount-codes` (jobPath for routing only)
- **API Endpoints**: Must use `ClaimsPrincipalExtensions.GetJobIdFromRegistrationAsync()` to derive `jobId` from the authenticated user's `regId` claim
- **NO route parameters containing sensitive IDs**: All `[Authorize]` endpoints must extract job context from JWT token
- **Policy**: `[Authorize(Policy = "AdminOnly")]` - Directors, SuperDirectors, and Superusers can manage codes
- **Validation**: Server must verify the discount code belongs to the user's job before any CRUD operation

**Pattern**:
```csharp
[HttpGet]
[Authorize(Policy = "AdminOnly")]
public async Task<ActionResult<List<DiscountCodeDto>>> GetDiscountCodes()
{
    var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
    if (jobId == null)
        return BadRequest(new { message = "Registration context required" });
    
    var codes = await _discountCodeService.GetDiscountCodesAsync(jobId.Value);
    return Ok(codes);
}
```

---

## 7. Implementation Steps

### Step 1: Backend - Repository Interface & Implementation
**Status**: [ ] Pending
**Files to create**:
- `TSIC.Contracts/Repositories/IDiscountCodeRepository.cs`
- `TSIC.Infrastructure/Repositories/DiscountCodeRepository.cs`

**Details**:
- Entity: `JobDiscountCode` (existing entity)
- `GetByJobIdAsync(Guid jobId)` - Returns all discount codes for job, `AsNoTracking()`, includes usage counts
- `GetByIdAsync(Guid discountCodeId)` - Single code (tracked, for updates)
- `GetByCodeAndJobAsync(string code, Guid jobId)` - For duplicate checking
- `GetUsageCountAsync(Guid discountCodeId)` - Count of registrations using this code
- `Add(JobDiscountCode code)` - Add new code
- `Remove(JobDiscountCode code)` - Delete code
- `SaveChangesAsync()` - Persist changes
- Query optimization: Project usage counts via join to avoid N+1 queries

### Step 2: Backend - Service Interface & Implementation
**Status**: [ ] Pending
**Files to create**:
- `TSIC.Contracts/Services/IDiscountCodeService.cs`
- `TSIC.Application/Services/DiscountCode/DiscountCodeService.cs`

**Details**:
- `GetDiscountCodesAsync(Guid jobId)` → `List<DiscountCodeDto>`
  - Returns all codes with computed status (active/expired), usage stats
- `AddDiscountCodeAsync(Guid jobId, AddDiscountCodeRequest request)` → `DiscountCodeDto`
  - Validate code uniqueness within job
  - Set JobDiscountCodeId = Guid.NewGuid()
  - Set created/modified metadata
- `BulkAddDiscountCodesAsync(Guid jobId, BulkAddDiscountCodeRequest request)` → `List<DiscountCodeDto>`
  - Generate multiple codes with pattern: `{prefix}{001..100}{suffix}`
  - Validate none already exist (all-or-nothing)
  - Return list of created codes
- `UpdateDiscountCodeAsync(Guid codeId, UpdateDiscountCodeRequest request)` → `DiscountCodeDto`
  - Validate code still belongs to user's job
  - Cannot edit Code or UsageCount (historical integrity)
  - Can edit: Amount, DiscountType, ExpirationDate, MaxUses, BActive
- `DeleteDiscountCodeAsync(Guid codeId)` → bool
  - Prevent deletion if code has been used (UsageCount > 0)
- `BatchUpdateStatusAsync(Guid jobId, List<Guid> codeIds, bool isActive)` → int (count updated)
  - Only updates codes that belong to the job
- `CheckCodeExistsAsync(Guid jobId, string code)` → bool
  - For real-time duplicate validation

### Step 3: Backend - DTOs
**Status**: [ ] Pending
**Files to create**:
- `TSIC.Contracts/Dtos/DiscountCode/DiscountCodeDto.cs`
- `TSIC.Contracts/Dtos/DiscountCode/AddDiscountCodeRequest.cs`
- `TSIC.Contracts/Dtos/DiscountCode/BulkAddDiscountCodeRequest.cs`
- `TSIC.Contracts/Dtos/DiscountCode/UpdateDiscountCodeRequest.cs`
- `TSIC.Contracts/Dtos/DiscountCode/BatchUpdateStatusRequest.cs`
- `TSIC.Contracts/Dtos/DiscountCode/CheckCodeExistsRequest.cs`

**DiscountCodeDto**:
```csharp
public record DiscountCodeDto
{
    public required Guid JobDiscountCodeId { get; init; }
    public required string Code { get; init; }
    public required string DiscountType { get; init; } // "Percentage" or "DollarAmount"
    public required decimal Amount { get; init; }
    public int? MaxUses { get; init; }
    public required int UsageCount { get; init; }
    public DateTime? ExpirationDate { get; init; }
    public required bool IsActive { get; init; }
    public required bool IsExpired { get; init; } // Computed
    public required bool IsExhausted { get; init; } // Computed: UsageCount >= MaxUses
    public required DateTime Created { get; init; }
    public DateTime? Modified { get; init; }
}
```

**AddDiscountCodeRequest**:
```csharp
public record AddDiscountCodeRequest
{
    public required string Code { get; init; }
    public required string DiscountType { get; init; } // "Percentage" or "DollarAmount"
    public required decimal Amount { get; init; }
    public int? MaxUses { get; init; }
    public DateTime? ExpirationDate { get; init; }
}
```

**BulkAddDiscountCodeRequest**:
```csharp
public record BulkAddDiscountCodeRequest
{
    public required string Prefix { get; init; }
    public required string Suffix { get; init; }
    public required int StartNumber { get; init; }
    public required int Count { get; init; }
    public required string DiscountType { get; init; }
    public required decimal Amount { get; init; }
    public int? MaxUses { get; init; }
    public DateTime? ExpirationDate { get; init; }
}
```

**UpdateDiscountCodeRequest**:
```csharp
public record UpdateDiscountCodeRequest
{
    public required string DiscountType { get; init; }
    public required decimal Amount { get; init; }
    public int? MaxUses { get; init; }
    public DateTime? ExpirationDate { get; init; }
    public required bool IsActive { get; init; }
}
```

### Step 4: Backend - Controller
**Status**: [ ] Pending
**Files to create**:
- `TSIC.API/Controllers/DiscountCodesController.cs`

**Endpoints** (all use `GetJobIdFromRegistrationAsync` - NO jobId parameters):
- `GET    api/discount-codes` → List discount codes `[Authorize(Policy = "AdminOnly")]`
- `POST   api/discount-codes` → Add single code
- `POST   api/discount-codes/bulk` → Bulk generate codes
- `PUT    api/discount-codes/{codeId}` → Update code
- `DELETE api/discount-codes/{codeId}` → Delete code
- `POST   api/discount-codes/batch-status` → Batch activate/deactivate
- `GET    api/discount-codes/check-exists/{code}` → Check if code exists

**CRITICAL**: Controller must inject `IJobLookupService` and use:
```csharp
var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
```

### Step 5: Backend - DI Registration
**Status**: [ ] Pending
**Files to modify**:
- `TSIC.API/Program.cs` - Add `AddScoped` for repository and service

### Step 6: Backend - Regenerate API Models
**Status**: [ ] Pending
**Action**: Run `.\scripts\2-Regenerate-API-Models.ps1` to generate TypeScript types from the new DTOs

### Step 7: Frontend - Discount Code Service
**Status**: [ ] Pending
**Files to create**:
- `src/app/views/admin/discount-codes/services/discount-code.service.ts`

**Methods** (all return Observables, NO jobId parameters - API derives from token):
- `getDiscountCodes()`
- `addDiscountCode(request: AddDiscountCodeRequest)`
- `bulkAddDiscountCodes(request: BulkAddDiscountCodeRequest)`
- `updateDiscountCode(codeId: string, request: UpdateDiscountCodeRequest)`
- `deleteDiscountCode(codeId: string)`
- `batchUpdateStatus(codeIds: string[], isActive: boolean)`
- `checkCodeExists(code: string)` - debounced for real-time validation

### Step 8: Frontend - Main Component (Table + Toolbar)
**Status**: [ ] Pending
**Files to create**:
- `src/app/views/admin/discount-codes/discount-codes.component.ts`
- `src/app/views/admin/discount-codes/discount-codes.component.html`
- `src/app/views/admin/discount-codes/discount-codes.component.scss`

**Features**:
- Page header with title + "Add Code" button + "Bulk Generate" button
- Responsive Bootstrap table with columns:
  - Checkbox
  - Code
  - Type (badge: green for $, blue for %)
  - Amount (formatted with $ or % suffix)
  - Usage (progress bar: 3/10, with visual exhausted indicator)
  - Expiration (chip: active/expired/none)
  - Status (active/inactive chip)
  - Actions (edit/delete icons)
- Multi-select checkboxes with "select all"
- Contextual toolbar: "{n} selected | Activate | Deactivate | Delete"
- Used codes cannot be deleted (greyed out delete button with tooltip)
- Signal-based state: `codes`, `selectedIds`, `isLoading`, `errorMessage`
- Computed: `hasSelection`, `selectedCount`, `hasUsedCodesSelected` (for delete validation)

### Step 9: Frontend - Add/Edit Modal Component
**Status**: [ ] Pending
**Files to create**:
- `src/app/views/admin/discount-codes/components/code-form-modal.component.ts`

**Features**:
- Uses `TsicDialogComponent` wrapper
- Add mode:
  - Code input with real-time duplicate checking (displays inline error if exists)
  - Discount Type radio buttons: Dollar Amount ($) vs Percentage (%)
  - Amount input (numeric, validation: 0-100 for %, 0-999999 for $)
  - Max Uses input (optional, numeric)
  - Expiration Date picker (optional, must be future date)
- Edit mode:
  - Code (read-only, greyed out)
  - All other fields editable
  - Usage Count (read-only, display only)
- Form validation with inline error messages
- Save/Cancel buttons

### Step 10: Frontend - Bulk Generation Modal Component
**Status**: [ ] Pending
**Files to create**:
- `src/app/views/admin/discount-codes/components/bulk-code-modal.component.ts`

**Features**:
- Uses `TsicDialogComponent` wrapper
- Prefix input (optional, e.g., "SUMMER")
- Suffix input (optional, e.g., "2026")
- Start Number input (e.g., 1)
- Count input (e.g., 100) - validates max 500 to prevent abuse
- Preview: "Generated codes will be: SUMMER001_2026, SUMMER002_2026, ... SUMMER100_2026"
- Shared fields: Discount Type, Amount, Max Uses, Expiration Date
- Warning: "This will create {n} codes. Continue?"
- Generate/Cancel buttons

### Step 11: Frontend - Routing
**Status**: [ ] Pending
**Files to modify**:
- App routing config - add route `/:jobPath/admin/discount-codes` → `DiscountCodesComponent`
- Route guard: `requirePhase2: true` (requires authenticated user with job context)

### Step 12: Styling & Polish
**Status**: [ ] Pending
**Details**:
- All colors via CSS variables
- Type badges: `bg-success-subtle` for $, `bg-info-subtle` for %
- Usage progress bars: green when under 50%, yellow 50-80%, red 80-100%, grey if exhausted
- Expiration chips: red for expired, yellow for expiring soon (<7 days), green for active, grey for none
- Status chips: green for active, muted for inactive
- Hover states on table rows
- Smooth transitions for toolbar appear/disappear
- Mobile: table scrolls horizontally, important columns sticky
- Test all 8 palettes

---

## 8. Testing Strategy

### Unit Tests (Recommended)

**Backend Service Tests** (`DiscountCodeServiceTests.cs`):
```csharp
// Test: BulkAddDiscountCodesAsync prevents duplicate generation
[Fact]
public async Task BulkAddDiscountCodes_WithExistingCode_ThrowsValidationException()
{
    // Arrange: Seed DB with "SUMMER001"
    // Act: Bulk generate SUMMER001-SUMMER100
    // Assert: Exception thrown, zero codes created (all-or-nothing)
}

// Test: Deletion blocked for used codes
[Fact]
public async Task DeleteDiscountCode_WithUsageCount_ThrowsInvalidOperationException()
{
    // Arrange: Create code with UsageCount = 5
    // Act: DeleteDiscountCodeAsync
    // Assert: Exception thrown, code still exists
}

// Test: Expired code detection
[Fact]
public async Task GetDiscountCodes_WithExpiredDate_ReturnsIsExpiredTrue()
{
    // Arrange: Create code with ExpirationDate = yesterday
    // Act: GetDiscountCodesAsync
    // Assert: DTO.IsExpired == true
}
```

**Backend Repository Tests** (`DiscountCodeRepositoryTests.cs`):
```csharp
// Test: GetUsageCountAsync returns correct count
[Fact]
public async Task GetUsageCount_WithMultipleRegistrations_ReturnsCorrectCount()
{
    // Arrange: Seed 3 registrations using code
    // Act: GetUsageCountAsync
    // Assert: Count == 3
}
```

**Why these tests?**:
- **Bulk generation all-or-nothing**: Critical business rule (prevents partial data corruption)
- **Deletion protection**: Prevents data loss, important for audit trails
- **Expiration logic**: Complex computed property, easy to get wrong
- **Usage counting**: Performance-sensitive (N+1 query risk), verify optimization works

### Integration Tests (Recommended)

**API Endpoint Tests** (`DiscountCodesControllerTests.cs`):
```csharp
// Test: Endpoint derives jobId from token, rejects invalid token
[Fact]
public async Task GetDiscountCodes_WithoutRegIdClaim_Returns400BadRequest()
{
    // Arrange: Mock User.GetJobIdFromRegistrationAsync returns null
    // Act: GET api/discount-codes
    // Assert: 400 BadRequest, message contains "Registration context required"
}

// Test: Code check endpoint returns correct existence
[Fact]
public async Task CheckCodeExists_WithExistingCode_ReturnsTrue()
{
    // Arrange: Seed code "TEST123"
    // Act: GET api/discount-codes/check-exists/TEST123
    // Assert: Response body { exists: true }
}
```

**Why these tests?**:
- **JWT claim extraction**: Security-critical, must fail gracefully if token malformed
- **Duplicate checking**: High-value feature, needs to work reliably for UX

### Frontend Tests (NOT Recommended - Low Value)

**SKIP** these tests (waste of time for this module):
- ❌ Component rendering tests (brittle, low value for CRUD UI)
- ❌ Form validation tests (already tested at API level)
- ❌ Service HTTP call mocks (just testing Angular HttpClient, not our logic)
- ❌ Signal state management tests (trivial, low bug risk)

**ONLY test if**:
- Complex computed logic in component (we don't have any here)
- Custom form validation (we use standard validators)
- Novel UI patterns (we're reusing established patterns)

---

## 9. Dependencies

- Existing `TsicDialogComponent` (shared-ui)
- Existing `ConfirmDialogComponent` (shared-ui)
- Existing `ToastService` (shared-ui)
- Existing `ClaimsPrincipalExtensions.GetJobIdFromRegistrationAsync` (API/Extensions)
- Existing `IJobLookupService` (API/Services)
- Existing `JobDiscountCode` entity (Domain)
- Existing "AdminOnly" authorization policy

## 10. Verification Checklist

- [ ] Backend builds (`dotnet build`)
- [ ] Unit tests pass (`dotnet test`)
- [ ] API endpoints respond correctly (test via Swagger)
- [ ] **Security**: All endpoints derive jobId from JWT token (NO route parameters)
- [ ] TypeScript models generated (run regeneration script)
- [ ] Frontend compiles (`ng build`)
- [ ] Table loads discount codes for current job
- [ ] Add code with duplicate checking works
- [ ] Edit code updates all fields correctly
- [ ] Cannot delete code with usage > 0
- [ ] Bulk generation creates correct number of codes with pattern
- [ ] Multi-select + batch activate/deactivate works
- [ ] Expiration status displays correctly (expired/active/none)
- [ ] Usage progress bars render correctly (colors, exhausted state)
- [ ] Toast notifications on success/error
- [ ] Responsive layout on mobile viewport
- [ ] Test with all 8 color palettes

---

## 11. Migration Notes

**Database**: No schema changes needed - `JobDiscountCode` table already exists

**Legacy endpoint removal**: After verification, remove:
- `~/Views/JobDiscountCode/Admin.cshtml`
- `JobDiscountCodeController.Admin()` action (legacy MVC)
- Related jqGrid JavaScript files

**Data migration**: None required - existing discount codes work as-is

**User communication**: Update documentation to point to new URL pattern
