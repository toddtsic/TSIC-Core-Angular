# 012 — Job DDL Options Editor

> **Status**: Design Spec — Ready for Review
> **Date**: 2026-02-18
> **Keyword**: `CWCC Implement JOB-DDL-OPTIONS`
> **Legacy reference**: `reference/TSIC-Unify-2024/TSIC-Unify/Controllers/Admin/JobJsonController.cs`
> **Legacy view**: `reference/TSIC-Unify-2024/TSIC-Unify/Views/JobJson/Index.cshtml`
> **Legacy service**: `reference/TSIC-Unify-2024/TSIC-Unify-Services/IJobJsonService.cs`

---

## 1. Problem Statement

The legacy `JobJson/Index` page lets SuperUsers configure the dropdown values that appear on
player and team registration forms — jersey sizes, positions, grad years, etc. These 20
dropdown categories are stored as a single serialized JSON blob in `Jobs.JsonOptions`.

The current implementation:

- **Violates repository pattern** — `JobJsonController` injects `SqlDbContext` directly (line 98)
- **Uses Newtonsoft.Json** — the new codebase standardizes on `System.Text.Json`
- **Poor UX** — multi-select lists with no obvious delete mechanism (user must deselect + submit)
- **No dirty detection** — form submits on every Add, no unsaved-changes warning
- **jQuery modal for adds** — uses Bootstrap 3 modal with `$('#newOptionModal').modal('show')`
- **No validation** — duplicate values, empty strings, and whitespace-only entries are silently saved
- **`Text` always equals `Value`** — the `JsonSelectListItem { Text, Value }` structure is redundant; every entry sets both fields to the same string

The migrated feature provides a clean Angular editor with chip-based UI, inline add/remove,
dirty detection, and repository-pattern-compliant backend.

---

## 2. Design Decisions

### 2.1 Flatten Text/Value to Simple String Lists

The legacy `JsonSelectListItem { Text, Value }` stores identical values in both fields.
Every edit operation in the legacy controller does `new JsonSelectListItem { Text = m, Value = m }`.
Registration form consumption uses `.Value` exclusively.

**Decision**: The API DTO uses `List<string>` per category. The service layer handles
round-trip mapping to/from `JsonSelectListItem` for storage compatibility — existing JSON
in the database remains readable without migration.

### 2.2 Single-Page Editor with Grouped Sections (No Tabs)

20 categories in 3 logical groups:

| Group | Categories | Count |
|---|---|---|
| **Clothing Sizes** | Jersey, Shorts, Reversible, Kilt, T-Shirt, Gloves, Sweatshirt, Shoes | 8 |
| **Player Data** | Years Experience, Positions, Grad Years, Recruiting Grad Years, School Grades, Strong Hand, Who Referred, Height Inches, Skill Levels | 9 |
| **Team & Context** | LOPs, Club Names, Prior Season Years | 3 |

**Rationale**: With a compact chip-based UI, all 20 categories fit on a single scrollable
page without feeling crowded. Tabs would add navigation complexity for no real benefit — the
user typically edits multiple categories in one session. Collapsible section headers allow
focusing on one group at a time.

### 2.3 Two Endpoints: GET + PUT

| Method | Route | Purpose |
|---|---|---|
| `GET` | `/api/job-ddl-options` | Return all 20 categories for current job |
| `PUT` | `/api/job-ddl-options` | Save all 20 categories back |

The entire `JsonOptions` blob is ~2-5 KB. A single GET/PUT pair is the simplest correct
approach — no need for per-category endpoints.

### 2.4 Chip-Based UI Instead of Multi-Select

Each category renders as:
- **Label** with friendly display name
- **Chip list** — each value as a removable chip (click X to remove)
- **Inline input** — text field + Add button (no modal needed)
- **Bulk add** — semicolon-delimited input preserved from legacy (`val1;val2;val3`)

This is materially better UX than the legacy multi-select approach.

