# 014 — Customer Groups

> **Status**: Design Spec — Ready for Review
> **Date**: 2026-02-18
> **Keyword**: `CWCC Implement CUSTOMER-GROUPS`
> **Legacy reference**: `reference/TSIC-Unify-2024/TSIC-Unify/Controllers/Admin/CustomerGroupsController.cs`
> **Legacy view**: `reference/TSIC-Unify-2024/TSIC-Unify/Views/CustomerGroups/Index.cshtml`

---

## 1. Problem Statement

Customer Groups allow SuperUsers to roll up individual Customer entities into
named groups for reporting purposes. The legacy implementation:

- Uses raw `_context` access in the controller (no repository, no service)
- Embeds DTOs as inner classes inside the controller
- Relies on Syncfusion remote adaptor CRUD with inline editing
- Has no validation beyond duplicate-member prevention on the client side
- Filters available customers via a fragile year-range query (year ±1)
- Contains a commented-out treeview feature that was never completed

The new system replaces this with a clean REST API, repository-backed
persistence, proper server-side validation, and a modern Angular component
with a master-detail layout.

---

## 2. Design Decisions

### 2.1 Scope — Deliberately Small

This is a **low-complexity, high-value** feature. Two tables, two entity types,
pure CRUD. No rich text, no role-based field visibility, no complex business
logic. The entire feature is SuperUser-only.

### 2.2 Customer Source — All Active Customers

The legacy code filters "available customers" from the Jobs table by year
range (current year ±1). This is fragile — it couples customer availability
to whether they happen to own a job in a narrow time window.

**New approach**: Return all customers from the `Jobs.Customers` table. The
list is small (typically <100) and the SuperUser already knows which customers
they want to group. A client-side search/filter on the dropdown handles
discoverability. This is simpler, more correct, and eliminates the year-range
coupling.

### 2.3 Master-Detail Layout

```
┌──────────────────────────────────────────────────────────────┐
│  Customer Groups                                       [+ Add] │
├──────────────────────────────────────────────────────────────┤
│                          │                                   │
│  ┌─────────────────────┐ │  Members of "Northeast Region"    │
│  │ Northeast Region  ● │ │                                   │
│  │ Southeast Region    │ │  ┌──────────────────────┐  [+ Add] │
│  │ West Coast          │ │  │ Acme Sports Club     │  [×]    │
│  │ Midwest Alliance    │ │  │ Bay Area United      │  [×]    │
│  │                     │ │  │ Pacific FC           │  [×]    │
│  └─────────────────────┘ │  └──────────────────────┘         │
│                          │                                   │
│  [Rename] [Delete]       │                                   │
└──────────────────────────────────────────────────────────────┘
```

- **Left panel**: Selectable list of customer groups with add/rename/delete
- **Right panel**: Members of the selected group with add/remove
- **Add member**: Dropdown of available customers (excludes already-assigned)

This is cleaner than the legacy dual-grid approach and matches the natural
mental model of "select a group, manage its members."

### 2.4 Deletion Rules (Server-Enforced)

- **Delete a group**: Only allowed if the group has zero members. Return
  `409 Conflict` with a clear message if members exist. The frontend
  disables the delete button and shows a tooltip when members are present.
- **Remove a member**: Always allowed — removes the junction record.

### 2.5 Duplicate Prevention (Server-Enforced)

- **Duplicate group name**: Reject with `409 Conflict` on create or rename.
- **Duplicate member**: Reject with `409 Conflict` if the customer is already
  in the group. The frontend filters the dropdown to exclude assigned
  customers, but the backend validates independently.

---

## 3. Database Schema

### 3.1 Existing Tables (No Migrations Required)

Both tables already exist in the database and the Domain entities are wired
into the DbContext.

| Table | PK | Columns | Notes |
|---|---|---|---|
| `Jobs.CustomerGroups` | `Id` (int, identity) | `CustomerGroupName` (required) | Parent entity |
| `Jobs.CustomerGroupCustomers` | `Id` (int, identity) | `CustomerGroupId` (FK), `CustomerId` (FK) | Junction table |

