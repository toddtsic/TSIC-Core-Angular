# 015 — Customer Configure

> **Status**: Design Spec — Ready for Review
> **Date**: 2026-02-18
> **Keyword**: `CWCC Implement CUSTOMER-CONFIGURE`
> **Legacy reference**: `reference/TSIC-Unify-2024/TSIC-Unify/Controllers/Admin/CustomerController.cs`
> **Legacy route**: `Customer/Admin`

---

## 1. Problem Statement

The legacy Customer/Admin page lets SuperUsers view and edit Customer records (name, timezone, Authorize.Net payment credentials). The current implementation suffers from:

| Issue | Detail |
|---|---|
| **Repository violation** | Controller accesses `SqlDbContext` directly — zero separation of concerns |
| **No DTO layer** | Uses a `CustomerViewModel` that maps 1:1 to entity columns |
| **No API surface** | MVC-only — no REST endpoints for the Angular frontend |
| **No validation** | No server-side validation beyond EF constraints |
| **Unsafe create defaults** | ADN credentials copied from TSIC default customer via `AppSettings.TSICParams.TSICCustomerId` — config value not yet in new backend |

**Goal**: Replace with a clean Angular + Web API implementation following repository pattern, proper DTOs, and modern UX. This is a simple CRUD interface for a small entity with ~5 editable fields.

---

## 2. Scope

### In Scope
- List all Customers with summary info (name, timezone, job count)
- Create new Customer (with TSIC default ADN credentials)
- Edit Customer (name, timezone, ADN credentials)
- Delete Customer (with safety check — reject if customer has jobs)
- Timezone dropdown (from `Timezones` table)
- SuperUser-only access

### Out of Scope
- CustomerGroups management (separate legacy page `CustomerGroups/Index` — future migration)
- Theme assignment (the `Theme` column exists but is not edited on this page in legacy)
- Audit trail (no legacy equivalent)

---

## 3. Database Schema

Both tables already exist. **No migrations needed.**

| Table | Schema | Relationship | Notes |
|---|---|---|---|
| `Customers` | `dbo` | Primary entity | ~5 editable fields |
| `Timezones` | `dbo` | Lookup (FK `TzId`) | Dropdown for timezone selection |
| `Jobs` | `jobs` | 1:many from Customers (FK `CustomerId`) | Used to compute job count; used for delete safety check |

### Customer Entity Properties

| Column | Type | Editable | Notes |
|---|---|---|---|
| `CustomerId` | `Guid` PK | No | Generated on create |
| `CustomerAi` | `int` identity | No | Auto-increment, display only |
| `CustomerName` | `string?` | Yes | Required in UI |
| `TzId` | `int` FK | Yes | Timezone dropdown |
| `AdnLoginId` | `string?` | Yes | Authorize.Net login ID |
| `AdnTransactionKey` | `string?` | Yes | Authorize.Net transaction key |
| `Theme` | `string?` | No | Not on this page |
| `LebUserId` | `string?` | No | Set to current user on save |
| `Modified` | `DateTime` | No | Set to UTC now on save |

---

## 4. Design Decisions

### 4a. UI Pattern — Table + Modal

| Option | Pro | Con |
|---|---|---|
| **Table with modal add/edit** (chosen) | Consistent with job admin fees (013), widget editor | Extra click for modal |
| Single-record form (legacy style) | Simpler | Poor UX for browsing multiple customers |

**Decision**: Data table listing all customers + modal for add/edit. Matches existing admin patterns (widget editor, job admin fees). Delete with confirmation dialog.

### 4b. Repository Strategy — Extend Existing `ICustomerRepository`

An `ICustomerRepository` already exists with two ADN credential methods. Rather than create a parallel repository, **extend** the existing interface with the new CRUD methods. This avoids repository proliferation and keeps all Customer data access in one place.

### 4c. ADN Credential Defaults on Create

Legacy copies ADN credentials from a TSIC default customer (`TSICCustomerId` in AppSettings). The new backend does not have this config value yet.

