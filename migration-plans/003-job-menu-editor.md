# Job Menu Editor — Admin CRUD Page

## Context
The legacy `Menu/Admin` page (TSIC-Unify-2024) uses jqGrid with right-click context menus to manage per-job, per-role menus. It's fully functional but clunky. This port replaces it with a modern Angular admin page using inline action buttons, CDK drag-drop reordering, and the established admin UI patterns (signals, TsicDialog modals, Bootstrap styling). The database entities (`JobMenus`, `JobMenuItems`) already exist in EF Core. A read-only `IMenuRepository` already exists for rendering menus — we need a separate admin repository for write operations.

Full plan saved to: `migration-plans/003-job-menu-editor.md`

## Legacy Behavior Summary (from `Controllers/Admin/MenuController.cs`)
- **3-level hierarchy**: Role menus (Level 0) → Parent items (Level 1) → Child items (Level 2)
- **Level 0**: Cannot add/delete. Can only toggle `Active`.
- **Level 1**: Add (auto-creates stub child), edit (Text, Active, Index), delete
- **Level 2**: Add, edit (Text, Active, Controller, Action, NavigateUrl, Index), delete
- **Delete logic**: Hard delete if siblings exist; soft delete (Active=false) if last sibling
- **CreateAllRoleMenus()**: Auto-creates menu entries for 6 roles (Superuser, Director, Staff, Player, ClubRep, Anonymous) with `MenuTypeId=6`, `Active=false`, plus stub parent/child items
- **Auth**: `[Authorize(Policy = "AdminOnly")]` — but new codebase uses `SuperUserOnly` for admin pages

## Plan

### Phase 1: Backend — DTOs

