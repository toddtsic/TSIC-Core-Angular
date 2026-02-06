# Migration Plan: JobAdministrator/Admin → Admin Management

## Context

The legacy TSIC-Unify-2024 project has a `JobAdministrator/Admin` page that manages administrative
users (Directors, SuperDirectors, ApiAuthorized, RefAssignors, StoreAdmins, STPAdmins) for a given job.
It uses jqGrid with inline CRUD operations. We are reimagining this as a modern Angular component backed
by a clean .NET Core API, establishing UI patterns that all future migrated modules will reuse.

---

## 1. Legacy Pain Points

- **jqGrid dependency** - Heavy jQuery plugin, dated look, poor mobile experience
- **Inline editing in grid cells** - Cramped, no validation feedback, confusing UX
- **No multi-select** - Batch operations are all-or-nothing (activate ALL / inactivate ALL)
- **Username as raw text input** - Typo-prone, no autocomplete, server-only validation
- **Full page reload after batch update** - Jarring, loses scroll position
- **No visual hierarchy** - Active/inactive admins look the same, roles have no visual distinction
- **Anti-forgery token plumbing** - Boilerplate in every AJAX call

## 2. Modern Vision

A clean, card-based admin management page with:
- **Responsive data table** with role badges, status indicators, and row-level actions
- **Multi-select with contextual toolbar** - Check rows, toolbar appears with batch actions
- **Modal dialog for add/edit** - Clean form with typeahead user search, role dropdown, validation
- **Instant feedback** - Optimistic UI updates with toast notifications, no page reloads
- **Mobile-first** - Stacks gracefully, touch-friendly targets
- **Superuser protection** - Visually distinct (locked icon), cannot be selected for batch ops

## 3. User Value

- **Fewer errors**: Typeahead user lookup prevents invalid usernames
- **Faster workflows**: Multi-select batch operations instead of all-or-nothing
- **Better awareness**: Role badges and status chips make scanning instant
- **Mobile access**: Admins can manage from any device
- **Modern feel**: Glassmorphic surfaces, smooth transitions, palette-responsive

## 4. Design Alignment

- Bootstrap table + CSS variables (all 8 palettes)
- `TsicDialogComponent` for modals
- Signal-based state, OnPush change detection
- Toast notifications via existing `ToastService`
- WCAG AA compliant (contrast, focus management, ARIA labels)

## 5. UI Standards Created / Employed

### CREATED (new patterns this module introduces)
- **Admin Data Table** - Responsive table with multi-select checkboxes, role badges, status chips, row actions
- **Contextual Batch Toolbar** - Appears when rows are selected, shows count + available actions
- **User Typeahead Input** - Searchable user lookup for forms (reusable across future modules)
- **Add/Edit Modal Form** - Standard modal pattern for CRUD operations with validation
- **Empty State** - Illustrated empty state when no data exists
- **Confirmation Dialog** - Reusable "are you sure?" pattern for destructive actions

### EMPLOYED (existing patterns reused)
- `TsicDialogComponent` for modals
- `ToastService` for success/error feedback
- Signal-based state management
- CSS variable design system tokens
- `@if` / `@for` template syntax
- OnPush change detection
- `inject()` dependency injection

---

## 6. Implementation Steps

### Step 1: Backend - Repository Interface & Implementation
**Status**: [x] Complete
**Files to create**:
- `TSIC.Contracts/Repositories/IAdministratorRepository.cs`
- `TSIC.Infrastructure/Repositories/AdministratorRepository.cs`

**Details**:
- `GetByJobIdAsync(Guid jobId)` - Returns admin registrations with User and Role navigation props, `AsNoTracking()`
- `GetByIdAsync(Guid registrationId)` - Single admin registration (tracked, for updates)
- `Add(Registrations registration)` - Add new admin
- `Remove(Registrations registration)` - Delete admin
- `SaveChangesAsync()` - Persist changes
- Query filters: RoleId IN [Superuser, Director, SuperDirector, ApiAuthorized, RefAssignor, StoreAdmin, STPAdmin]
- Use `RoleConstants` for role IDs