**Decision**: Add `TsicSettings:DefaultCustomerId` to `appsettings.json` and bind to an options class. The service reads default ADN credentials from this customer when creating a new one. If the config value is missing or the default customer doesn't exist, the create still succeeds — ADN fields are simply left blank.

### 4d. Delete Safety — Reject If Jobs Exist

Legacy deletes customers without checking for associated jobs, which could orphan job records. This is a bug.

**Decision**: The service checks `CountCustomerJobs > 0` before deletion and returns a 409 Conflict if the customer has associated jobs. The frontend shows the job count in the table so the user understands why deletion is blocked.

### 4e. Concurrency — Last Write Wins

Customer records are rarely edited concurrently. No optimistic concurrency needed.

---

## 5. Backend Architecture

### 5a. Configuration

**Add to `appsettings.json`:**
```json
"TsicSettings": {
    "DefaultCustomerId": "60660D3C-6C8C-DC11-8046-00137250256D"
}
```

**Options class** — `TSIC.Contracts/Options/TsicSettings.cs`:
```csharp
public class TsicSettings
{
    public Guid DefaultCustomerId { get; set; }
}
```

Registered in `Program.cs`:
```csharp
builder.Services.Configure<TsicSettings>(builder.Configuration.GetSection("TsicSettings"));
```

### 5b. DTOs

**File**: `TSIC.Contracts/Dtos/Customer/CustomerConfigureDtos.cs`

```csharp
// --- Response DTOs ---

public record CustomerListDto
{
    public required Guid CustomerId { get; init; }
    public required int CustomerAi { get; init; }
    public required string? CustomerName { get; init; }
    public required int TzId { get; init; }
    public required string? TimezoneName { get; init; }
    public required int JobCount { get; init; }
}

public record CustomerDetailDto
{
    public required Guid CustomerId { get; init; }
    public required int CustomerAi { get; init; }
    public required string? CustomerName { get; init; }
    public required int TzId { get; init; }
    public required string? AdnLoginId { get; init; }
    public required string? AdnTransactionKey { get; init; }
}

public record TimezoneDto
{
    public required int TzId { get; init; }
    public required string? TzName { get; init; }
}

// --- Request DTOs ---

public record CreateCustomerRequest
{
    public required string CustomerName { get; init; }
    public required int TzId { get; init; }
    public string? AdnLoginId { get; init; }
    public string? AdnTransactionKey { get; init; }
}

public record UpdateCustomerRequest
{
    public required string CustomerName { get; init; }
    public required int TzId { get; init; }
    public string? AdnLoginId { get; init; }
    public string? AdnTransactionKey { get; init; }
}
```

### 5c. Repository Interface (Extend Existing)

**File**: `TSIC.Contracts/Repositories/ICustomerRepository.cs` — add methods to existing interface:

```csharp
public interface ICustomerRepository
{
    // ── Existing methods (unchanged) ────────────────────
    Task<AdnCredentialsViewModel?> GetAdnCredentialsAsync(Guid customerId, CancellationToken ct = default);
    Task<AdnCredentialsViewModel?> GetAdnCredentialsByJobIdAsync(Guid jobId, CancellationToken ct = default);

    // ── New: Customer Configure CRUD ────────────────────
    // Read (AsNoTracking)
    Task<List<CustomerListDto>> GetAllCustomersAsync(CancellationToken ct = default);
    Task<CustomerDetailDto?> GetCustomerByIdAsync(Guid customerId, CancellationToken ct = default);
    Task<List<TimezoneDto>> GetTimezonesAsync(CancellationToken ct = default);
    Task<int> GetCustomerJobCountAsync(Guid customerId, CancellationToken ct = default);
    Task<bool> TimezoneExistsAsync(int tzId, CancellationToken ct = default);

    // Write (tracked)
    Task<Customers?> GetCustomerTrackedAsync(Guid customerId, CancellationToken ct = default);
    void AddCustomer(Customers customer);
    void RemoveCustomer(Customers customer);

    // Persistence
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
```

