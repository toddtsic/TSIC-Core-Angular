# Migration Plan 009-1: Fields/Index → Manage Fields

## Context

The Fields management page is the **first step** in the scheduling pipeline. Before any pairings, timeslots, or schedules can be created, administrators must define the physical playing fields and assign them to their league-season. Fields have addresses, geospatial coordinates, and capacity metadata that drive the entire downstream scheduling engine.

The legacy page uses a **two-panel jqGrid layout**: "All Fields" (global) on the left and "League-Season Fields" (assigned) on the right. Fields are swapped between panels to activate/deactivate them for the current league-season.

This interface follows the same **dual-panel swapper pattern** already established by Roster Swapper and Pool Assignment — two side-by-side panels with per-row and batch transfer operations, checkbox selection, sortable/filterable columns, frozen columns, and capacity indicators.

**Legacy URL:** `/Fields/Index` (Controller=Fields, Action=Index)

**Legacy Controller:** `reference/TSIC-Unify-2024/TSIC-Unify/Controllers/Scheduling/FieldsController.cs`
**Legacy View:** `reference/TSIC-Unify-2024/TSIC-Unify/Views/Fields/Index.cshtml`

---

## 1. Legacy Strengths (Preserve These!)

- **Two-tier model** — global field library (shared across all leagues) + per-league-season assignment (swap in/out)
- **Full address data** — Name, Address, City, State, ZIP, Directions stored per field
- **Geospatial support** — Latitude, Longitude, and `Location` (NetTopologySuite Geometry) for map integration
- **Role-aware visibility** — SuperUsers see all fields globally; Directors see only fields historically used by any of their jobs
- **System field filtering** — Fields prefixed with `*` are system/reserved and excluded from scheduling dropdowns
- **Simple swap metaphor** — click a button to add/remove a field from the current league-season
- **Audit trail** — every edit tracks `LebUserId` and `Modified` timestamp

## 2. Legacy Pain Points (Fix These!)

- **jqGrid dependency** — heavy jQuery, poor mobile, dated styling
- **No inline validation** — form submits with invalid data (e.g., blank field name)
- **No map preview** — address data exists but no embedded map for verification
- **No bulk operations** — fields must be swapped one at a time
- **No search/filter** — large field libraries (50+ fields) have no search capability
- **Direct SqlDbContext** — controller accesses database directly, violating repository pattern
- **No geocoding** — latitude/longitude must be entered manually; no address-to-coordinate lookup

## 3. Modern Vision

**Recommended UI: Dual-Panel Swapper (matching Roster Swapper & Pool Assignment)**

Follow the identical dual-panel pattern established by the existing swapper components: two equal `1fr 1fr` grid panels, each with a panel-header (controls), panel-body (scrollable table with frozen columns), and panel-footer (batch action button). Per-row arrow buttons for single-field transfer, checkbox multi-select for batch operations.

