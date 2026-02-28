# Navigation System

## Overview

The navigation system provides configurable, role-based menus for the TSIC application. SuperUsers manage menus through the Nav Editor (`/:jobPath/admin/nav-editor`). The system supports platform-wide defaults per role with optional per-job overrides.

---

## Data Model

### Schema: `nav`

**`nav.Nav`** — One record per role (+ optional job override)

| Column | Type | Purpose |
|--------|------|---------|
| `NavId` | INT IDENTITY | PK |
| `RoleId` | NVARCHAR(450) | FK to AspNetRoles |
| `JobId` | UNIQUEIDENTIFIER NULL | NULL = platform default; set = job override |
| `Active` | BIT | Enable/disable entire nav for a role |
| `Modified` | DATETIME2 | Last modification timestamp |
| `ModifiedBy` | NVARCHAR(450) | User who last modified |

**`nav.NavItem`** — Two-level tree (root + children)

| Column | Type | Purpose |
|--------|------|---------|
| `NavItemId` | INT IDENTITY | PK |
| `NavId` | INT | FK to nav.Nav (CASCADE) |
| `ParentNavItemId` | INT NULL | NULL = root; set = child item |
| `Active` | BIT | Show/hide from rendered menu |
| `SortOrder` | INT | Display order within sibling group |
| `Text` | NVARCHAR(200) | Display label |
| `IconName` | NVARCHAR(100) NULL | Bootstrap icon name |
| `RouterLink` | NVARCHAR(500) NULL | Angular routerLink value |
| `NavigateUrl` | NVARCHAR(500) NULL | External URL |
| `Target` | NVARCHAR(20) NULL | Link target (_self, _blank) |

### Key Constraints

- **Two-level hierarchy only**: Root items (ParentNavItemId = NULL) and children (ParentNavItemId = root ID). No deeper nesting.
- **One platform default per role**: Enforced in service layer (not DB index — that breaks EF scaffold).
- **Job overrides**: One per role+job, enforced via `UQ_nav_Nav_Role_Job` unique index.
- **NavItemId is IDENTITY**: Auto-generated; must `SaveChanges()` after parent insert to get ID before creating children.

---

## Architecture

### Backend

```
NavController (API)
  ├── Public: GET /api/nav/merged (authenticated users)
  └── Editor: /api/nav/editor/* (SuperUserOnly)
        ↓
NavEditorService (Business Logic)
        ↓
INavEditorRepository → NavEditorRepository (Data Access)
        ↓
SqlDbContext → nav.Nav + nav.NavItem
```

### API Endpoints

| Method | Endpoint | Purpose |
|--------|----------|---------|
| GET | `/api/nav/merged` | Merged nav for current user's role+job |
| GET | `/api/nav/editor/defaults` | All platform default navs with items |
| POST | `/api/nav/editor/defaults` | Create platform default or job override |
| POST | `/api/nav/editor/defaults/ensure-all-roles` | Ensure all standard roles have navs |
| PUT | `/api/nav/editor/defaults/{navId}/active` | Toggle nav active state |
| POST | `/api/nav/editor/items` | Create nav item |
| PUT | `/api/nav/editor/items/{navItemId}` | Update nav item |
| DELETE | `/api/nav/editor/items/{navItemId}` | Delete nav item |
| PUT | `/api/nav/editor/items/reorder` | Reorder siblings |
| POST | `/api/nav/editor/items/cascade-route` | Cascade route change across roles |
| POST | `/api/nav/editor/items/clone-branch` | Clone Level 1 item + children to another role |
| POST | `/api/nav/editor/import-legacy` | Import from legacy JobMenus |
| GET | `/api/nav/editor/export-sql` | Export as idempotent SQL script |

### Frontend

```
src/app/views/menu-admin/
  ├── menu-admin.component.ts        ← Main editor component
  ├── menu-admin.component.html      ← WYSIWYG nav preview + dialogs
  ├── menu-admin.component.scss      ← Editor styles
  └── nav-item-form-dialog.component.ts ← Item create/edit dialog

src/app/core/services/
  └── nav-admin.service.ts           ← Signal-based HTTP service
```