### 5d. Service Interface

**File**: `TSIC.Contracts/Services/ICustomerConfigureService.cs`

```csharp
public interface ICustomerConfigureService
{
    Task<List<CustomerListDto>> GetAllCustomersAsync(CancellationToken ct);
    Task<CustomerDetailDto?> GetCustomerByIdAsync(Guid customerId, CancellationToken ct);
    Task<List<TimezoneDto>> GetTimezonesAsync(CancellationToken ct);
    Task<CustomerDetailDto> CreateCustomerAsync(CreateCustomerRequest request, string userId, CancellationToken ct);
    Task<CustomerDetailDto> UpdateCustomerAsync(Guid customerId, UpdateCustomerRequest request, string userId, CancellationToken ct);
    Task DeleteCustomerAsync(Guid customerId, CancellationToken ct);
}
```

### 5e. Service Implementation

**File**: `TSIC.API/Services/Admin/CustomerConfigureService.cs`

Key logic:
- **Create**: Generate `Guid.NewGuid()` for `CustomerId`. If `TsicSettings.DefaultCustomerId` is configured, load that customer's ADN credentials as defaults (only when the request doesn't supply its own). Set `LebUserId` and `Modified`.
- **Update**: Load tracked entity, apply changes, set `LebUserId` and `Modified = DateTime.UtcNow`.
- **Delete**: Check `GetCustomerJobCountAsync > 0` → throw `InvalidOperationException` ("Cannot delete customer with associated jobs"). Otherwise remove.

Validation rules:
- `CustomerName` must not be null/empty
- `TzId` must reference an existing timezone (`TimezoneExistsAsync`)
- On delete: customer must have zero associated jobs

### 5f. Controller

**File**: `TSIC.API/Controllers/CustomerConfigureController.cs`

```
[ApiController]
[Route("api/customer-configure")]
[Authorize(Policy = "SuperUserOnly")]
```

| Verb | Route | Purpose |
|---|---|---|
| `GET` | `/` | List all customers (with timezone name + job count) |
| `GET` | `/{customerId:guid}` | Get single customer detail (includes ADN credentials) |
| `GET` | `/timezones` | List all timezones for dropdown |
| `POST` | `/` | Create new customer |
| `PUT` | `/{customerId:guid}` | Update customer |
| `DELETE` | `/{customerId:guid}` | Delete customer (409 if has jobs) |

**6 endpoints total.**

---

## 6. Frontend Architecture

### 6a. File Structure

```
src/app/views/admin/customer-configure/
├── customer-configure.component.ts       // Shell — table, state, CRUD orchestration
├── customer-configure.component.html     // Template
├── customer-configure.component.scss     // Styles
└── customer-dialog/
    ├── customer-dialog.component.ts      // Add/edit modal form
    ├── customer-dialog.component.html
    └── customer-dialog.component.scss
```

### 6b. State Management

```typescript
// customer-configure.component.ts
readonly customers = signal<CustomerListDto[]>([]);
readonly timezones = signal<TimezoneDto[]>([]);
readonly isLoading = signal(false);
readonly dialogOpen = signal(false);
readonly editingCustomer = signal<CustomerDetailDto | null>(null); // null = add mode
```

### 6c. Table Design

| Column | Source | Format |
|---|---|---|
| ID | `customerAi` | Number (auto-increment display) |
| Name | `customerName` | Text |
| Timezone | `timezoneName` | Text (resolved name) |
| Jobs | `jobCount` | Number (badge) |
| Actions | — | Edit / Delete icon buttons |

Sorted by `customerName` ascending. "No customers found" empty state.

Delete button disabled (with tooltip) when `jobCount > 0`.

### 6d. Add/Edit Modal

Uses `TsicDialogComponent` wrapper. Form fields:

