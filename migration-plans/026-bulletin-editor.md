# Migration Plan: Bulletin/Admin → Bulletin Editor

## Context

The legacy TSIC-Unify-2024 project has a `Bulletin/Admin` page that manages job bulletins (announcements
displayed on the job landing page). It uses jqGrid for inline editing with immediate server updates.
We are modernizing this as an Angular component with a clean API, following patterns established
in the Discount Codes Management module.

**Legacy path**: `bulletin/admin`
**New route**: `/:jobPath/admin/bulletin-editor`

### What Already Exists

- **Frontend read-only widget**: `BulletinsComponent` in `widgets/communications/` — displays active bulletins on dashboard
- **Backend read endpoint**: `GET /api/bulletins/job/{jobPath}` (anonymous, public)
- **Repository**: `IBulletinRepository.GetActiveBulletinsForJobAsync()` — read-only, date-filtered
- **Service**: `IBulletinService.GetActiveBulletinsForJobAsync()` — applies token substitution (!JOBNAME, !USLAXVALIDTHROUGHDATE)
- **DTO**: `BulletinDto` (lightweight, public fields only — no `Active`, `LebUserId`, `Modified`)
- **Entity**: `Bulletins` (EF-scaffolded, includes `Active`, `LebUserId`, `Modified`, `ExpireHours`, `Bcore`)
- **Rich text editor**: Syncfusion EJ2 RichTextEditor (`@syncfusion/ej2-angular-richtexteditor`) already installed and used in job config tabs + rescheduler

---

## 1. Legacy Pain Points

- **jqGrid dependency** — Heavy jQuery plugin, dated look, poor mobile experience
- **Inline editing with confusing UX** — Edit mode unclear, no validation feedback before save
- **No rich text preview** — HTML entered blindly via CKEditor in a grid cell
- **No date validation** — Can create bulletins with end date before start date
- **Batch toggle clunky** — Single "Set All Active" operation, no granularity
- **Token replacement invisible** — Admin doesn't see `!JOBNAME` will be replaced
- **Anti-forgery token plumbing** — Boilerplate in every AJAX call

## 2. Modern Vision

A clean, card-based bulletin management page with:
- **Responsive data table** with title, active status, date range, last modified, actions
- **Add/Edit modal** with Syncfusion RichTextEditor for HTML body
- **Date range pickers** with validation (end ≥ start)
- **Active toggle** per bulletin + batch activate/deactivate all
- **Token hint** — info callout showing available tokens (!JOBNAME, !USLAXVALIDTHROUGHDATE)
- **Preview** — rendered HTML preview in the modal
- **Instant feedback** — Toast notifications, optimistic UI updates

## 3. User Value

- **Rich editing**: Syncfusion RTE replaces CKEditor — consistent with job config editors
- **Clear date management**: Date pickers with validation prevent expired-but-active confusion
- **Batch operations**: Activate/deactivate all bulletins in one click
- **Token awareness**: Callout explains available tokens so admins use them correctly
- **Mobile access**: Manage bulletins from any device

## 4. Design Alignment

- Bootstrap table + CSS variables (all 8 palettes)
- `TsicDialogComponent` for modals (size `lg` to accommodate RTE)
- Signal-based state, OnPush change detection
- Toast notifications via existing `ToastService`
- Confirmation dialog for delete
- WCAG AA compliant (contrast, focus management, ARIA labels)
- Follows discount codes / administrator management table patterns exactly

## 5. UI Standards Created / Employed

### CREATED (new patterns this module introduces)
- **Bulletin Form with RTE** — modal-based form combining text inputs + Syncfusion RichTextEditor (reuses `JOB_CONFIG_RTE_TOOLS` config)
- **Token hint callout** — info-style callout explaining available text tokens

### EMPLOYED (existing patterns reused)
- `TsicDialogComponent` for modals
- `ConfirmDialogComponent` for destructive actions
- `ToastService` for success/error feedback
- Signal-based state management (discount codes pattern)
- Sortable table with icon-btn action buttons
- Batch activate/deactivate toolbar (discount codes pattern)
- Syncfusion `ejs-richtexteditor` with shared `JOB_CONFIG_RTE_TOOLS`
- CSS variable design system tokens
- `@if` / `@for` template syntax
- OnPush change detection

---

## 6. Security Requirements

**CRITICAL**: This module must derive `jobId` from JWT claims, NOT from route parameters.

- **Route**: `/:jobPath/admin/bulletin-editor` (jobPath for routing only)
- **API Endpoints**: Must use `User.GetJobIdFromRegistrationAsync(_jobLookupService)` to derive `jobId`
- **Policy**: `[Authorize(Policy = "AdminOnly")]` — Directors, SuperDirectors, and Superusers
- **Validation**: Server must verify bulletin belongs to the user's job before update/delete