### 2.5 Data-Driven Rendering

Instead of 20 copy-paste template blocks (like the legacy view), define a category metadata
array and render via `@for`:

```typescript
readonly categories: DdlCategory[] = [
  { key: 'jerseySizes', label: 'Jersey Sizes', group: 'clothing' },
  { key: 'shortsSizes', label: 'Shorts Sizes', group: 'clothing' },
  // ... 18 more
];
```

The template iterates this array. Adding a new category is a one-line config change.

### 2.6 Dedicated Repository (Not IJobRepository Extension)

Follows the established pattern: Widget Editor has `IWidgetEditorRepository`, Job Clone has
`IJobCloneRepository`. Each feature gets a focused repository. The DDL options feature
needs exactly 2 methods (read JSON string, write JSON string).

---

## 3. Database Schema

**No migrations required.** The feature reads/writes a single existing column:

| Table | Column | Type | Purpose |
|---|---|---|---|
| `Jobs.Jobs` | `JsonOptions` | `nvarchar(max)` | Serialized `JobJsonOptions` JSON |

The JSON structure inside `JsonOptions` is a dictionary of 20 category keys, each containing
a `List<JsonSelectListItem>` (where `JsonSelectListItem = { Text, Value }`).

---

## 4. Backend Architecture

### Architecture Flow

```
DdlOptionsController  →  IDdlOptionsService  →  IDdlOptionsRepository  →  SqlDbContext
     (API)                  (Application)           (Infrastructure)         (Data)
```

### Authorization

SuperUser-only: `[Authorize(Policy = "SuperUserOnly")]` on controller.

### Internal JSON Model (for serialization compatibility)

This class mirrors the legacy `JobJsonOptions` exactly, used only inside the service layer
for deserialization/serialization of the existing JSON format:

```csharp
// Internal to service — NOT exposed via API
internal class JobJsonOptions
{
    public List<JsonSelectListItem>? ListSizes_Jersey { get; set; }
    public List<JsonSelectListItem>? ListSizes_Shorts { get; set; }
    public List<JsonSelectListItem>? ListSizes_Reversible { get; set; }
    public List<JsonSelectListItem>? ListSizes_Kilt { get; set; }
    public List<JsonSelectListItem>? ListSizes_Tshirt { get; set; }
    public List<JsonSelectListItem>? ListSizes_Gloves { get; set; }
    public List<JsonSelectListItem>? ListSizes_Sweatshirt { get; set; }
    public List<JsonSelectListItem>? ListSizes_Shoes { get; set; }
    public List<JsonSelectListItem>? List_YearsExperience { get; set; }
    public List<JsonSelectListItem>? List_Positions { get; set; }
    public List<JsonSelectListItem>? List_GradYears { get; set; }
    public List<JsonSelectListItem>? List_RecruitingGradYears { get; set; }
    public List<JsonSelectListItem>? List_SchoolGrades { get; set; }
    public List<JsonSelectListItem>? List_StrongHand { get; set; }
    public List<JsonSelectListItem>? List_WhoReferred { get; set; }
    public List<JsonSelectListItem>? List_HeightInches { get; set; }
    public List<JsonSelectListItem>? List_ClubNames { get; set; }
    public List<JsonSelectListItem>? List_Lops { get; set; }
    public List<JsonSelectListItem>? List_PriorSeasonYears { get; set; }
    public List<JsonSelectListItem>? List_SkillLevels { get; set; }
}

internal class JsonSelectListItem
{
    public string? Text { get; set; }
    public string? Value { get; set; }
}
```

### API DTO

File: `TSIC.Contracts/Dtos/Admin/JobDdlOptionsDtos.cs`

