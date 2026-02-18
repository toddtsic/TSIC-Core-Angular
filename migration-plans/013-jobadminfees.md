# 013 — Job Admin Fees

> **Status**: Design Spec — Ready for Review
> **Date**: 2026-02-18
> **Keyword**: `CWCC Implement JOB-ADMIN-FEES`
> **Legacy reference**: `reference/TSIC-Unify-2024/TSIC-Unify/Controllers/Admin/JobAdminFeesController.cs`
> **Legacy view**: `JobAdminFees/Index` — jqGrid CRUD for per-job admin charges

---

## 1. Problem Statement

The legacy Job Admin Fees page lets SuperUsers record per-job administrative charges (chargebacks, scheduling fees, client refunds, etc.) against a specific job. The current implementation suffers from:

| Issue | Detail |
|---|---|
| **Repository violation** | Controller accesses `SqlDbContext` directly — zero separation of concerns |
| **No DTO layer** | `Load()` returns raw `JobAdminCharges` entities as JSON — leaks EF internals |
| **jqGrid dependency** | jQuery-based inline CRUD with `oper` string switching (`add`/`edit`/`del`) |
| **No validation** | No server-side validation beyond EF constraints — negative amounts, future dates, and orphan type IDs all accepted |
| **No API surface** | MVC-only — no REST endpoints for mobile or external consumers |

**Goal**: Replace with a clean Angular + Web API implementation following repository pattern, proper DTOs, and modern UX.

---

## 2. Scope

### In Scope
- Full CRUD for `JobAdminCharges` records scoped to the active job
- Lookup endpoint for `JobAdminChargeTypes` (database-driven dropdown)
- Server-side validation (required fields, positive amounts, valid type/year/month)
- Angular table with modal-based add/edit and inline delete
- SuperUser-only access (matches legacy `[Authorize(Policy = "SuperUserOnly")]`)

### Out of Scope
- Editing charge type definitions (reference data — managed elsewhere)
- Reporting / aggregation of charges across jobs
- Audit trail (deferred — no legacy equivalent exists)

---

## 3. Database Schema

Both tables already exist in the domain model and `SqlDbContext`. **No migrations needed.**

| Table | Schema | Relationship | Notes |
|---|---|---|---|
| `JobAdminCharges` | `adn` | 1:many from `Jobs` (FK `JobId`) | Main data table |
| `JobAdminChargeTypes` | `reference` | Lookup (FK `ChargeTypeId`) | ~7 rows: chargeback amount, chargeback admin fee, scheduling fee, TEAMS mobile app fee, client refund, client charge, chargeback reversal |

### Existing Entities

**`TSIC.Domain/Entities/JobAdminCharges.cs`**:
```
Id (int PK), ChargeTypeId (int FK), ChargeAmount (decimal/money),
Comment (string?), JobId (Guid FK), Year (int), Month (int), CreateDate (DateTime)
```

**`TSIC.Domain/Entities/JobAdminChargeTypes.cs`**:
```
Id (int PK), Name (string? — NVARCHAR(20))
```

---

## 4. Design Decisions

### 4a. API Shape — Separate Endpoints vs Single Edit Endpoint

| Option | Pro | Con |
|---|---|---|
| **Separate REST endpoints** (chosen) | Standard REST, clear intent, proper HTTP verbs | More endpoints to define |
| Single `Edit` with `oper` param (legacy) | Less code | Anti-pattern, unclear semantics, not RESTful |

**Decision**: Standard REST. `GET` to list, `POST` to create, `PUT` to update, `DELETE` to remove.

### 4b. Frontend CRUD UX — Inline Edit vs Modal

| Option | Pro | Con |
|---|---|---|
| Inline table edit | Fewer clicks for power users | Complex state management, harder validation UX |
| **Modal add/edit** (chosen) | Clean form validation, consistent with existing admin patterns (widget editor) | Extra click to open modal |

**Decision**: Modal for add/edit using `TsicDialogComponent` wrapper. Delete with confirmation prompt. Consistent with widget editor pattern.

### 4c. Year Dropdown — Dynamic vs Static

Legacy provides current year + prior year only. This is restrictive — a charge created in January may reference December of the prior year, and auditors may need older entries.

**Decision**: Default dropdown to current year ± 1 (matching legacy), but accept any reasonable year on the backend (2020–2099). The frontend will show current year and prior year as the default options but won't reject other values already stored.

### 4d. CreateDate Field — User-Editable vs Auto-Set