```
┌──────────────────────────────────────────────────────────────────────────────┐
│  Manage Fields                                                    [+ New]   │
├──────────────────────────────────────────────────────────────────────────────┤
│                                                                              │
│  ┌─ Available Fields ─────────────────┐  ┌─ League-Season Fields ──────────┐│
│  │ panel-header                       │  │ panel-header                    ││
│  │  (not assigned to this season)     │  │  (active for scheduling)        ││
│  │  🔍 [Filter...              ]     │  │  🔍 [Filter...            ]    ││
│  │  4 fields                          │  │  6 fields                       ││
│  ├────────────────────────────────────┤  ├─────────────────────────────────┤│
│  │ panel-body (scrollable)            │  │ panel-body (scrollable)         ││
│  │ ┌──────────────────────────────┐  │  │ ┌────────────────────────────┐ ││
│  │ │☐│ → │ Field Name  │ City    │  │  │ │☐│ ← │ Field Name │ City   │ ││
│  │ ├──────────────────────────────┤  │  │ ├────────────────────────────┤ ││
│  │ │☐│ → │ Oak Park #3 │ Austin  │  │  │ │☐│ ← │ Cedar Pk A │ Cedar  │ ││
│  │ │☐│ → │ Riverside   │ Houston │  │  │ │☐│ ← │ Lakeline   │ Austin │ ││
│  │ │☐│ → │ Memorial    │ Dallas  │  │  │ │☐│ ← │ Round Rock │ RR     │ ││
│  │ │☐│ → │ Zilker #1   │ Austin  │  │  │ │☐│ ← │ Old Settle │ RR     │ ││
│  │ │                              │  │  │ │☐│ ← │ Kelly Rees │ Austin │ ││
│  │ │                              │  │  │ │☐│ ← │ Brushy Crk │ RR    │ ││
│  │ └──────────────────────────────┘  │  │ └────────────────────────────┘ ││
│  ├────────────────────────────────────┤  ├─────────────────────────────────┤│
│  │ panel-footer                       │  │ panel-footer                    ││
│  │  Assign Selected → (2)            │  │  ← Remove Selected (1)         ││
│  └────────────────────────────────────┘  └─────────────────────────────────┘│
│                                                                              │
│  ── Field Detail (shown when row clicked in either panel) ────────────────  │
│  ┌──────────────────────────────────────────────────────────────────────────┐│
│  │  Name:    [Cedar Park Complex A     ]    Address: [1890 N Lakeline    ] ││
│  │  City:    [Cedar Park               ]    State:   [TX ]  ZIP: [78613 ] ││
│  │  Directions: [Take 183 north, exit Lakeline...                        ] ││
│  │  Lat:     [30.5245   ]  Lng: [-97.8201  ]                              ││
│  │                                                 [Save]  [Delete]        ││
│  └──────────────────────────────────────────────────────────────────────────┘│
└──────────────────────────────────────────────────────────────────────────────┘
```

**Pattern alignment with existing swappers:**

| Pattern | Roster Swapper | Pool Assignment | Fields (this) |
|---------|---------------|-----------------|---------------|
| Panel layout | `1fr 1fr` grid | `1fr 1fr` grid | `1fr 1fr` grid |
| Panel structure | header/body/footer | header/body/footer | header/body/footer |
| Per-row transfer | `→` arrow button | `→` arrow button | `→` / `←` arrow button |
| Batch transfer | "Swap Selected (N)" | "Move Selected (N)" | "Assign Selected → (N)" / "← Remove Selected (N)" |
| Selection | Checkbox + select-all | Checkbox + select-all | Checkbox + select-all |
| Frozen columns | Checkbox, row#, swap btn | Checkbox, row#, swap btn | Checkbox, swap btn |
| Filter | Text input in panel-header | Text input in panel-header | Text input in panel-header |
| Sorting | Click column header | Click column header | Click column header |
| Empty state | `bi-inbox` icon + message | `bi-inbox` icon + message | `bi-inbox` icon + message |
| Loading spinner | Per-row on `swappingId` | Per-row on `swappingId` | Per-row on `swappingId` |
| CSS variables | Full compliance | Full compliance | Full compliance |
| Change detection | OnPush + standalone | OnPush + standalone | OnPush + standalone |