| Field | Control | Validation |
|---|---|---|
| Customer Name | `<input type="text">` | Required |
| Timezone | `<select>` from `timezones()` | Required |
| ADN Login ID | `<input type="text">` | Optional |
| ADN Transaction Key | `<input type="text">` | Optional |

On **add**: fields start blank (ADN credentials populated by backend from TSIC defaults).
On **edit**: fields populated from `GET /{customerId}` response.

### 6e. Delete Confirmation

`TsicDialogComponent` confirmation: "Delete customer '{name}'? This cannot be undone." with Cancel / Delete buttons. Only reachable when `jobCount === 0`.

### 6f. Routing

```typescript
{
    path: 'customer-configure',
    loadComponent: () => import('./views/admin/customer-configure/customer-configure.component')
        .then(m => m.CustomerConfigureComponent),
    data: { requireSuperUser: true, title: 'Customer Configure' }
}
```

Under the `admin` parent route (which uses `requireAdmin: true`). The explicit `requireSuperUser: true` on this child ensures only SuperUsers can access it.

### 6g. Breadcrumb

```typescript
ROUTE_TITLE_MAP['admin/customer-configure'] = 'Customer Configure';
```

### 6h. Styling

- Glass-surface card for the table container (matches widget editor, job admin fees)
- CSS variables only (no hardcoded colors/spacing)
- 8px spacing grid (`var(--space-N)`)
- Responsive — table scrolls horizontally on narrow viewports
- Job count column uses a badge/pill style

---

## 7. Implementation Phases

### Phase 1 — Backend DTOs & Contracts

**Goal**: Define data shapes and interfaces.

| # | Task | File |
|---|---|---|
| 1 | Create config options class | `Contracts/Options/TsicSettings.cs` |
| 2 | Create DTOs | `Contracts/Dtos/Customer/CustomerConfigureDtos.cs` |
| 3 | Extend repository interface | `Contracts/Repositories/ICustomerRepository.cs` |
| 4 | Create service interface | `Contracts/Services/ICustomerConfigureService.cs` |

### Phase 2 — Backend Implementation

**Goal**: Repository, service, controller — full API working.

| # | Task | File |
|---|---|---|
| 5 | Extend repository implementation | `Infrastructure/Repositories/CustomerRepository.cs` |
| 6 | Create service implementation | `API/Services/Admin/CustomerConfigureService.cs` |
| 7 | Create controller | `API/Controllers/CustomerConfigureController.cs` |
| 8 | Add config section to appsettings | `API/appsettings.json` |
| 9 | Register DI + bind config in Program.cs | `API/Program.cs` |

**Deliverable**: All 6 endpoints callable via Swagger.

### Phase 3 — Frontend Shell & Table

**Goal**: Angular component loads and displays customers.

| # | Task | File |
|---|---|---|
| 10 | Run `.\scripts\2-Regenerate-API-Models.ps1` | — |
| 11 | Create component shell + table | `views/admin/customer-configure/customer-configure.component.*` |
| 12 | Add route in `app.routes.ts` under admin children | `app.routes.ts` |
| 13 | Update breadcrumb service | `breadcrumb.service.ts` |
| 14 | Wire up GET calls on component init | — |

**Deliverable**: Table renders with live data, no CRUD yet.

### Phase 4 — CRUD & Polish

**Goal**: Add/edit/delete working with validation, polish UX.

| # | Task | File |
|---|---|---|
| 15 | Create customer dialog component | `views/admin/customer-configure/customer-dialog/customer-dialog.component.*` |
| 16 | Wire up POST, PUT, DELETE from dialog/table actions | — |
| 17 | Add delete confirmation dialog | — |
| 18 | Add loading states, error toasts, empty state | — |
| 19 | Palette test across all 8 themes | — |

**Deliverable**: Full feature parity with legacy (plus delete safety), ready for testing.

---

## 8. Error Handling