**Relationships:**
```
CustomerGroups (1) ──→ (N) CustomerGroupCustomers (N) ←── (1) Customers
```

Both foreign keys use `DeleteBehavior.ClientSetNull` — the application
enforces referential integrity, not cascade delete.

### 3.2 Related Table (Read-Only)

| Table | Usage |
|---|---|
| `Jobs.Customers` | Lookup for customer names in dropdown and member display |

---

## 4. Backend Architecture

### 4.1 Layer Overview

```
CustomerGroupsController       (API — thin, delegates to service)
    ↓
ICustomerGroupService           (Application — validation, mapping)
    ↓
ICustomerGroupRepository        (Infrastructure — data access via EF Core)
    ↓
SqlDbContext                     (never touched outside repository)
```

### 4.2 Authorization Strategy

All endpoints are `[Authorize(Policy = "SuperUserOnly")]`. No role-based
field filtering needed — the entire feature is SuperUser-only.

### 4.3 DTOs (`TSIC.Contracts/Dtos/Admin/CustomerGroupDtos.cs`)

```csharp
namespace TSIC.Contracts.Dtos.Admin;

// ── Response DTOs ────────────────────────────────────────

public record CustomerGroupDto
{
    public required int Id { get; init; }
    public required string CustomerGroupName { get; init; }
    public required int MemberCount { get; init; }
}

public record CustomerGroupMemberDto
{
    public required int Id { get; init; }
    public required int CustomerGroupId { get; init; }
    public required Guid CustomerId { get; init; }
    public required string CustomerName { get; init; }
}

public record CustomerLookupDto
{
    public required Guid CustomerId { get; init; }
    public required string CustomerName { get; init; }
}

// ── Request DTOs ─────────────────────────────────────────

public record CreateCustomerGroupRequest
{
    public required string CustomerGroupName { get; init; }
}

public record RenameCustomerGroupRequest
{
    public required string CustomerGroupName { get; init; }
}

public record AddCustomerGroupMemberRequest
{
    public required Guid CustomerId { get; init; }
}
```

### 4.4 Repository Interface (`TSIC.Contracts/Repositories/ICustomerGroupRepository.cs`)

```csharp
namespace TSIC.Contracts.Repositories;

public interface ICustomerGroupRepository
{
    // ── Read ─────────────────────────────────────────────
    Task<List<CustomerGroupDto>> GetAllGroupsAsync(CancellationToken ct = default);
    Task<List<CustomerGroupMemberDto>> GetMembersAsync(int groupId, CancellationToken ct = default);
    Task<List<CustomerLookupDto>> GetAllCustomersAsync(CancellationToken ct = default);

    // ── Validation ───────────────────────────────────────
    Task<bool> GroupExistsAsync(int groupId, CancellationToken ct = default);
    Task<bool> GroupNameExistsAsync(string name, int? excludeGroupId = null, CancellationToken ct = default);
    Task<bool> MemberExistsAsync(int groupId, Guid customerId, CancellationToken ct = default);
    Task<int> GetMemberCountAsync(int groupId, CancellationToken ct = default);

    // ── Write ────────────────────────────────────────────
    void AddGroup(CustomerGroups group);
    Task<CustomerGroups?> GetGroupTrackedAsync(int groupId, CancellationToken ct = default);
    void RemoveGroup(CustomerGroups group);

    void AddMember(CustomerGroupCustomers member);
    Task<CustomerGroupCustomers?> GetMemberTrackedAsync(int memberId, CancellationToken ct = default);
    void RemoveMember(CustomerGroupCustomers member);

    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
```

### 4.5 Service Interface (`TSIC.Contracts/Services/ICustomerGroupService.cs`)