Legacy allows the user to set `CreateDate` via a datetime picker. This is intentional — the "Created" date represents when the charge was incurred, not when the DB row was inserted.

**Decision**: Preserve user-editable `CreateDate` (labeled "Charge Date" in UI for clarity). Default to current date/time on add.

---

## 5. Backend Architecture

### 5a. DTOs

**File**: `TSIC.Contracts/Dtos/JobAdminFees/JobAdminFeeDtos.cs`

```csharp
// --- Response DTOs ---

public record JobAdminFeeDto
{
    public required int Id { get; init; }
    public required int ChargeTypeId { get; init; }
    public required string ChargeTypeName { get; init; }
    public required decimal ChargeAmount { get; init; }
    public string? Comment { get; init; }
    public required int Year { get; init; }
    public required int Month { get; init; }
    public required DateTime CreateDate { get; init; }
}

public record ChargeTypeDto
{
    public required int Id { get; init; }
    public required string Name { get; init; }
}

// --- Request DTOs ---

public record CreateJobAdminFeeRequest
{
    public required int ChargeTypeId { get; init; }
    public required decimal ChargeAmount { get; init; }
    public string? Comment { get; init; }
    public required int Year { get; init; }
    public required int Month { get; init; }
    public required DateTime CreateDate { get; init; }
}

public record UpdateJobAdminFeeRequest
{
    public required int ChargeTypeId { get; init; }
    public required decimal ChargeAmount { get; init; }
    public string? Comment { get; init; }
    public required int Year { get; init; }
    public required int Month { get; init; }
    public required DateTime CreateDate { get; init; }
}
```

### 5b. Repository Interface

**File**: `TSIC.Contracts/Repositories/IJobAdminFeeRepository.cs`

```csharp
public interface IJobAdminFeeRepository
{
    // Read (AsNoTracking)
    Task<List<JobAdminFeeDto>> GetFeesForJobAsync(Guid jobId, CancellationToken ct);
    Task<List<ChargeTypeDto>> GetChargeTypesAsync(CancellationToken ct);
    Task<JobAdminCharges?> GetFeeByIdAsync(int id, CancellationToken ct);
    Task<bool> ChargeTypeExistsAsync(int chargeTypeId, CancellationToken ct);

    // Write
    void AddFee(JobAdminCharges fee);
    void RemoveFee(JobAdminCharges fee);

    // Persistence
    Task<int> SaveChangesAsync(CancellationToken ct);
}
```

### 5c. Repository Implementation

**File**: `TSIC.Infrastructure/Repositories/JobAdminFeeRepository.cs`