---

## 7. Implementation Steps

### Step 1: Backend — Admin DTOs

**Files to create**:
- `TSIC.Contracts/Dtos/Bulletin/BulletinAdminDto.cs`
- `TSIC.Contracts/Dtos/Bulletin/CreateBulletinRequest.cs`
- `TSIC.Contracts/Dtos/Bulletin/UpdateBulletinRequest.cs`
- `TSIC.Contracts/Dtos/Bulletin/BatchUpdateBulletinStatusRequest.cs`

**BulletinAdminDto** (extends public DTO with admin fields):
```csharp
public record BulletinAdminDto
{
    public required Guid BulletinId { get; init; }
    public string? Title { get; init; }
    public string? Text { get; init; }
    public required bool Active { get; init; }
    public DateTime? StartDate { get; init; }
    public DateTime? EndDate { get; init; }
    public required DateTime CreateDate { get; init; }
    public required DateTime Modified { get; init; }
    public string? ModifiedByUsername { get; init; }
}
```

**CreateBulletinRequest**:
```csharp
public record CreateBulletinRequest
{
    public required string Title { get; init; }
    public required string Text { get; init; }
    public required bool Active { get; init; }
    public DateTime? StartDate { get; init; }
    public DateTime? EndDate { get; init; }
}
```

**UpdateBulletinRequest**: Same shape as CreateBulletinRequest.

**BatchUpdateBulletinStatusRequest**:
```csharp
public record BatchUpdateBulletinStatusRequest
{
    public required bool Active { get; init; }
}
```

### Step 2: Backend — Repository Expansion

**Files to modify**:
- `TSIC.Contracts/Repositories/IBulletinRepository.cs` — add methods
- `TSIC.Infrastructure/Repositories/BulletinRepository.cs` — implement them

**New methods**:
```csharp
// Admin list: ALL bulletins for job (no date filter, includes inactive)
Task<List<BulletinAdminDto>> GetAllBulletinsForJobAsync(Guid jobId, CancellationToken ct = default);

// Single bulletin for edit (tracked, not AsNoTracking)
Task<Bulletins?> GetByIdAsync(Guid bulletinId, CancellationToken ct = default);

// Batch update active status for all bulletins in job
Task<int> BatchUpdateActiveStatusAsync(Guid jobId, bool active, CancellationToken ct = default);

// Standard write support
void Add(Bulletins bulletin);
void Remove(Bulletins bulletin);
Task SaveChangesAsync(CancellationToken ct = default);
```

**GetAllBulletinsForJobAsync** query: no date filtering, no `Active` filter, ordered by `CreateDate` desc.
Join to `AspNetUsers` via `LebUserId` to get `ModifiedByUsername`.

### Step 3: Backend — Service Expansion

**Files to modify**:
- `TSIC.API/Services/Shared/Bulletins/IBulletinService.cs` — add methods
- `TSIC.API/Services/Shared/Bulletins/BulletinService.cs` — implement them

**New methods**:
```csharp
Task<List<BulletinAdminDto>> GetAllBulletinsForJobAsync(Guid jobId, CancellationToken ct = default);
Task<BulletinAdminDto> CreateBulletinAsync(Guid jobId, string userId, CreateBulletinRequest request, CancellationToken ct = default);
Task<BulletinAdminDto> UpdateBulletinAsync(Guid bulletinId, Guid jobId, string userId, UpdateBulletinRequest request, CancellationToken ct = default);
Task<bool> DeleteBulletinAsync(Guid bulletinId, Guid jobId, CancellationToken ct = default);
Task<int> BatchUpdateStatusAsync(Guid jobId, bool active, CancellationToken ct = default);
```

**Business rules**:
- Create: set `BulletinId = Guid.NewGuid()`, `CreateDate = DateTime.UtcNow`, `Modified = DateTime.UtcNow`, `LebUserId = userId`
- Update: verify bulletin belongs to job, set `Modified = DateTime.UtcNow`, `LebUserId = userId`
- Delete: verify bulletin belongs to job before removing
- EndDate validation: if both StartDate and EndDate are set, EndDate must be >= StartDate
- Token substitution NOT applied for admin view — admin sees raw `!JOBNAME` tokens

### Step 4: Backend — Controller Expansion

**File to modify**:
- `TSIC.API/Controllers/BulletinsController.cs` — uncomment stubs and implement

**New endpoints** (all `[Authorize(Policy = "AdminOnly")]`):
```
GET    /api/bulletins/admin        → GetAllBulletins()       → List<BulletinAdminDto>
POST   /api/bulletins              → CreateBulletin()        → BulletinAdminDto
PUT    /api/bulletins/{bulletinId} → UpdateBulletin()        → BulletinAdminDto
DELETE /api/bulletins/{bulletinId} → DeleteBulletin()        → NoContent
POST   /api/bulletins/batch-status → BatchUpdateStatus()    → { updatedCount: int }
```