---

## Platform Defaults vs Job Overrides

- **Platform defaults** (`JobId = NULL`): One per role. Defines the baseline menu for all jobs.
- **Job overrides** (`JobId = <specific job>`): Optional. Items are appended to the platform default when rendering.
- **Merge strategy**: `GetMergedNavAsync()` returns platform default items + job override items concatenated.

---

## Key Features

### Clone Branch (Level 1 Item to Another Role)

Copies a Level 1 nav item and its **active children** to a different role's nav:

1. Click the copy icon on any Level 1 item in the nav editor
2. Select target role from dropdown
3. If duplicate exists (same text, case-insensitive), confirm to replace or cancel
4. Backend creates new items with fresh IDs and `SortOrder` appended after existing items

**Endpoint**: `POST /api/nav/editor/items/clone-branch`
**Request**: `CloneBranchRequest { SourceNavItemId, TargetNavId, ReplaceExisting }`

### Custom Route Entry

The Angular Route mode supports two input modes via a toggle switch:

- **Dropdown** (default): Pick from known Angular routes discovered from the router config
- **Custom route** (toggle on): Free-text input for parameterized routes not in the dropdown

This is needed for routes like `reporting/Get_JobPlayers_TSICDAILY` which match the parameterized `reporting/:action` pattern — the router config scanner skips `:param` segments so these never appear in the dropdown. The `ClientMenuComponent` wildcard prefix matching already recognizes any `reporting/*` route as implemented.

When editing an existing item with a custom route, the toggle auto-enables so the value displays in the text input rather than showing a broken dropdown selection.

### Route Cascade

When editing an item's route, the editor detects matching items (same text + parent text) across other roles and offers to update them all in one action.

**Endpoint**: `POST /api/nav/editor/items/cascade-route`

### Legacy Menu Import

Imports items from the old `dbo.JobMenus` / `dbo.JobMenuItems` tables, translating Controller/Action paths to Angular RouterLink values via `LegacyRouteMap`.

### SQL Export

Generates an idempotent SQL script that recreates the full nav configuration. Used for deploying dev configurations to production.

**Script**: Run via the Export SQL button (dev mode only) or `GET /api/nav/editor/export-sql`.

---

## DTOs

| DTO | Purpose |
|-----|---------|
| `NavDto` / `NavItemDto` | Read-only merged nav for end users |
| `NavEditorNavDto` / `NavEditorNavItemDto` | Full nav with all items for editor |
| `CreateNavRequest` | Create platform default or job override |
| `CreateNavItemRequest` | Create nav item |
| `UpdateNavItemRequest` | Update nav item |
| `ReorderNavItemsRequest` | Reorder siblings |
| `CascadeRouteRequest` | Cascade route across roles |
| `CloneBranchRequest` | Clone Level 1 item + children to target role |
| `ToggleNavActiveRequest` | Toggle nav active state |
| `ImportLegacyMenuRequest` | Import from legacy menus |

---

## SQL Scripts

| Script | Purpose |
|--------|---------|
| `scripts/create-nav-schema.sql` | Creates `nav` schema + tables (v2, drops+recreates) |
| `scripts/seed-nav-defaults.sql` | Bootstraps sample nav data for dev |

---

## Important Rules

1. **NEVER edit scaffolded entity files** (`Nav.cs`, `NavItem.cs`) — fix at DB level, re-scaffold
2. **Two levels max** — root items + children; enforced in `CreateNavItemAsync()`
3. **Creating root items auto-creates a stub child** (inactive, text "new child") — this is intentional for the editor UX
4. **"One default per role" enforced in service layer**, NOT via filtered unique index (that breaks EF scaffold)
5. **Sequential awaits only** — no `Task.WhenAll` with shared DbContext
6. **SortOrder is 1-based sequential** — calculated via `GetSiblingCountAsync() + 1`