### Step 2: Backend - Service Interface & Implementation
**Status**: [x] Complete
**Files to create**:
- `TSIC.Contracts/Services/IAdministratorService.cs`
- `TSIC.API/Services/Admin/AdministratorService.cs`

**Details**:
- `GetAdministratorsAsync(Guid jobId)` → `List<AdministratorDto>`
- `AddAdministratorAsync(Guid jobId, AddAdministratorRequest request)` → `AdministratorDto`
  - Validate username exists via `UserManager<ApplicationUser>`
  - Create Registration with zero fees, RegistrationCategory = "Director"
- `UpdateAdministratorAsync(Guid registrationId, UpdateAdministratorRequest request)` → `AdministratorDto`
  - Prevent editing Superuser registrations
  - Update BActive, RoleId, Modified, LebUserId
- `DeleteAdministratorAsync(Guid registrationId)` → bool
  - Prevent deleting Superuser registrations
- `BatchUpdateStatusAsync(Guid jobId, bool isActive)` → int (count updated)
  - Updates Director, SuperDirector, ApiAuthorized only (matches legacy behavior)
- `SearchUsersAsync(string query)` → `List<UserSearchResultDto>`
  - For typeahead: returns matching users by username/firstname/lastname

### Step 3: Backend - DTOs
**Status**: [x] Complete
**Files to create**:
- `TSIC.Contracts/Dtos/Admin/AdministratorDto.cs`
- `TSIC.Contracts/Dtos/Admin/AddAdministratorRequest.cs`
- `TSIC.Contracts/Dtos/Admin/UpdateAdministratorRequest.cs`
- `TSIC.Contracts/Dtos/Admin/BatchUpdateStatusRequest.cs`
- `TSIC.Contracts/Dtos/Admin/UserSearchResultDto.cs`

**AdministratorDto**:
```csharp
public record AdministratorDto
{
    public required Guid RegistrationId { get; init; }
    public required string AdministratorName { get; init; }
    public required string UserName { get; init; }
    public string? RoleName { get; init; }
    public required bool IsActive { get; init; }
    public required DateTime RegisteredDate { get; init; }
    public required bool IsSuperuser { get; init; }
}
```

**AddAdministratorRequest**:
```csharp
public record AddAdministratorRequest
{
    public required string UserName { get; init; }
    public required string RoleName { get; init; }
}
```

**UpdateAdministratorRequest**:
```csharp
public record UpdateAdministratorRequest
{
    public required bool IsActive { get; init; }
    public required string RoleName { get; init; }
}
```

**BatchUpdateStatusRequest**:
```csharp
public record BatchUpdateStatusRequest
{
    public required bool IsActive { get; init; }
}
```

**UserSearchResultDto**:
```csharp
public record UserSearchResultDto
{
    public required string UserId { get; init; }
    public required string UserName { get; init; }
    public required string DisplayName { get; init; }
}
```

### Step 4: Backend - Controller
**Status**: [x] Complete
**Files to create**:
- `TSIC.API/Controllers/AdministratorsController.cs`

**Endpoints**:
- `GET    api/administrators/{jobId}` → List admins `[Authorize(Policy = "SuperUserOnly")]`
- `POST   api/administrators/{jobId}` → Add admin
- `PUT    api/administrators/{registrationId}` → Update admin
- `DELETE api/administrators/{registrationId}` → Delete admin
- `POST   api/administrators/{jobId}/batch-status` → Batch activate/inactivate
- `GET    api/administrators/users/search?q={query}` → User typeahead search

### Step 5: Backend - DI Registration
**Status**: [x] Complete
**Files to modify**:
- `TSIC.API/Program.cs` - Add `AddScoped` for repository and service

### Step 6: Backend - Regenerate API Models
**Status**: [x] Complete
**Action**: Run `.\scripts\2-Regenerate-API-Models.ps1` to generate TypeScript types from the new DTOs

### Step 7: Frontend - Administrator Service
**Status**: [x] Complete
**Files to create**:
- `src/app/views/admin/administrator-management/services/administrator.service.ts`