```csharp
namespace TSIC.Contracts.Services;

public interface ICustomerGroupService
{
    // ── Groups ───────────────────────────────────────────
    Task<List<CustomerGroupDto>> GetAllGroupsAsync(CancellationToken ct = default);
    Task<CustomerGroupDto> CreateGroupAsync(CreateCustomerGroupRequest req, CancellationToken ct = default);
    Task<CustomerGroupDto> RenameGroupAsync(int groupId, RenameCustomerGroupRequest req, CancellationToken ct = default);
    Task DeleteGroupAsync(int groupId, CancellationToken ct = default);

    // ── Members ──────────────────────────────────────────
    Task<List<CustomerGroupMemberDto>> GetMembersAsync(int groupId, CancellationToken ct = default);
    Task<CustomerGroupMemberDto> AddMemberAsync(int groupId, AddCustomerGroupMemberRequest req, CancellationToken ct = default);
    Task RemoveMemberAsync(int groupId, int memberId, CancellationToken ct = default);

    // ── Lookup ───────────────────────────────────────────
    Task<List<CustomerLookupDto>> GetAvailableCustomersAsync(int groupId, CancellationToken ct = default);
}
```

**`GetAvailableCustomersAsync`** returns all customers **minus** those already
in the specified group. This powers the "Add Member" dropdown with only valid
choices.

### 4.6 Controller (`TSIC.API/Controllers/CustomerGroupsController.cs`)

```csharp
[ApiController]
[Route("api/customer-groups")]
[Authorize(Policy = "SuperUserOnly")]
public class CustomerGroupsController : ControllerBase
{
    private readonly ICustomerGroupService _service;

    // ── Groups ───────────────────────────────────────────

    [HttpGet]
    // Returns List<CustomerGroupDto> — all groups with member counts

    [HttpPost]
    // Accepts CreateCustomerGroupRequest, returns CustomerGroupDto (201 Created)

    [HttpPut("{groupId:int}")]
    // Accepts RenameCustomerGroupRequest, returns CustomerGroupDto

    [HttpDelete("{groupId:int}")]
    // Returns 204 NoContent (or 409 if group has members)

    // ── Members ──────────────────────────────────────────

    [HttpGet("{groupId:int}/members")]
    // Returns List<CustomerGroupMemberDto>

    [HttpPost("{groupId:int}/members")]
    // Accepts AddCustomerGroupMemberRequest, returns CustomerGroupMemberDto (201 Created)

    [HttpDelete("{groupId:int}/members/{memberId:int}")]
    // Returns 204 NoContent

    // ── Lookup ───────────────────────────────────────────

    [HttpGet("{groupId:int}/available-customers")]
    // Returns List<CustomerLookupDto> — customers not yet in this group
}
```

**Endpoint summary: 8 endpoints**

| Verb | Route | Purpose |
|---|---|---|
| GET | `/api/customer-groups` | List all groups with member counts |
| POST | `/api/customer-groups` | Create a new group |
| PUT | `/api/customer-groups/{groupId}` | Rename a group |
| DELETE | `/api/customer-groups/{groupId}` | Delete an empty group |
| GET | `/api/customer-groups/{groupId}/members` | List members of a group |
| POST | `/api/customer-groups/{groupId}/members` | Add a customer to a group |
| DELETE | `/api/customer-groups/{groupId}/members/{memberId}` | Remove a member |
| GET | `/api/customer-groups/{groupId}/available-customers` | Customers not yet in this group |

---

## 5. Service Layer — Key Logic

### 5.1 Create Group

```csharp
public async Task<CustomerGroupDto> CreateGroupAsync(
    CreateCustomerGroupRequest req, CancellationToken ct)
{
    var trimmed = req.CustomerGroupName.Trim();
    if (string.IsNullOrWhiteSpace(trimmed))
        throw new ArgumentException("Group name is required.");

    if (await _repo.GroupNameExistsAsync(trimmed, ct: ct))
        throw new InvalidOperationException($"A group named '{trimmed}' already exists.");

    var entity = new CustomerGroups { CustomerGroupName = trimmed };
    _repo.AddGroup(entity);
    await _repo.SaveChangesAsync(ct);

    return new CustomerGroupDto
    {
        Id = entity.Id,
        CustomerGroupName = entity.CustomerGroupName,
        MemberCount = 0
    };
}
```