```csharp
/// <summary>
/// Flat DTO of all 20 dropdown categories. Each category is a simple string list.
/// </summary>
public record JobDdlOptionsDto
{
    // Clothing sizes
    public required List<string> JerseySizes { get; init; }
    public required List<string> ShortsSizes { get; init; }
    public required List<string> ReversibleSizes { get; init; }
    public required List<string> KiltSizes { get; init; }
    public required List<string> TShirtSizes { get; init; }
    public required List<string> GlovesSizes { get; init; }
    public required List<string> SweatshirtSizes { get; init; }
    public required List<string> ShoesSizes { get; init; }

    // Player data
    public required List<string> YearsExperience { get; init; }
    public required List<string> Positions { get; init; }
    public required List<string> GradYears { get; init; }
    public required List<string> RecruitingGradYears { get; init; }
    public required List<string> SchoolGrades { get; init; }
    public required List<string> StrongHand { get; init; }
    public required List<string> WhoReferred { get; init; }
    public required List<string> HeightInches { get; init; }
    public required List<string> SkillLevels { get; init; }

    // Team & context
    public required List<string> Lops { get; init; }
    public required List<string> ClubNames { get; init; }
    public required List<string> PriorSeasonYears { get; init; }
}
```

### Repository Interface

File: `TSIC.Contracts/Repositories/IDdlOptionsRepository.cs`

```csharp
public interface IDdlOptionsRepository
{
    /// <summary>
    /// Read the raw JsonOptions string for a job (AsNoTracking).
    /// </summary>
    Task<string?> GetJsonOptionsAsync(Guid jobId, CancellationToken ct = default);

    /// <summary>
    /// Update the JsonOptions column for a job.
    /// </summary>
    Task UpdateJsonOptionsAsync(Guid jobId, string jsonOptions, CancellationToken ct = default);
}
```

### Repository Implementation

File: `TSIC.Infrastructure/Repositories/DdlOptionsRepository.cs`

```csharp
public class DdlOptionsRepository : IDdlOptionsRepository
{
    private readonly SqlDbContext _context;

    public DdlOptionsRepository(SqlDbContext context) => _context = context;

    public async Task<string?> GetJsonOptionsAsync(Guid jobId, CancellationToken ct = default)
    {
        return await _context.Jobs
            .AsNoTracking()
            .Where(j => j.JobId == jobId)
            .Select(j => j.JsonOptions)
            .SingleOrDefaultAsync(ct);
    }

    public async Task UpdateJsonOptionsAsync(Guid jobId, string jsonOptions, CancellationToken ct = default)
    {
        await _context.Jobs
            .Where(j => j.JobId == jobId)
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.JsonOptions, jsonOptions), ct);
    }
}
```

**Note**: `ExecuteUpdateAsync` is a single-statement UPDATE — no need to load the entire
200+ column Jobs entity just to change one field.

### Service Interface

File: `TSIC.Contracts/Services/IDdlOptionsService.cs`

```csharp
public interface IDdlOptionsService
{
    Task<JobDdlOptionsDto> GetOptionsAsync(Guid jobId, CancellationToken ct = default);
    Task SaveOptionsAsync(Guid jobId, JobDdlOptionsDto dto, CancellationToken ct = default);
}
```

### Service Implementation

File: `TSIC.API/Services/Admin/DdlOptionsService.cs`

Key responsibilities:
1. **GET**: Read raw JSON string → deserialize to `JobJsonOptions` → map to `JobDdlOptionsDto`
   (extracting `.Value` from each `JsonSelectListItem`)
2. **PUT**: Map `JobDdlOptionsDto` → `JobJsonOptions` (creating `{ Text = v, Value = v }` pairs)
   → serialize to JSON → write back via repository
3. **Validation on save**: trim whitespace, remove empty/duplicate entries per category
4. **Null handling**: if `JsonOptions` is null (new job, never configured), return DTO with
   all empty lists