**Methods** (all return Observables):
- `getAdministrators(jobId: string)`
- `addAdministrator(jobId: string, request: AddAdministratorRequest)`
- `updateAdministrator(registrationId: string, request: UpdateAdministratorRequest)`
- `deleteAdministrator(registrationId: string)`
- `batchUpdateStatus(jobId: string, isActive: boolean)`
- `searchUsers(query: string)` - with debounce handled at component level

### Step 8: Frontend - Main Component (Table + Toolbar)
**Status**: [x] Complete
**Files to create**:
- `src/app/views/admin/administrator-management/administrator-management.component.ts`
- `src/app/views/admin/administrator-management/administrator-management.component.html`
- `src/app/views/admin/administrator-management/administrator-management.component.scss`

**Features**:
- Page header with title + "Add Administrator" button
- Responsive Bootstrap table with columns: checkbox, Name, Role (badge), Username, Status (chip), Registered, Actions (edit/delete icons)
- Superuser rows: locked icon, checkbox disabled, no edit/delete actions
- Multi-select checkboxes with "select all" header checkbox (excludes Superusers)
- Contextual toolbar appears when rows selected: "{n} selected | Activate | Inactivate | Delete"
- Loading skeleton state
- Empty state with message
- Signal-based state: `administrators`, `selectedIds`, `isLoading`, `errorMessage`
- Computed: `hasSelection`, `selectedCount`, `allNonSuperusersSelected`

### Step 9: Frontend - Add/Edit Modal Component
**Status**: [x] Complete
**Files to create**:
- `src/app/views/admin/administrator-management/components/admin-form-modal.component.ts`
- `src/app/views/admin/administrator-management/components/admin-form-modal.component.html`

**Features**:
- Uses `TsicDialogComponent` wrapper
- Add mode: User typeahead input + Role dropdown
- Edit mode: Name (read-only) + Role dropdown + Active toggle
- Typeahead: Debounced input, dropdown of matching users, click to select
- Role options: Director, SuperDirector, ApiAuthorized, Ref Assignor, Store Admin, STPAdmin
- Form validation with inline error messages
- Save/Cancel buttons

### Step 10: Frontend - Confirmation Dialog
**Status**: [x] Complete
**Files to create**:
- `src/app/shared-ui/components/confirm-dialog/confirm-dialog.component.ts`
- `src/app/shared-ui/components/confirm-dialog/confirm-dialog.component.html`

**Features**:
- Reusable confirmation dialog (shared-ui, not module-specific)
- Props: title, message, confirmLabel, confirmVariant (danger/warning/primary), cancelLabel
- Uses `TsicDialogComponent` wrapper
- Output: confirmed / cancelled

### Step 11: Frontend - Routing
**Status**: [x] Complete
**Files to modify**:
- App routing config - add route `/:jobPath/admin/administrators` → `AdministratorManagementComponent`

### Step 12: Styling & Polish
**Status**: [x] Complete
**Details**:
- All colors via CSS variables
- Role badges: color-coded by role (using `bg-*-subtle` classes)
- Status chips: green for active, muted for inactive
- Hover states on table rows
- Smooth transitions for toolbar appear/disappear
- Mobile: table stacks or scrolls horizontally
- Test all 8 palettes

---

## 7. Dependencies

- Existing `TsicDialogComponent` (shared-ui)
- Existing `ToastService` (shared-ui)
- Existing `AuthService` (core) - for current user context
- Existing `RoleConstants` (Domain) - for role ID lookups
- Existing `Registrations` entity (Domain)
- Existing `AspNetUsers` / `AspNetRoles` entities (Domain)
- Existing "SuperUserOnly" authorization policy

## 8. Verification

- [ ] Backend builds (`dotnet build`)
- [ ] API endpoints respond correctly (test via Swagger)
- [ ] TypeScript models generated (run regeneration script)
- [ ] Frontend compiles (`ng build`)
- [ ] Table loads administrators for current job
- [ ] Add administrator via typeahead + role selection works
- [ ] Edit administrator (role change, active toggle) works
- [ ] Delete administrator with confirmation works
- [ ] Superuser rows cannot be edited/deleted/selected
- [ ] Multi-select + batch activate/inactivate works
- [ ] Toast notifications on success/error
- [ ] Responsive layout on mobile viewport
- [ ] Test with all 8 color palettes