### 5.2 Delete Group (With Member Guard)

```csharp
public async Task DeleteGroupAsync(int groupId, CancellationToken ct)
{
    var group = await _repo.GetGroupTrackedAsync(groupId, ct)
        ?? throw new KeyNotFoundException($"Group {groupId} not found.");

    var memberCount = await _repo.GetMemberCountAsync(groupId, ct);
    if (memberCount > 0)
        throw new InvalidOperationException(
            $"Cannot delete group '{group.CustomerGroupName}' — it has {memberCount} member(s). Remove all members first.");

    _repo.RemoveGroup(group);
    await _repo.SaveChangesAsync(ct);
}
```

### 5.3 Add Member (With Duplicate Guard)

```csharp
public async Task<CustomerGroupMemberDto> AddMemberAsync(
    int groupId, AddCustomerGroupMemberRequest req, CancellationToken ct)
{
    if (!await _repo.GroupExistsAsync(groupId, ct))
        throw new KeyNotFoundException($"Group {groupId} not found.");

    if (await _repo.MemberExistsAsync(groupId, req.CustomerId, ct))
        throw new InvalidOperationException("This customer is already a member of this group.");

    var entity = new CustomerGroupCustomers
    {
        CustomerGroupId = groupId,
        CustomerId = req.CustomerId
    };
    _repo.AddMember(entity);
    await _repo.SaveChangesAsync(ct);

    // Return the full DTO with customer name
    var members = await _repo.GetMembersAsync(groupId, ct);
    return members.First(m => m.Id == entity.Id);
}
```

### 5.4 Available Customers (Exclude Already Assigned)

```csharp
public async Task<List<CustomerLookupDto>> GetAvailableCustomersAsync(
    int groupId, CancellationToken ct)
{
    var allCustomers = await _repo.GetAllCustomersAsync(ct);
    var currentMembers = await _repo.GetMembersAsync(groupId, ct);
    var assignedIds = currentMembers.Select(m => m.CustomerId).ToHashSet();

    return allCustomers
        .Where(c => !assignedIds.Contains(c.CustomerId))
        .OrderBy(c => c.CustomerName)
        .ToList();
}
```

---

## 6. Frontend Architecture

### 6.1 File Structure

```
src/app/views/admin/customer-groups/
├── customer-groups.component.ts       // Master-detail shell
├── customer-groups.component.html     // Template
├── customer-groups.component.scss     // Styles
└── customer-groups.service.ts         // HTTP + signal state
```

### 6.2 State Management

```typescript
@Injectable()
export class CustomerGroupsService {
    private readonly api = inject(CustomerGroupsApiService); // auto-generated

    // ── Groups ───────────────────────────────────
    readonly groups = signal<CustomerGroupDto[]>([]);
    readonly selectedGroup = signal<CustomerGroupDto | null>(null);

    // ── Members (for selected group) ─────────────
    readonly members = signal<CustomerGroupMemberDto[]>([]);
    readonly availableCustomers = signal<CustomerLookupDto[]>([]);

    // ── UI state ─────────────────────────────────
    readonly isLoading = signal(false);
    readonly isSaving = signal(false);
    readonly error = signal<string | null>(null);

    // ── Actions ──────────────────────────────────
    loadGroups(): void { ... }
    selectGroup(group: CustomerGroupDto): void { ... }  // loads members + available
    createGroup(name: string): void { ... }
    renameGroup(groupId: number, name: string): void { ... }
    deleteGroup(groupId: number): void { ... }
    addMember(customerId: string): void { ... }
    removeMember(memberId: number): void { ... }
}
```

### 6.3 Component Behavior