```csharp
public class DdlOptionsService : IDdlOptionsService
{
    private readonly IDdlOptionsRepository _repository;

    public DdlOptionsService(IDdlOptionsRepository repository)
        => _repository = repository;

    public async Task<JobDdlOptionsDto> GetOptionsAsync(Guid jobId, CancellationToken ct = default)
    {
        var json = await _repository.GetJsonOptionsAsync(jobId, ct);
        if (string.IsNullOrWhiteSpace(json))
            return EmptyDto();

        var options = JsonSerializer.Deserialize<JobJsonOptions>(json, _serializerOptions)
            ?? new JobJsonOptions();

        return MapToDto(options);
    }

    public async Task SaveOptionsAsync(Guid jobId, JobDdlOptionsDto dto, CancellationToken ct = default)
    {
        var sanitized = SanitizeDto(dto);        // trim, dedup, remove blanks
        var options = MapFromDto(sanitized);
        var json = JsonSerializer.Serialize(options, _serializerOptions);
        await _repository.UpdateJsonOptionsAsync(jobId, json, ct);
    }
}
```

### Controller

File: `TSIC.API/Controllers/DdlOptionsController.cs`

```csharp
[ApiController]
[Route("api/job-ddl-options")]
[Authorize(Policy = "SuperUserOnly")]
public class DdlOptionsController : ControllerBase
{
    private readonly IDdlOptionsService _service;
    private readonly IJobRepository _jobRepository;

    [HttpGet]
    public async Task<ActionResult<JobDdlOptionsDto>> Get(CancellationToken ct)
    {
        var jobId = User.GetJobId();      // from JWT claims
        var dto = await _service.GetOptionsAsync(jobId, ct);
        return Ok(dto);
    }

    [HttpPut]
    public async Task<IActionResult> Save(JobDdlOptionsDto dto, CancellationToken ct)
    {
        var jobId = User.GetJobId();
        await _service.SaveOptionsAsync(jobId, dto, ct);
        return NoContent();
    }
}
```

---

## 5. Frontend Architecture

### File Structure

```
src/app/views/admin/ddl-options/
├── ddl-options.component.ts       (~180 lines)
├── ddl-options.component.html     (~80 lines — data-driven template)
└── ddl-options.component.scss     (~60 lines)
```

### Category Metadata (data-driven)

```typescript
interface DdlCategory {
  key: keyof JobDdlOptionsDto;
  label: string;
  group: 'clothing' | 'player' | 'team';
}

const CATEGORIES: DdlCategory[] = [
  // Clothing Sizes
  { key: 'jerseySizes',      label: 'Jersey Sizes',      group: 'clothing' },
  { key: 'shortsSizes',      label: 'Shorts Sizes',      group: 'clothing' },
  { key: 'reversibleSizes',  label: 'Reversible Sizes',  group: 'clothing' },
  { key: 'kiltSizes',        label: 'Kilt Sizes',        group: 'clothing' },
  { key: 'tShirtSizes',      label: 'T-Shirt Sizes',     group: 'clothing' },
  { key: 'glovesSizes',      label: 'Gloves Sizes',      group: 'clothing' },
  { key: 'sweatshirtSizes',  label: 'Sweatshirt Sizes',  group: 'clothing' },
  { key: 'shoesSizes',       label: 'Shoes Sizes',       group: 'clothing' },

  // Player Data
  { key: 'yearsExperience',      label: 'Years Experience',      group: 'player' },
  { key: 'positions',            label: 'Positions',              group: 'player' },
  { key: 'gradYears',            label: 'Grad Years',             group: 'player' },
  { key: 'recruitingGradYears',  label: 'Recruiting Grad Years',  group: 'player' },
  { key: 'schoolGrades',         label: 'School Grades',          group: 'player' },
  { key: 'strongHand',           label: 'Strong Hand',            group: 'player' },
  { key: 'whoReferred',          label: 'Who Referred',           group: 'player' },
  { key: 'heightInches',         label: 'Height (Inches)',        group: 'player' },
  { key: 'skillLevels',          label: 'Skill Levels',           group: 'player' },

  // Team & Context
  { key: 'lops',              label: 'LOPs (Team Reg Form)',   group: 'team' },
  { key: 'clubNames',         label: 'Club Names',              group: 'team' },
  { key: 'priorSeasonYears',  label: 'Prior Season Years',      group: 'team' },
];

const GROUP_LABELS: Record<string, string> = {
  clothing: 'Clothing Sizes',
  player:   'Player Data',
  team:     'Team & Context',
};
```