Key implementation notes:
- `GetFeesForJobAsync` — `AsNoTracking()`, joins `ChargeType` to project `ChargeTypeName`, ordered by `CreateDate` descending (newest first — more useful than legacy's ascending)
- `GetChargeTypesAsync` — `AsNoTracking()`, ordered by `Name`
- `GetFeeByIdAsync` — tracked (needed for update/delete), **no** `AsNoTracking()`
- `ChargeTypeExistsAsync` — `AsNoTracking()`, `AnyAsync` for FK validation

### 5d. Service Interface

**File**: `TSIC.Contracts/Services/IJobAdminFeeService.cs`

```csharp
public interface IJobAdminFeeService
{
    Task<List<JobAdminFeeDto>> GetFeesAsync(Guid jobId, CancellationToken ct);
    Task<List<ChargeTypeDto>> GetChargeTypesAsync(CancellationToken ct);
    Task<JobAdminFeeDto> CreateFeeAsync(Guid jobId, CreateJobAdminFeeRequest request, CancellationToken ct);
    Task<JobAdminFeeDto> UpdateFeeAsync(int id, Guid jobId, UpdateJobAdminFeeRequest request, CancellationToken ct);
    Task DeleteFeeAsync(int id, Guid jobId, CancellationToken ct);
}
```

### 5e. Service Implementation

**File**: `TSIC.API/Services/Admin/JobAdminFeeService.cs`

Validation rules (enforced in service, not repository):
- `ChargeTypeId` must reference an existing `JobAdminChargeTypes` row
- `ChargeAmount` must not be zero (negative allowed — refunds/reversals)
- `Month` must be 1–12
- `Year` must be 2020–2099
- On update/delete: fee must belong to the specified `JobId` (prevent cross-job manipulation)

### 5f. Controller

**File**: `TSIC.API/Controllers/JobAdminFeeController.cs`

```
[ApiController]
[Route("api/job-admin-fees")]
[Authorize(Policy = "SuperUserOnly")]
```

| Verb | Route | Purpose |
|---|---|---|
| `GET` | `/` | List all fees for the active job |
| `GET` | `/charge-types` | List all charge type options |
| `POST` | `/` | Create a new fee |
| `PUT` | `/{id:int}` | Update an existing fee |
| `DELETE` | `/{id:int}` | Delete a fee |

All endpoints extract `JobId` from the JWT claims (same pattern as other admin controllers). The `charge-types` endpoint is a simple lookup — no job scoping needed.

---

## 6. Frontend Architecture

### 6a. File Structure

```
src/app/views/admin/job-admin-fees/
├── job-admin-fees.component.ts       // Shell — table, state, CRUD orchestration
├── job-admin-fees.component.html     // Template
├── job-admin-fees.component.scss     // Styles
└── fee-dialog/
    ├── fee-dialog.component.ts       // Add/edit modal form
    ├── fee-dialog.component.html
    └── fee-dialog.component.scss
```

### 6b. State Management

```typescript
// job-admin-fees.component.ts
readonly fees = signal<JobAdminFeeDto[]>([]);
readonly chargeTypes = signal<ChargeTypeDto[]>([]);
readonly isLoading = signal(false);
readonly dialogOpen = signal(false);
readonly editingFee = signal<JobAdminFeeDto | null>(null); // null = add mode
```

### 6c. Table Design

A standard data table (not jqGrid) with columns:

| Column | Source | Format |
|---|---|---|
| Charge Date | `createDate` | `date:'MM/dd/yyyy hh:mm a'` pipe |
| Type | `chargeTypeName` | Text (resolved name, not ID) |
| Charge | `chargeAmount` | `currency:'USD'` pipe |
| Year | `year` | Number |
| Month | `month` | Number |
| Comment | `comment` | Text (truncated with tooltip) |
| Actions | — | Edit / Delete icon buttons |

Sorted by `createDate` descending (newest first). "No data..." empty state when list is empty.

### 6d. Add/Edit Modal

Uses `TsicDialogComponent` as the dialog wrapper. Form fields:

| Field | Control | Default (Add) | Validation |
|---|---|---|---|
| Charge Date | `<input type="datetime-local">` | Current datetime | Required |
| Type | `<select>` populated from `chargeTypes()` | First option | Required |
| Charge | `<input type="number" step="0.01">` | — | Required, non-zero |
| Year | `<select>` (current year, prior year) | Current year | Required |
| Month | `<select>` (1–12) | Current month | Required |
| Comment | `<textarea>` | — | Optional, max 500 chars |

### 6e. Delete Confirmation

Simple `TsicDialogComponent` confirmation: "Delete this charge of {amount} ({type})?" with Cancel / Delete buttons.

### 6f. Routing

```typescript
{
    path: 'job-admin-fees',
    loadComponent: () => import('./views/admin/job-admin-fees/job-admin-fees.component')
        .then(m => m.JobAdminFeesComponent),
    data: { title: 'Job Admin Fees' }
}
```

Add to `ROUTE_TITLE_MAP` in `BreadcrumbService`:
```typescript
'job-admin-fees': 'Job Admin Fees'
```

### 6g. Styling

- Glass-surface card for the table container
- CSS variables only (no hardcoded colors/spacing)
- 8px spacing grid
- Responsive — table scrolls horizontally on narrow viewports
- Currency cells right-aligned, monospace font for amounts

---

## 7. Implementation Phases

### Phase 1 — Backend DTOs & Contracts
**Goal**: Define the data shapes and interfaces.

| # | Task |
|---|---|
| 1 | Create `TSIC.Contracts/Dtos/JobAdminFees/JobAdminFeeDtos.cs` with all DTOs |
| 2 | Create `TSIC.Contracts/Repositories/IJobAdminFeeRepository.cs` |
| 3 | Create `TSIC.Contracts/Services/IJobAdminFeeService.cs` |

### Phase 2 — Backend Implementation
**Goal**: Repository, service, and controller — full API working.

| # | Task |
|---|---|
| 4 | Create `TSIC.Infrastructure/Repositories/JobAdminFeeRepository.cs` |
| 5 | Create `TSIC.API/Services/Admin/JobAdminFeeService.cs` |
| 6 | Create `TSIC.API/Controllers/JobAdminFeeController.cs` |
| 7 | Register DI in `Program.cs` (`IJobAdminFeeRepository` + `IJobAdminFeeService`) |

**Deliverable**: All 5 endpoints callable via Swagger.

### Phase 3 — Frontend Shell & Table
**Goal**: Angular component loads and displays fees.

| # | Task |
|---|---|
| 8 | Run `.\scripts\2-Regenerate-API-Models.ps1` to generate TypeScript models |
| 9 | Create `job-admin-fees.component.ts/html/scss` with data table |
| 10 | Add route in `app.routes.ts` under admin children |
| 11 | Update `BreadcrumbService` with route title |
| 12 | Wire up `GET /` and `GET /charge-types` calls on component init |

**Deliverable**: Table renders with live data, no CRUD yet.

### Phase 4 — CRUD & Polish
**Goal**: Add/edit/delete working with validation, polish UX.

| # | Task |
|---|---|
| 13 | Create `fee-dialog/` component with form + validation |
| 14 | Wire up `POST /`, `PUT /{id}`, `DELETE /{id}` from dialog actions |
| 15 | Add delete confirmation dialog |
| 16 | Add loading states, error toasts, empty state |
| 17 | Add nav link from admin menu (if applicable) |

**Deliverable**: Full feature parity with legacy, ready for testing.

---

## 8. Constraints & Conventions

| Rule | Detail |
|---|---|
| Repository pattern | All data access via `IJobAdminFeeRepository` — zero `SqlDbContext` in service/controller |
| Sequential awaits | No `Task.WhenAll` — DbContext is not thread-safe |
| DTO pattern | `required` + `init` properties, object initializer syntax |
| Auto-generated models | Run `2-Regenerate-API-Models.ps1` after backend is complete |
| CSS variables only | No hardcoded colors, spacing, or shadows |
| Signals for state | `signal<T>()` for component state, observables for HTTP only |
| Relative routerLinks | Never absolute — preserve `:jobPath` prefix |
| `AsNoTracking()` | All read-only repository queries |
| Standalone components | No NgModules, `@if`/`@for` control flow, OnPush change detection |

---

## 9. Risk & Mitigation

| Risk | Impact | Mitigation |
|---|---|---|
| Charge types change in DB but frontend caches stale list | Wrong type displayed | Fetch charge types fresh on each component load (tiny table, negligible cost) |
| Negative `ChargeAmount` for refunds vs validation rejecting negatives | Business logic error | Allow negative amounts — refunds/reversals are legitimate. Only reject zero |
| Cross-job fee manipulation via crafted request | Security | Service validates `fee.JobId == callerJobId` on update/delete |
| Legacy rows with invalid year/month values | Display issue | Backend accepts any stored values; frontend only constrains new entries |

---

## 10. Success Criteria

- [ ] All 5 API endpoints return correct data and status codes
- [ ] Fees are scoped to the active job — no cross-job data leakage
- [ ] Charge types load dynamically from the database
- [ ] Add/edit modal validates all fields before submission
- [ ] Delete requires confirmation
- [ ] Zero `SqlDbContext` references outside the repository
- [ ] All DTOs use `required` + `init` pattern (no positional records)
- [ ] CSS uses only design system variables
- [ ] Table displays currency-formatted amounts
- [ ] Breadcrumb displays correctly on the page
- [ ] SuperUser-only access enforced (non-SuperUsers see 403)

---

## 11. Files Summary

### New Files (10)
1. `TSIC.Contracts/Dtos/JobAdminFees/JobAdminFeeDtos.cs`
2. `TSIC.Contracts/Repositories/IJobAdminFeeRepository.cs`
3. `TSIC.Contracts/Services/IJobAdminFeeService.cs`
4. `TSIC.Infrastructure/Repositories/JobAdminFeeRepository.cs`
5. `TSIC.API/Services/Admin/JobAdminFeeService.cs`
6. `TSIC.API/Controllers/JobAdminFeeController.cs`
7. `src/frontend/tsic-app/src/app/views/admin/job-admin-fees/job-admin-fees.component.ts`
8. `src/frontend/tsic-app/src/app/views/admin/job-admin-fees/job-admin-fees.component.html`
9. `src/frontend/tsic-app/src/app/views/admin/job-admin-fees/job-admin-fees.component.scss`
10. `src/frontend/tsic-app/src/app/views/admin/job-admin-fees/fee-dialog/fee-dialog.component.ts` (+html/scss)

### Modified Files (3)
1. `TSIC.API/Program.cs` — DI registration for repository + service
2. `src/frontend/tsic-app/src/app/app.routes.ts` — Add admin route
3. `src/frontend/tsic-app/src/app/infrastructure/services/breadcrumb.service.ts` — Add route title mapping