All admin endpoints derive jobId via:
```csharp
var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
if (jobId == null) return BadRequest(new { message = "Registration context required" });
```

### Step 5: Backend — DI Registration

**File to check**: `TSIC.API/Program.cs`
- Verify `IBulletinRepository` / `BulletinRepository` already registered (they should be)
- Verify `IBulletinService` / `BulletinService` already registered (they should be)
- No new registrations expected

### Step 6: Regenerate Frontend API Models

Run `.\scripts\2-Regenerate-API-Models.ps1` to generate TypeScript types for the new DTOs.

### Step 7: Frontend — Bulletin Admin Service

**File to create**: `src/app/views/admin/bulletin-editor/services/bulletin-admin.service.ts`

```typescript
@Injectable({ providedIn: 'root' })
export class BulletinAdminService {
  private readonly http = inject(HttpClient);
  private readonly apiUrl = `${environment.apiUrl}/Bulletins`;

  getAllBulletins(): Observable<BulletinAdminDto[]> {
    return this.http.get<BulletinAdminDto[]>(`${this.apiUrl}/admin`);
  }

  createBulletin(request: CreateBulletinRequest): Observable<BulletinAdminDto> {
    return this.http.post<BulletinAdminDto>(this.apiUrl, request);
  }

  updateBulletin(bulletinId: string, request: UpdateBulletinRequest): Observable<BulletinAdminDto> {
    return this.http.put<BulletinAdminDto>(`${this.apiUrl}/${bulletinId}`, request);
  }

  deleteBulletin(bulletinId: string): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/${bulletinId}`);
  }

  batchUpdateStatus(active: boolean): Observable<{ updatedCount: number }> {
    return this.http.post<{ updatedCount: number }>(`${this.apiUrl}/batch-status`, { active });
  }
}
```

### Step 8: Frontend — Main Component (Table + Toolbar)

**Files to create**:
- `src/app/views/admin/bulletin-editor/bulletin-editor.component.ts`
- `src/app/views/admin/bulletin-editor/bulletin-editor.component.html`
- `src/app/views/admin/bulletin-editor/bulletin-editor.component.scss`

**Layout**: Identical to discount codes — page header with action buttons, sortable table, icon-btn actions.

**Signals**:
```typescript
bulletins = signal<BulletinAdminDto[]>([]);
isLoading = signal(false);
errorMessage = signal<string | null>(null);
sortColumn = signal<SortColumn>('createDate');
sortDirection = signal<SortDirection>('desc');
showAddModal = signal(false);
showEditModal = signal(false);
editTarget = signal<BulletinAdminDto | null>(null);
showDeleteConfirm = signal(false);
deleteTarget = signal<BulletinAdminDto | null>(null);
sortedBulletins = computed(() => { /* sorted by sortColumn/sortDirection */ });
```

**Table columns**:
| # | Title | Active | Start Date | End Date | Modified | Actions |
|---|-------|--------|------------|----------|----------|---------|
| row num | `title` (truncated ~60 chars) | toggle badge | formatted date | formatted date | relative time + username | edit / delete |

**Header buttons**:
- "Activate All" / "Deactivate All" (batch toggle)
- Refresh
- "Add Bulletin" (primary)

### Step 9: Frontend — Add/Edit Modal with RTE

**File to create**: `src/app/views/admin/bulletin-editor/components/bulletin-form-modal.component.ts`

**Features**:
- Uses `TsicDialogComponent` wrapper, size `lg` (RTE needs width)
- **Title** — text input (required)
- **Text** — Syncfusion `ejs-richtexteditor` using shared `JOB_CONFIG_RTE_TOOLS` config
  - Import `RichTextEditorAllModule`
  - Height: 300px (taller than job-config's 200px — bulletins are longer-form)
  - `[enableHtmlSanitizer]="false"` (raw HTML stored, sanitization at display time)
- **Active** — toggle/checkbox (default true for add)
- **Start Date** — date input (default: today for add)
- **End Date** — date input (optional, validation: >= start date)
- **Token hint** — small info callout below RTE:
  > Available tokens: `!JOBNAME` (replaced with job name), `!USLAXVALIDTHROUGHDATE` (replaced with US Lacrosse valid-through date)
- Add mode: empty form, defaults applied
- Edit mode: pre-populated from `editTarget`

**RTE config** — reuse shared config:
```typescript
import { JOB_CONFIG_RTE_TOOLS } from '@views/admin/job-config/shared/rte-config';