**Key differences from existing swappers:**
- **No dropdown selector** — both panels are always populated (available vs. assigned) rather than selecting a source/target from a dropdown. This simplifies the UI — there's only one global field library and one league-season context.
- **Detail editor below** — clicking a row in either panel shows an editable detail form below the panels (for editing field address/directions). This doesn't exist in Roster Swapper or Pool Assignment.
- **Create button** — `[+ New]` in the header opens the detail editor in create mode.
- **Transfer is immediate** — no preview modal needed (unlike Pool Assignment's fee preview). Assigning/removing a field has no financial side effects.

**Key improvements over legacy:**
- ✅ Multi-select for bulk assign/remove (legacy: one at a time)
- ✅ Text filter in each panel (handle 50+ fields easily)
- ✅ Sortable columns (field name, city)
- ✅ Inline detail editor with validation below panels
- ✅ Consistent UX with Roster Swapper and Pool Assignment
- ✅ Repository pattern compliance
- ✅ Glassmorphic panel styling via CSS variables
- ✅ Responsive: panels stack vertically on mobile (`@media max-width: 767.98px`)

**UI Recommendation:** Consider adding a small embedded map (Google Maps or Leaflet) next to the detail editor when lat/lng are populated. This is a nice-to-have — not blocking for MVP.

---

## 4. Security

- **Authorization:** `[Authorize(Policy = "AdminOnly")]` on all endpoints
- **Scoping:** `jobId`, `leagueId`, `season` derived from JWT claims — never route params
- **Director field visibility:** Directors see only fields that have been **historically used by any of their jobs** — i.e., fields that appear in `FieldsLeagueSeason` or `Schedule` for any job belonging to the Director's organization. This is NOT based on `LebUserId` on the Fields record. SuperUsers see all fields.
- **System fields:** Fields with names starting with `*` are filtered from the available/assigned panels (backend enforced)

---

## 5. Database Entities

### Fields (primary)
| Column | Type | Notes |
|--------|------|-------|
| `FieldId` | Guid (PK) | |
| `FName` | string | Field name |
| `Address` | string | Street address |
| `City` | string | |
| `State` | string | |
| `Zip` | string | |
| `Directions` | string | Free text driving directions |
| `Latitude` | double? | |
| `Longitude` | double? | |
| `Location` | Geometry? | NetTopologySuite geospatial |
| `LebUserId` | Guid | Owner/creator |
| `Modified` | DateTime | Audit timestamp |

### FieldsLeagueSeason (junction)
| Column | Type | Notes |
|--------|------|-------|
| `FlsId` | Guid (PK) | |
| `FieldId` | Guid (FK) | → Fields |
| `LeagueId` | Guid (FK) | → Leagues |
| `Season` | string | |
| `BActive` | bool? | Active for scheduling |
| `LebUserId` | string | |
| `Modified` | DateTime? | |

---

## 6. Business Rules

1. **Global vs. League-Season**: A field exists globally (Fields table) and can be assigned to multiple league-seasons (FieldsLeagueSeason junction). Creating a field doesn't auto-assign it.
2. **System fields**: Names starting with `*` are system/reserved — hidden from both panels, excluded from scheduling dropdowns.
3. **Director scoping**: Directors see only fields historically used by any of their jobs (fields appearing in `FieldsLeagueSeason` or `Schedule` joined through any job belonging to the Director's customer). SuperUsers see all fields globally.
4. **Delete cascade**: Deleting a field from the global library should only be allowed if no league-season references exist. Deleting from league-season just removes the junction record.
5. **Active flag**: `FieldsLeagueSeason.BActive` controls whether a field appears in downstream scheduling tools (Timeslots, Schedule Division).
6. **Assign is immediate**: Unlike Pool Assignment (which previews fee impact), field assignment has no financial side effects — transfers execute immediately with per-row spinner feedback, matching the Roster Swapper pattern.

---

## 7. Implementation Steps

### Phase 1: Backend — DTOs

**File:** `TSIC.Contracts/Dtos/Scheduling/FieldDtos.cs`

```csharp
// ── Response DTOs ──

public record FieldDto
{
    public required Guid FieldId { get; init; }
    public required string FName { get; init; }
    public string? Address { get; init; }
    public string? City { get; init; }
    public string? State { get; init; }
    public string? Zip { get; init; }
    public string? Directions { get; init; }
    public double? Latitude { get; init; }
    public double? Longitude { get; init; }
}

public record LeagueSeasonFieldDto
{
    public required Guid FlsId { get; init; }
    public required Guid FieldId { get; init; }
    public required string FName { get; init; }
    public string? City { get; init; }
    public string? State { get; init; }
    public bool? BActive { get; init; }
}

public record FieldManagementResponse
{
    /// Fields NOT assigned to current league-season (available for assignment)
    public required List<FieldDto> AvailableFields { get; init; }
    /// Fields assigned to current league-season
    public required List<LeagueSeasonFieldDto> AssignedFields { get; init; }
}

// ── Request DTOs ──

public record CreateFieldRequest
{
    public required string FName { get; init; }
    public string? Address { get; init; }
    public string? City { get; init; }
    public string? State { get; init; }
    public string? Zip { get; init; }
    public string? Directions { get; init; }
    public double? Latitude { get; init; }
    public double? Longitude { get; init; }
}

public record UpdateFieldRequest
{
    public required Guid FieldId { get; init; }
    public required string FName { get; init; }
    public string? Address { get; init; }
    public string? City { get; init; }
    public string? State { get; init; }
    public string? Zip { get; init; }
    public string? Directions { get; init; }
    public double? Latitude { get; init; }
    public double? Longitude { get; init; }
}

public record AssignFieldsRequest
{
    /// Field IDs to assign to the current league-season
    public required List<Guid> FieldIds { get; init; }
}

public record RemoveFieldsRequest
{
    /// Field IDs to remove from the current league-season
    public required List<Guid> FieldIds { get; init; }
}
```

### Phase 2: Backend — Repository

**Interface:** `TSIC.Contracts/Repositories/IFieldRepository.cs`

```
Methods:
- GetAvailableFieldsAsync(Guid leagueId, string season, List<Guid> directorJobIds, bool isSuperUser) → List<Fields>
  // SuperUser: all non-system fields not assigned to this league-season
  // Director: fields historically used by any job in directorJobIds, not assigned to this league-season
- GetLeagueSeasonFieldsAsync(Guid leagueId, string season) → List<FieldsLeagueSeason> (with Field nav prop)
- GetFieldByIdAsync(Guid fieldId) → Fields?
- CreateFieldAsync(Fields field) → Fields
- UpdateFieldAsync(Fields field) → void
- DeleteFieldAsync(Guid fieldId) → bool (false if referenced)
- AssignFieldsToLeagueSeasonAsync(Guid leagueId, string season, List<Guid> fieldIds, string userId) → void
- RemoveFieldsFromLeagueSeasonAsync(Guid leagueId, string season, List<Guid> fieldIds) → void
- IsFieldReferencedAsync(Guid fieldId) → bool (check FieldsLeagueSeason, Schedule, TimeslotsLeagueSeasonFields)
```

**Implementation:** `TSIC.Infrastructure/Repositories/FieldRepository.cs`

Director scoping query logic:
```csharp
// Director sees fields that have been used historically by any of their jobs
var directorFieldIds = await _context.FieldsLeagueSeason
    .Where(fls => directorJobIds.Contains(
        _context.JobLeagues
            .Where(jl => jl.LeagueId == fls.LeagueId)
            .Select(jl => jl.JobId)
            .FirstOrDefault()))
    .Select(fls => fls.FieldId)
    .Union(
        _context.Schedule
            .Where(s => directorJobIds.Contains(s.JobId))
            .Select(s => s.FieldId))
    .Distinct()
    .ToListAsync();
```

### Phase 3: Backend — Service

**Interface:** `TSIC.Contracts/Services/IFieldService.cs`
**Implementation:** `TSIC.API/Services/Scheduling/FieldService.cs`

```
Methods:
- GetFieldManagementDataAsync() → FieldManagementResponse
- CreateFieldAsync(CreateFieldRequest) → FieldDto
- UpdateFieldAsync(UpdateFieldRequest) → void
- DeleteFieldAsync(Guid fieldId) → bool
- AssignFieldsAsync(AssignFieldsRequest) → void
- RemoveFieldsAsync(RemoveFieldsRequest) → void
```

### Phase 4: Backend — Controller

**File:** `TSIC.API/Controllers/FieldController.cs`

```
[Authorize(Policy = "AdminOnly")]
[ApiController]
[Route("api/[controller]")]

GET  /api/field                    → GetFieldManagementDataAsync()
POST /api/field                    → CreateFieldAsync(CreateFieldRequest)
PUT  /api/field                    → UpdateFieldAsync(UpdateFieldRequest)
DELETE /api/field/{fieldId}        → DeleteFieldAsync(fieldId)
POST /api/field/assign             → AssignFieldsAsync(AssignFieldsRequest)
POST /api/field/remove             → RemoveFieldsAsync(RemoveFieldsRequest)
```

### Phase 5: Frontend — Generate API Models

```powershell
.\scripts\2-Regenerate-API-Models.ps1
```

### Phase 6: Frontend — Service

**File:** `src/app/views/admin/scheduling/fields/services/field.service.ts`

```typescript
@Injectable({ providedIn: 'root' })
export class FieldService {
    private readonly http = inject(HttpClient);
    private readonly apiUrl = `${environment.apiUrl}/field`;

    getFieldManagementData(): Observable<FieldManagementResponse> { ... }
    createField(request: CreateFieldRequest): Observable<FieldDto> { ... }
    updateField(request: UpdateFieldRequest): Observable<void> { ... }
    deleteField(fieldId: string): Observable<void> { ... }
    assignFields(request: AssignFieldsRequest): Observable<void> { ... }
    removeFields(request: RemoveFieldsRequest): Observable<void> { ... }
}
```

### Phase 7: Frontend — Component

**File:** `src/app/views/admin/scheduling/fields/manage-fields.component.ts`

Follow the established swapper component pattern exactly:

```typescript
// ── Panel state signals (mirrored pattern from Roster Swapper) ──

// Available panel
readonly availableFields = signal<FieldDto[]>([]);
readonly availableSelected = signal<Set<string>>(new Set());
readonly availableFilter = signal('');
readonly availableSortCol = signal<SortColumn>(null);
readonly availableSortDir = signal<SortDir>(null);
readonly sortedFilteredAvailable = computed(() => ...);

// Assigned panel
readonly assignedFields = signal<LeagueSeasonFieldDto[]>([]);
readonly assignedSelected = signal<Set<string>>(new Set());
readonly assignedFilter = signal('');
readonly assignedSortCol = signal<SortColumn>(null);
readonly assignedSortDir = signal<SortDir>(null);
readonly sortedFilteredAssigned = computed(() => ...);

// Transfer state
readonly swappingId = signal<string | null>(null);
readonly isBatchAssigning = signal(false);
readonly isBatchRemoving = signal(false);
readonly isLoading = signal(false);

// Detail editor state
readonly selectedField = signal<FieldDto | null>(null);
readonly isCreating = signal(false);
```

**HTML template** follows the panel-header / panel-body / panel-footer structure with:
- Frozen checkbox column + frozen arrow-button column
- Sortable `Field Name` and `City` headers
- Text filter `<input>` in panel-header
- Per-row `→` / `←` buttons with spinner on `swappingId`
- "Assign Selected → (N)" / "← Remove Selected (N)" in panel-footer
- Detail form below panels (conditionally shown)
- Empty state icons matching existing swappers

**SCSS** reuses the established patterns:
- `.field-panels { display: grid; grid-template-columns: 1fr 1fr; gap: var(--space-3); }`
- `.panel-header`, `.panel-body`, `.panel-footer` with CSS variable colors
- `.frozen` columns with sticky positioning and z-index stacking
- `@media (max-width: 767.98px) { grid-template-columns: 1fr; }`

### Phase 8: Frontend — Route

Add to `app.routes.ts`:
```typescript
{
  path: 'admin/scheduling/fields',
  loadComponent: () => import('./views/admin/scheduling/fields/manage-fields.component')
    .then(m => m.ManageFieldsComponent),
  canActivate: [authGuard],
  data: { roles: ['SuperUser', 'Director', 'SuperDirector'] }
}
```

### Phase 9: Testing

- Verify SuperUser sees all non-system fields in available panel
- Verify Director sees only fields historically used by their jobs (not by `LebUserId`)
- Verify system fields (starting with `*`) are excluded from both panels
- Verify single-row assign: click `→`, field moves from available to assigned with spinner
- Verify single-row remove: click `←`, field moves from assigned to available with spinner
- Verify batch assign: select multiple checkboxes, click "Assign Selected → (N)"
- Verify batch remove: select multiple, click "← Remove Selected (N)"
- Verify detail editor: click row in either panel, form populates below
- Verify create: click `[+ New]`, empty form appears, save creates field in available panel
- Verify delete blocked when field referenced in Schedule or Timeslots
- Verify audit trail (LebUserId, Modified) on all mutations
- Verify text filter works in both panels independently
- Verify column sorting (ascending → descending → none cycle)
- Test all 8 color palettes for styling compliance
- Test responsive layout: panels stack vertically below 768px