**File: `src/backend/TSIC.Contracts/Dtos/MenuAdminDtos.cs`** (CREATE)
- `MenuAdminDto` — role menu with nested items tree: `MenuId`, `JobId`, `RoleId`, `RoleName`, `Active`, `MenuTypeId`, `Items: List<MenuItemAdminDto>`
- `MenuItemAdminDto` — full item details: `MenuItemId`, `MenuId`, `ParentMenuItemId`, `Text`, `IconName`, `RouterLink`, `NavigateUrl`, `Controller`, `Action`, `Target`, `Active`, `Index`, `Children: List<MenuItemAdminDto>`
- `CreateMenuItemRequest` — `MenuId`, `ParentMenuItemId?`, `Text`, `Active`, `IconName?`, `RouterLink?`, `NavigateUrl?`, `Controller?`, `Action?`, `Target?`
- `UpdateMenuItemRequest` — same fields minus MenuId/ParentMenuItemId (those don't change)
- `UpdateMenuActiveRequest` — `Active` only
- `ReorderMenuItemsRequest` — `MenuId`, `ParentMenuItemId?`, `OrderedItemIds: List<Guid>`
- All use `required` + `init` pattern per project standards

### Phase 2: Backend — Repository (extend existing)

**File: `src/backend/TSIC.Contracts/Repositories/IMenuRepository.cs`** (EDIT — add admin methods)
**File: `src/backend/TSIC.Infrastructure/Repositories/MenuRepository.cs`** (EDIT — implement admin methods)

Add to existing `IMenuRepository` / `MenuRepository`:
```
GetAllMenusForJobAsync(jobId) → List<JobMenus> (include Role nav for name)
GetMenuByIdAsync(menuId) → JobMenus? (tracked for updates)
GetMenuItemByIdAsync(menuItemId) → JobMenuItems? (tracked)
GetMenuItemsByMenuIdAsync(menuId) → List<JobMenuItems> (AsNoTracking, includes inactive)
GetSiblingItemsAsync(menuId, parentMenuItemId?) → List<JobMenuItems> (tracked for reorder)
GetSiblingCountAsync(menuId, parentMenuItemId?) → int
GetExistingMenuRoleIdsForJobAsync(jobId) → List<string>
AddMenu(JobMenus) / AddMenuItem(JobMenuItems) / RemoveMenuItem(JobMenuItems)
SaveChangesAsync()
```
- `AsNoTracking()` for reads, tracked entities for writes
- Include `Role` navigation on `GetAllMenusForJobAsync` for role name display

### Phase 3: Backend — Service

**File: `src/backend/TSIC.API/Services/Admin/IMenuAdminService.cs`** (CREATE)
**File: `src/backend/TSIC.API/Services/Admin/MenuAdminService.cs`** (CREATE)

Key methods:
- `GetAllMenusAsync(jobId)` — loads all menus + items, builds hierarchical tree (root items where ParentMenuItemId==null, children nested). **Includes inactive items** (admin needs full view).
- `ToggleMenuActiveAsync(menuId, active, userId)` — find menu, update Active/Modified/LebUserId
- `CreateMenuItemAsync(jobId, request, userId)`:
  - If ParentMenuItemId==null (Level 1): create parent, auto-create stub child (`Text="new child"`, `Active=false`, `Index=1`)
  - If ParentMenuItemId set (Level 2): create child, Index = siblingCount + 1
- `UpdateMenuItemAsync(menuItemId, request, userId)` — update properties, set Modified/LebUserId
- `DeleteMenuItemAsync(menuItemId)` — if siblingCount > 1: hard delete. If == 1: soft delete (Active=false)
- `ReorderMenuItemsAsync(request, userId)` — for each ID in OrderedItemIds, set Index = position+1. Set Modified timestamps.
- `EnsureAllRoleMenusAsync(jobId, userId)` — check which of 6 roles (from `RoleConstants`) are missing menus, create menu + stub parent + stub child for each. Uses `MenuTypeId=6`.

### Phase 4: Backend — Controller

**File: `src/backend/TSIC.API/Controllers/MenuAdminController.cs`** (CREATE)
- `[ApiController]`, `[Route("api/menu-admin")]`, `[Authorize(Policy = "SuperUserOnly")]`
- All endpoints derive jobId/userId from JWT claims (never from parameters)
- `GET /menus` — get all role menus with items tree
- `PUT /menus/{menuId:guid}/active` — toggle active, body: `UpdateMenuActiveRequest`
- `POST /items` — create item, body: `CreateMenuItemRequest`
- `PUT /items/{menuItemId:guid}` — update item, body: `UpdateMenuItemRequest`
- `DELETE /items/{menuItemId:guid}` — delete item
- `PUT /items/reorder` — reorder siblings, body: `ReorderMenuItemsRequest`
- `POST /menus/ensure-all-roles` — auto-create missing role menus

### Phase 5: Backend — DI Registration

**File: `src/backend/TSIC.API/Program.cs`** (EDIT)
- Add `builder.Services.AddScoped<IMenuAdminService, MenuAdminService>();` near line 127
- (No new repository registration needed — existing `IMenuRepository` already registered)

### Phase 6: Frontend — Service & Models

**File: `src/frontend/tsic-app/src/app/views/admin/menu-editor/services/menu-admin.service.ts`** (CREATE)
- `getMenus(): Observable<MenuAdminDto[]>`
- `toggleMenuActive(menuId, active): Observable<void>`
- `createMenuItem(request): Observable<MenuItemAdminDto>`
- `updateMenuItem(menuItemId, request): Observable<MenuItemAdminDto>`
- `deleteMenuItem(menuItemId): Observable<void>`
- `reorderItems(request): Observable<void>`
- `ensureAllRoleMenus(): Observable<void>`

**File: `src/frontend/tsic-app/src/app/views/admin/menu-editor/models/menu-admin.models.ts`** (CREATE)
- Local TypeScript interfaces matching backend DTOs (replaced by `@core/api` after model regeneration)

### Phase 7: Frontend — WYSIWYG Menu Editor Component

**Design approach**: Instead of a flat tree-list editor, render the menu **as it actually appears** to users (mirroring the `client-menu` sidebar layout) with edit affordances overlaid. Admin selects a role from a dropdown, sees that role's menu rendered visually, and edits it in-place.

**File: `src/frontend/tsic-app/src/app/views/admin/menu-editor/menu-editor.component.ts`** (CREATE)
- Standalone, OnPush, signals for all state
- `inject()` for MenuAdminService, JobService, ToastService
- Effect to reload on job change
- State: `menus`, `selectedRoleId`, `isLoading`, `errorMessage`, `expandedParents: Set<string>`
- Modal state: `showItemModal`, `modalMode`, `editTarget`, `showDeleteConfirm`, `deleteTarget`
- Computed: `selectedMenu` (derived from menus + selectedRoleId), `roleOptions` (roles that have menus)
- Methods: `loadMenus()`, `onRoleChange()`, `toggleMenuActive()`, `toggleExpand()`, `openAddParent()`, `openAddChild()`, `openEdit()`, `confirmDelete()`, `onDropParent()`, `onDropChild()`, `ensureAllRoles()`
- CDK drag-drop: separate `cdkDropList` for Level 1 items and per-parent Level 2 items (same-level only)

**File: `src/frontend/tsic-app/src/app/views/admin/menu-editor/menu-editor.component.html`** (CREATE)

Layout:
```
┌─────────────────────────────────────────────────────────┐
│ Menu Editor                    [Ensure All Roles] [⟳]   │
│ Role: [▼ Superuser     ]    ☐ Menu Active   [+ Add Top] │
├─────────────────────────────────────────────────────────┤
│                                                         │
│  WYSIWYG Menu Preview (mirrors client-menu sidebar)     │
│  ┌───────────────────────────────────────────────────┐  │
│  │ ⊞ Dashboard                    [✏] [+child] [🗑] │  │
│  │   ├ Home Page                        [✏] [🗑]    │  │
│  │   └ My Profile                       [✏] [🗑]    │  │
│  │ ⊞ Reports                      [✏] [+child] [🗑] │  │
│  │ ⊟ Settings  (inactive)         [✏] [+child] [🗑] │  │
│  │   └ Job Config  (inactive)           [✏] [🗑]    │  │
│  └───────────────────────────────────────────────────┘  │
│                                                         │
│  Inactive items shown dimmed/strikethrough               │
│  Hover reveals drag handle + action buttons              │
│  CDK drag-drop for same-level reordering                 │
│                                                         │
└─────────────────────────────────────────────────────────┘
```

Toolbar area (above menu preview):
- Role selector: `<select>` dropdown populated from menus array (role names)
- Menu active toggle: `form-check form-switch` for selected menu's Active state
- "Add Top-Level Item" button
- "Ensure All Roles" button (creates missing role menus)
- Refresh button

WYSIWYG menu preview area:
- Mirrors `client-menu` mobile sidebar layout (vertical nav, collapsible parents, indented children)
- Uses same CSS variable system (`--brand-surface`, `--brand-text`, `--brand-border`, etc.)
- Parent items: icon + text + chevron toggle, expandable to show children
- Child items: indented under parent with `border-left` accent line (matches sidebar)
- Inactive items: reduced opacity + strikethrough text + "(inactive)" badge
- Hover state: reveals action button group (edit, add-child for parents, delete) and drag handle (`bi-grip-vertical`)
- CDK drag-drop: separate `cdkDropList` for Level 1 items and per-parent Level 2 items
- Empty state when no items ("No menu items. Click '+ Add Top-Level Item' to get started.")
- Loading spinner, error alert (matching admin-management patterns)

**File: `src/frontend/tsic-app/src/app/views/admin/menu-editor/menu-editor.component.scss`** (CREATE)
- Menu preview card styled to match `client-menu` offcanvas sidebar appearance
- Nav items: `--brand-text`, `--font-weight-medium`, `--radius-md` border-radius
- Children: left border accent (`rgba(var(--bs-primary-rgb), 0.1)`), indented `ms-5`
- Inactive items: `opacity: 0.45`, `text-decoration: line-through`
- Hover: reveals action buttons (hidden by default, shown on `:hover`)
- Drag: grip cursor, CDK preview shadow, placeholder opacity
- All colors/spacing via CSS variables (no hardcoded values)

### Phase 8: Frontend — Item Form Modal

**File: `src/frontend/tsic-app/src/app/views/admin/menu-editor/components/menu-item-form-modal.component.ts`** (CREATE)
- Inline template using `TsicDialogComponent` (`size="md"`)
- Inputs: `mode: 'add-parent' | 'add-child' | 'edit'`, `item?`, `menuId`
- Outputs: `close`, `saved`
- Form fields (FormsModule + ngModel):
  - **Text** (always shown)
  - **Active** toggle (always shown)
  - **IconName** (always shown, max 20 chars)
  - **Controller** (Level 2 / edit only)
  - **Action** (Level 2 / edit only)
  - **NavigateUrl** (Level 2 / edit only)
  - **RouterLink** (Level 2 / edit only)
  - **Target** (Level 2 / edit only — dropdown: `_self`, `_blank`)
- Save button calls service, emits `saved`, closes modal
- Validation: Text required

### Phase 9: Frontend — Route

**File: `src/frontend/tsic-app/src/app/app.routes.ts`** (EDIT)
- Add under `admin` children (alongside profile-editor, theme-editor):
  ```typescript
  { path: 'menu-editor', loadComponent: () => import('./views/admin/menu-editor/menu-editor.component').then(m => m.MenuEditorComponent) }
  ```
- Add legacy-compatible route (matching DB menu item `Controller=Menu, Action=Admin`):
  ```typescript
  { path: 'menu/admin', canActivate: [authGuard], data: { requireSuperUser: true }, loadComponent: () => import('./views/admin/menu-editor/menu-editor.component').then(m => m.MenuEditorComponent) }
  ```

### Phase 10: Post-Build — API Model Regeneration
- Run `.\scripts\2-Regenerate-API-Models.ps1`
- Switch imports from local models to `@core/api`
- Delete `models/menu-admin.models.ts`

## Files Summary

| File | Action |
|------|--------|
| `TSIC.Contracts/Dtos/MenuAdminDtos.cs` | Create |
| `TSIC.Contracts/Repositories/IMenuRepository.cs` | Edit (add admin methods) |
| `TSIC.Infrastructure/Repositories/MenuRepository.cs` | Edit (implement admin methods) |
| `TSIC.API/Services/Admin/IMenuAdminService.cs` | Create |
| `TSIC.API/Services/Admin/MenuAdminService.cs` | Create |
| `TSIC.API/Controllers/MenuAdminController.cs` | Create |
| `TSIC.API/Program.cs` | Edit (1 DI line) |
| `views/admin/menu-editor/services/menu-admin.service.ts` | Create |
| `views/admin/menu-editor/models/menu-admin.models.ts` | Create (temporary) |
| `views/admin/menu-editor/menu-editor.component.ts` | Create |
| `views/admin/menu-editor/menu-editor.component.html` | Create |
| `views/admin/menu-editor/menu-editor.component.scss` | Create |
| `views/admin/menu-editor/components/menu-item-form-modal.component.ts` | Create |
| `app.routes.ts` | Edit (2 routes) |

## Key Design Decisions

1. **WYSIWYG menu preview** — renders the menu as it actually appears to users (mirrors `client-menu` sidebar), with inline edit affordances. Far superior to legacy jqGrid tree-list approach.
2. **Role dropdown** (not tabs) — admin selects from roles that have menus, sees that role's menu rendered visually
3. **Extend existing `IMenuRepository`** — add admin methods (write ops, include-inactive queries) to existing repo rather than creating a separate admin repo
4. **Ordered ID list reorder** (not legacy index swap) — sends full sibling order after drag-drop, backend assigns sequential indexes. Simpler and supports multi-position moves.
5. **CDK drag-drop** (not Syncfusion TreeView) — same-level reordering only, matches existing profile-editor pattern, cohesive with Bootstrap styling
6. **Auto-stub child on parent creation** — preserves legacy behavior; menus need at least one child to render
7. **Soft delete protection** — last sibling gets deactivated instead of deleted (prevents orphaning)
8. **SuperUserOnly policy** — matches `AdministratorsController` pattern (legacy used `AdminOnly`)
9. **Role constants from `RoleConstants.cs`** — Superuser, Director, Staff, Player, ClubRep, Anonymous (6 roles with GUIDs)

## Verification

1. `dotnet build` — backend compiles
2. `ng build` — frontend compiles
3. Navigate to `/{jobPath}/menu/admin` — page loads, role dropdown populated
4. Select a role — WYSIWYG menu preview renders matching sidebar layout
5. "Ensure All Roles" creates missing menus
6. Add parent item → stub child auto-created
7. Add child item → appears under parent in sidebar preview
8. Edit item → properties persist
9. Delete item → hard delete (has siblings) or soft delete (last sibling)
10. Drag-drop reorder → index values update correctly
11. Toggle menu active/inactive → persists, inactive items shown dimmed
12. Menu item with `Controller=Menu, Action=Admin` no longer shows "Coming Soon" badge