### State Management (Signals)

```typescript
// Data
readonly options = signal<JobDdlOptionsDto | null>(null);
readonly originalOptions = signal<JobDdlOptionsDto | null>(null);

// UI state
readonly isLoading = signal(false);
readonly isSaving = signal(false);
readonly errorMessage = signal<string | null>(null);

// Per-category add input (keyed by category key)
readonly addInputs = signal<Record<string, string>>({});

// Dirty detection
readonly isDirty = computed(() => {
  const current = this.options();
  const original = this.originalOptions();
  if (!current || !original) return false;
  return JSON.stringify(current) !== JSON.stringify(original);
});

// Change summary for save bar
readonly changeCount = computed(() => {
  // Count categories that differ between current and original
});
```

### Template Structure (data-driven)

```html
<!-- Sticky save bar (appears when dirty) -->
@if (isDirty()) {
  <div class="save-bar">
    <span>{{ changeCount() }} category(ies) modified</span>
    <button (click)="reset()" [disabled]="isSaving()">Reset</button>
    <button (click)="save()" [disabled]="isSaving()">Save Changes</button>
  </div>
}

<!-- Category groups -->
@for (group of groups; track group.key) {
  <section class="category-group">
    <h3>{{ group.label }}</h3>

    @for (cat of group.categories; track cat.key) {
      <div class="category-row">
        <label>{{ cat.label }}</label>
        <div class="chip-list">
          @for (val of getValues(cat.key); track val; let i = $index) {
            <span class="chip">
              {{ val }}
              <button (click)="removeValue(cat.key, i)" aria-label="Remove {{ val }}">×</button>
            </span>
          }
        </div>
        <div class="add-row">
          <input [value]="getAddInput(cat.key)"
                 (input)="setAddInput(cat.key, $event)"
                 (keydown.enter)="addValues(cat.key)"
                 placeholder="Add value (semicolon for bulk)" />
          <button (click)="addValues(cat.key)">Add</button>
        </div>
      </div>
    }
  </section>
}
```

### Styling Approach

- CSS variables only (no hardcoded colors)
- Chips use `--bs-primary` background with `--brand-text` text
- Category rows use `var(--space-*)` grid spacing
- Save bar: sticky bottom, glassmorphic elevated surface
- Responsive: 2-column label+chips at 1280px+, stacked on mobile

### Routing

Add to admin children in `app.routes.ts`:

```typescript
{ path: 'ddl-options', loadComponent: () => import('./views/admin/ddl-options/ddl-options.component').then(m => m.DdlOptionsComponent) }
```

Update `BreadcrumbService`:
- `ROUTE_TITLE_MAP`: `'ddl-options' → 'DDL Options'`
- `ROUTE_WORKSPACE_MAP`: `'ddl-options' → 'settings'`

---

## 6. Validation Rules (Service Layer)

On save, the service sanitizes all 20 categories:

| Rule | Behavior |
|---|---|
| Trim whitespace | `"  XL  "` → `"XL"` |
| Remove blanks | Empty strings and whitespace-only entries dropped |
| Remove duplicates | Case-insensitive dedup within each category |
| Preserve order | Values saved in the order received from frontend |

No max-length enforcement per category — SuperUsers manage their own lists.

---

## 7. Implementation Phases

### Phase 1: DTOs (Contracts layer)
| Action | File |
|---|---|
| Create | `TSIC.Contracts/Dtos/Admin/JobDdlOptionsDtos.cs` |

### Phase 2: Repository Interface + Implementation
| Action | File |
|---|---|
| Create | `TSIC.Contracts/Repositories/IDdlOptionsRepository.cs` |
| Create | `TSIC.Infrastructure/Repositories/DdlOptionsRepository.cs` |