rteTools = JOB_CONFIG_RTE_TOOLS;
rteHeight = 300;
```

### Step 10: Frontend — Routing

**File to modify**: `app.routes.ts` (admin children section)

```typescript
{
  path: 'bulletin-editor',
  loadComponent: () => import('./views/admin/bulletin-editor/bulletin-editor.component')
    .then(m => m.BulletinEditorComponent),
  data: { title: 'Bulletin Editor' }
}
```

Guard: inherits from parent admin route (`authGuard` with `requirePhase2: true`).

### Step 11: Legacy Route Translation

Map legacy `bulletin/admin` → `admin/bulletin-editor` in nav system `LegacyRouteMap` (if applicable).

### Step 12: Styling & Polish

- All colors via CSS variables
- Active badge: green for active, muted for inactive
- Date columns: formatted with `DatePipe` (`MMM d, yyyy`)
- Modified column: show username + relative time ("John D. — 2 days ago")
- Table follows exact same SCSS as discount codes (`.sortable`, `.col-actions`, `.icon-btn`)
- RTE in modal: full-width, subtle border matching form inputs
- Token hint: `bg-info-subtle` callout with `bi-info-circle` icon
- Test all 8 palettes
- Mobile: table scrolls horizontally, RTE modal full-screen on small viewports

---

## 8. File Summary

### Backend (modify existing)
| File | Changes |
|------|---------|
| `IBulletinRepository.cs` | +5 methods (admin list, get by ID, add, remove, save, batch) |
| `BulletinRepository.cs` | +5 implementations |
| `IBulletinService.cs` | +5 methods (admin list, create, update, delete, batch) |
| `BulletinService.cs` | +5 implementations |
| `BulletinsController.cs` | +5 endpoints (uncomment stubs + implement) |

### Backend (new files)
| File | Purpose |
|------|---------|
| `Dtos/Bulletin/BulletinAdminDto.cs` | Admin DTO with Active, Modified, Username |
| `Dtos/Bulletin/CreateBulletinRequest.cs` | Create request |
| `Dtos/Bulletin/UpdateBulletinRequest.cs` | Update request |
| `Dtos/Bulletin/BatchUpdateBulletinStatusRequest.cs` | Batch status request |

### Frontend (new files)
| File | Purpose |
|------|---------|
| `bulletin-editor/bulletin-editor.component.ts` | Main table + toolbar |
| `bulletin-editor/bulletin-editor.component.html` | Template |
| `bulletin-editor/bulletin-editor.component.scss` | Styles |
| `bulletin-editor/components/bulletin-form-modal.component.ts` | Add/Edit modal with RTE |
| `bulletin-editor/services/bulletin-admin.service.ts` | HTTP service |

### Frontend (modify existing)
| File | Changes |
|------|---------|
| `app.routes.ts` | +1 admin child route |

---

## 9. Dependencies

- Existing `TsicDialogComponent` (shared-ui)
- Existing `ConfirmDialogComponent` (shared-ui)
- Existing `ToastService` (shared-ui)
- Existing `ClaimsPrincipalExtensions.GetJobIdFromRegistrationAsync` (API/Extensions)
- Existing `IJobLookupService` (API/Services)
- Existing `Bulletins` entity (Domain)
- Existing `IBulletinRepository` / `BulletinRepository` (Infrastructure)
- Existing `IBulletinService` / `BulletinService` (API/Services)
- Existing `@syncfusion/ej2-angular-richtexteditor` (already installed)
- Existing `JOB_CONFIG_RTE_TOOLS` shared config
- Existing "AdminOnly" authorization policy

## 10. Verification Checklist

- [ ] Backend builds (`dotnet build`)
- [ ] API endpoints respond correctly (test via Swagger)
- [ ] **Security**: All admin endpoints derive jobId from JWT token
- [ ] TypeScript models generated (run regeneration script)
- [ ] Frontend compiles (`ng build`)
- [ ] Table loads all bulletins (including inactive) for current job
- [ ] Add bulletin with RTE works — HTML saved correctly
- [ ] Edit bulletin pre-populates all fields including RTE content
- [ ] Delete bulletin with confirmation dialog
- [ ] Batch activate/deactivate all works
- [ ] Date validation: end date >= start date
- [ ] Token hint displays correctly
- [ ] Toast notifications on success/error
- [ ] Active/inactive badge styling correct
- [ ] Responsive layout on mobile viewport
- [ ] RTE toolbar renders correctly in modal
- [ ] Test all 8 color palettes

---

## 11. Migration Notes

**Database**: No schema changes needed — `Bulletins` table already exists with all required columns.

**Legacy endpoint removal**: After verification, remove:
- `~/Views/Bulletin/Admin.cshtml`
- `BulletinController.Admin()` action (legacy MVC)
- Related jqGrid + CKEditor JavaScript files

**Data migration**: None required — existing bulletins work as-is.

**Legacy route mapping**: Map `bulletin/admin` → `admin/bulletin-editor` in nav system.