**Group selection** triggers loading members and available customers for that
group. The right panel is empty with a prompt until a group is selected.

**Inline rename**: Clicking a group name makes it editable (contenteditable or
input overlay). Enter confirms, Escape cancels. Alternatively, a "Rename"
button in the group action bar opens a simple input.

**Add group**: Button at top of left panel → inline input at top of list →
Enter to create, Escape to cancel.

**Add member**: Dropdown/combobox at top of right panel. Searchable, filtered
to exclude already-assigned customers. Selecting a customer immediately adds
them (no separate "Add" button needed).

**Remove member**: "×" button on each member row. Confirm via
`TsicDialogComponent` if desired, or immediate removal (given the action is
easily reversible by re-adding).

**Delete group**: Only enabled when member count is 0. Button shows tooltip
"Remove all members before deleting" when disabled.

### 6.4 Routing

```typescript
// In app.routes.ts — admin children
{
    path: 'customer-groups',
    data: { requireSuperUser: true },
    loadComponent: () => import('./views/admin/customer-groups/customer-groups.component')
        .then(m => m.CustomerGroupsComponent)
}
```

**Breadcrumb mappings**:
```typescript
ROUTE_TITLE_MAP['admin/customer-groups'] = 'Customer Groups';
ROUTE_WORKSPACE_MAP['admin/customer-groups'] = 'customer-groups';
```

### 6.5 Styling

- **Glassmorphic card** wrapping the full master-detail layout
- **Two-column layout**: `grid-template-columns: 1fr 2fr` at `≥768px`,
  stacked single column below
- **Group list**: Vertical list items with hover/active states using
  `var(--bs-primary)` for selected item
- **Member list**: Clean table or list with customer name and remove button
- **All spacing**: `var(--space-N)` tokens
- **All colors**: CSS variables from design system
- **Empty state**: Friendly message when no groups exist or no group is selected

---

## 7. Implementation Phases

### Phase 1 — Backend DTOs + Repository (Contracts + Infrastructure)

**Goal**: Data access layer complete and testable.

| Task | Files |
|---|---|
| Create DTO file | `Contracts/Dtos/Admin/CustomerGroupDtos.cs` |
| Create repository interface | `Contracts/Repositories/ICustomerGroupRepository.cs` |
| Implement repository | `Infrastructure/Repositories/CustomerGroupRepository.cs` |

**Deliverable**: Repository methods callable, all read queries use
`AsNoTracking()`, write operations follow tracked-entity pattern.

### Phase 2 — Service + Controller + DI (API layer)

**Goal**: All 8 endpoints callable via Swagger.

| Task | Files |
|---|---|
| Create service interface | `Contracts/Services/ICustomerGroupService.cs` |
| Implement service | `API/Services/Admin/CustomerGroupService.cs` |
| Create controller | `API/Controllers/CustomerGroupsController.cs` |
| Register DI | `Program.cs` — `AddScoped<ICustomerGroupRepository, CustomerGroupRepository>()` and `AddScoped<ICustomerGroupService, CustomerGroupService>()` |

**Deliverable**: Swagger shows all 8 endpoints. CRUD operations verified via
Swagger UI. Validation errors return proper HTTP status codes.

### Phase 3 — Frontend Component

**Goal**: Working Angular UI with full CRUD.

| Task | Files |
|---|---|
| Regenerate API models | `scripts/2-Regenerate-API-Models.ps1` |
| Create service | `views/admin/customer-groups/customer-groups.service.ts` |
| Create component | `views/admin/customer-groups/customer-groups.component.ts` |
| Create template | `views/admin/customer-groups/customer-groups.component.html` |
| Create styles | `views/admin/customer-groups/customer-groups.component.scss` |
| Add route | `app.routes.ts` |
| Add breadcrumb mapping | `breadcrumb.service.ts` |

**Deliverable**: Navigate to `/:jobPath/admin/customer-groups`, see
master-detail layout, create/rename/delete groups, add/remove members.

### Phase 4 — Polish + Verify