| Condition | Exception | HTTP Status |
|---|---|---|
| Customer not found | `KeyNotFoundException` | 404 |
| Invalid timezone ID | `ArgumentException` | 400 |
| Empty customer name | `ArgumentException` | 400 |
| Delete customer with jobs | `InvalidOperationException` | 409 Conflict |
| Duplicate detection (if needed) | — | Not required — customer names are not unique-constrained |

---

## 9. Constraints & Conventions

| Rule | Detail |
|---|---|
| Repository pattern | All data access via `ICustomerRepository` — zero `SqlDbContext` in service/controller |
| Sequential awaits | No `Task.WhenAll` — DbContext is not thread-safe |
| DTO pattern | `required` + `init` properties, object initializer syntax |
| No positional records | Use `{ get; init; }` style for all DTOs |
| Auto-generated models | Run `2-Regenerate-API-Models.ps1` after backend is complete |
| CSS variables only | No hardcoded colors, spacing, or shadows |
| Signals for state | `signal<T>()` for component state, observables for HTTP only |
| Relative routerLinks | Never absolute — preserve `:jobPath` prefix |
| `AsNoTracking()` | All read-only repository queries |
| Standalone components | No NgModules, `@if`/`@for` control flow, OnPush change detection |
| `TsicDialogComponent` | All modals wrap the gold-standard dialog (native dialog, focus trap, ESC) |

---

## 10. Risk & Mitigation

| Risk | Impact | Mitigation |
|---|---|---|
| Default customer config missing in new backend | Create fails to populate ADN defaults | Graceful fallback — ADN fields left blank if config missing |
| Deleting customer with jobs orphans records | Data integrity | Service rejects delete when `jobCount > 0` (409 Conflict) |
| ADN credentials displayed in plain text | Security | Credentials are sensitive — consider masking `AdnTransactionKey` in list view (show only in edit modal). Legacy showed them in plain text too, so parity is acceptable for MVP |
| Timezones table is small but changes rarely | Stale data | Fetch fresh on each component load (tiny table, negligible cost) |

---

## 11. Success Criteria

- [ ] All 6 API endpoints return correct data and status codes
- [ ] Customer list shows name, timezone, job count for all customers
- [ ] Add modal creates customer with TSIC default ADN credentials
- [ ] Edit modal loads and saves all editable fields
- [ ] Delete blocked (409) when customer has associated jobs
- [ ] Timezone dropdown populated from database
- [ ] Zero `SqlDbContext` references outside the repository
- [ ] All DTOs use `required` + `init` pattern (no positional records)
- [ ] CSS uses only design system variables
- [ ] Breadcrumb displays correctly
- [ ] SuperUser-only access enforced (non-SuperUsers see 403)
- [ ] All 8 color palettes render correctly

---

## 12. Files Summary

### New Files (8)
1. `TSIC.Contracts/Options/TsicSettings.cs`
2. `TSIC.Contracts/Dtos/Customer/CustomerConfigureDtos.cs`
3. `TSIC.Contracts/Services/ICustomerConfigureService.cs`
4. `TSIC.API/Services/Admin/CustomerConfigureService.cs`
5. `TSIC.API/Controllers/CustomerConfigureController.cs`
6. `src/frontend/tsic-app/src/app/views/admin/customer-configure/customer-configure.component.ts` (+html/scss)
7. `src/frontend/tsic-app/src/app/views/admin/customer-configure/customer-dialog/customer-dialog.component.ts` (+html/scss)

### Modified Files (5)
1. `TSIC.Contracts/Repositories/ICustomerRepository.cs` — Add CRUD methods
2. `TSIC.Infrastructure/Repositories/CustomerRepository.cs` — Implement CRUD methods
3. `TSIC.API/Program.cs` — DI registration + config binding
4. `TSIC.API/appsettings.json` — Add `TsicSettings` section
5. `src/frontend/tsic-app/src/app/app.routes.ts` — Add admin child route
6. `src/frontend/tsic-app/src/app/infrastructure/services/breadcrumb.service.ts` — Add route title mapping