### Phase 3: Service Interface + Implementation
| Action | File |
|---|---|
| Create | `TSIC.Contracts/Services/IDdlOptionsService.cs` |
| Create | `TSIC.API/Services/Admin/DdlOptionsService.cs` |

Key: service owns `JobJsonOptions` + `JsonSelectListItem` as `internal` classes for
serialization compatibility. Mapping logic between `JobDdlOptionsDto ↔ JobJsonOptions`.

### Phase 4: Controller + DI Registration
| Action | File |
|---|---|
| Create | `TSIC.API/Controllers/DdlOptionsController.cs` |
| Modify | `TSIC.API/Program.cs` — add `AddScoped<IDdlOptionsRepository, DdlOptionsRepository>()` and `AddScoped<IDdlOptionsService, DdlOptionsService>()` |

### Phase 5: Frontend Component + Routing
| Action | File |
|---|---|
| Run | `.\scripts\2-Regenerate-API-Models.ps1` (after backend compiles) |
| Create | `src/app/views/admin/ddl-options/ddl-options.component.ts` |
| Create | `src/app/views/admin/ddl-options/ddl-options.component.html` |
| Create | `src/app/views/admin/ddl-options/ddl-options.component.scss` |
| Modify | `src/app/app.routes.ts` — add `ddl-options` child route under admin |
| Modify | `src/app/infrastructure/services/breadcrumb.service.ts` — add title + workspace map entries |

### Phase 6: Build + Verify
- `dotnet build` backend
- Regenerate API models
- `ng build` frontend
- Manual Swagger test: GET/PUT `/api/job-ddl-options`
- Verify round-trip: existing JSON → GET → PUT unchanged → JSON identical
- Verify add/remove/bulk-add in UI
- Verify dirty detection and save bar behavior
- Verify sanitization (trim, dedup, blank removal)

---

## 8. Error Handling

| Condition | Exception | HTTP Status |
|---|---|---|
| Job not found (bad JWT) | `KeyNotFoundException` | 404 |
| Null/invalid DTO body | Model validation | 400 |
| Serialization failure | `JsonException` | 500 |

---

## 9. Files Summary

### New files (8)
1. `TSIC.Contracts/Dtos/Admin/JobDdlOptionsDtos.cs`
2. `TSIC.Contracts/Repositories/IDdlOptionsRepository.cs`
3. `TSIC.Contracts/Services/IDdlOptionsService.cs`
4. `TSIC.Infrastructure/Repositories/DdlOptionsRepository.cs`
5. `TSIC.API/Services/Admin/DdlOptionsService.cs`
6. `TSIC.API/Controllers/DdlOptionsController.cs`
7. `src/app/views/admin/ddl-options/ddl-options.component.ts`
8. `src/app/views/admin/ddl-options/ddl-options.component.html`
9. `src/app/views/admin/ddl-options/ddl-options.component.scss`

### Modified files (3)
1. `TSIC.API/Program.cs` — DI registration (2 lines)
2. `src/app/app.routes.ts` — admin child route
3. `src/app/infrastructure/services/breadcrumb.service.ts` — title + workspace map

---

## 10. Recommendations Over Legacy

| Area | Legacy | New |
|---|---|---|
| **Add UX** | jQuery modal, auto-submits form | Inline input, explicit Save |
| **Remove UX** | Deselect from multi-select + Submit | Click × on chip |
| **Bulk add** | Semicolon-delimited in modal | Semicolon-delimited in inline input |
| **Dirty detection** | None | Computed signal, sticky save bar |
| **Validation** | None | Trim, dedup, blank removal |
| **Template** | 20 copy-paste HTML blocks | Data-driven `@for` loop |
| **Data access** | Direct `SqlDbContext` | Repository pattern |
| **Serialization** | `Newtonsoft.Json` | `System.Text.Json` |