**Goal**: Production-ready.

| Task | Detail |
|---|---|
| Toast notifications | Success/error feedback on all mutations |
| Empty states | "No groups yet" / "Select a group" / "No members" messages |
| Keyboard navigation | Tab through controls, Enter to confirm |
| Responsive layout | Test at 1024px, 768px, 480px breakpoints |
| Palette testing | Verify all 8 palettes render correctly |
| `dotnet build` | Backend compiles clean |
| `ng build` | Frontend compiles clean |

---

## 8. Error Handling

| Condition | Exception | HTTP Status |
|---|---|---|
| Group not found | `KeyNotFoundException` | 404 Not Found |
| Duplicate group name | `InvalidOperationException` | 409 Conflict |
| Delete group with members | `InvalidOperationException` | 409 Conflict |
| Duplicate member assignment | `InvalidOperationException` | 409 Conflict |
| Empty/whitespace group name | `ArgumentException` | 400 Bad Request |
| Member record not found | `KeyNotFoundException` | 404 Not Found |

---

## 9. Constraints & Conventions

| Rule | Detail |
|---|---|
| Repository pattern | All data access via `ICustomerGroupRepository` — zero DbContext in service/controller |
| Sequential awaits | No `Task.WhenAll` — DbContext is not thread-safe |
| DTO pattern | `required` + `init` properties, object initializer syntax |
| No positional records | Use `{ get; init; }` style for all DTOs |
| Auto-generated models | Run `2-Regenerate-API-Models.ps1` after backend DTO changes |
| CSS variables only | No hardcoded colors, spacing, or shadows |
| 8px spacing grid | `var(--space-1)` through `var(--space-20)` |
| Signals for state | `signal<T>()` for component + service state |
| Observables for HTTP | `subscribe()` to HTTP calls, update signals in callback |
| OnPush detection | All components use `ChangeDetectionStrategy.OnPush` |
| `@if` / `@for` | Modern control flow syntax |
| Standalone components | No NgModules |
| Relative routerLinks | Never absolute — preserve `:jobPath` prefix |
| Breadcrumb updates | Add to `ROUTE_TITLE_MAP` and `ROUTE_WORKSPACE_MAP` |
| WCAG AA | 4.5:1 contrast minimum, keyboard navigable |

---

## 10. Files Summary

### New files (9)
1. `TSIC.Contracts/Dtos/Admin/CustomerGroupDtos.cs`
2. `TSIC.Contracts/Repositories/ICustomerGroupRepository.cs`
3. `TSIC.Contracts/Services/ICustomerGroupService.cs`
4. `TSIC.Infrastructure/Repositories/CustomerGroupRepository.cs`
5. `TSIC.API/Services/Admin/CustomerGroupService.cs`
6. `TSIC.API/Controllers/CustomerGroupsController.cs`
7. `src/app/views/admin/customer-groups/customer-groups.component.ts`
8. `src/app/views/admin/customer-groups/customer-groups.component.html`
9. `src/app/views/admin/customer-groups/customer-groups.component.scss`

### Modified files (3)
1. `TSIC.API/Program.cs` — DI registration (2 lines)
2. `src/app/.../app.routes.ts` — admin child route
3. `src/app/.../breadcrumb.service.ts` — route title/workspace maps

---

## 11. Success Criteria

- [ ] All 8 endpoints return correct data via Swagger
- [ ] Create group with duplicate name returns 409
- [ ] Delete group with members returns 409
- [ ] Add duplicate member returns 409
- [ ] Master-detail layout loads groups and members correctly
- [ ] Add/remove members updates the available customers dropdown
- [ ] Rename group updates the group list immediately
- [ ] All 8 color palettes render correctly
- [ ] Responsive layout works at 1024px, 768px, 480px
- [ ] No hardcoded colors, spacing, or shadows in SCSS
- [ ] Zero `SqlDbContext` references outside repository
- [ ] All DTOs use `required` + `init` pattern
