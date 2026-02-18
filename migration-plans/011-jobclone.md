# 011 — Job Clone

> **Status**: Design Spec — Ready for Review
> **Date**: 2026-02-18
> **Keyword**: `CWCC Implement JOB-CLONE`
> **Legacy reference**: `utility.CloneJobII` stored procedure
> **Legacy controller**: `reference/TSIC-Unify-2024/TSIC-Unify/Controllers/Admin/JobController.cs` (lines 958–1085)

---

## 1. Problem Statement

Job cloning (creating a new season from an existing tournament/event) is currently handled by a 300-line stored procedure (`utility.CloneJobII`) that:

- Uses dynamic SQL string concatenation (SQL injection risk, hard to debug)
- Has no visibility into individual clone steps (all-or-nothing black box)
- Cannot be unit tested
- Lacks structured error reporting
- Requires direct DB access to invoke

Moving this to the C# backend gives us: repository-pattern compliance, step-by-step logging, structured error handling, and a proper API endpoint with a frontend form for the SuperUser.

---

## 2. Scope

### In Scope
- Clone **11 table groups** in dependency order (see Section 4)
- Clone LAD hierarchy (League → Agegroups → Divisions) — **NOT Teams**
- Grad year auto-increment in agegroup names and structured fields
- All legacy flags: `UpAgegroupNamesByOne`, `SetDirectorsToInactive`, `NoParallaxSlide1`
- Frontend form for SuperUser to configure all clone parameters
- Explicit database transaction for atomicity across 11 tables

### Out of Scope (future work)
- Team cloning (was `@bDoNotCloneTeams` = false in SP)
- Widget/dashboard cloning (JobWidget records)
- Schedule/calendar cloning
- Store/merchandise cloning

---

## 3. Architecture

```
JobCloneController  →  IJobCloneService  →  IJobCloneRepository  →  SqlDbContext
     (API)              (Application)         (Infrastructure)        (Data)
```

All layers follow existing patterns established by the WidgetEditor feature.

---

## 4. Clone Dependency Graph (Execution Order)

```
Step 1:  Jobs.Jobs              (root — new GUID)
Step 2:  Jobs.JobDisplayOptions  (1:1, FK → Jobs)
Step 3:  Jobs.JobOwlImages       (1:1, FK → Jobs)
Step 4:  Jobs.Bulletins          (1:N, FK → Jobs)
Step 5:  Jobs.JobAgeRanges       (1:N, FK → Jobs)
Step 6:  Jobs.JobMenus           (1:N, FK → Jobs)
Step 7:  Jobs.JobMenuItems       (1:N, FK → JobMenus — requires Step 6 IDs)
Step 8:  Jobs.Registrations      (admin roles only, FK → Jobs)
Step 9:  Leagues.Leagues         (new GUID — independent root)
Step 10: Jobs.JobLeagues         (FK → Jobs + Leagues — requires Steps 1 & 9)
Step 11: Leagues.Agegroups       (FK → Leagues — requires Step 9)
Step 12: Leagues.Divisions       (FK → Agegroups — requires Step 11 IDs)
```

All steps queue mutations in the DbContext change tracker. A single `SaveChangesAsync` inside an explicit transaction commits everything atomically.

---

## 5. Request / Response DTOs

### File: `TSIC.Contracts/Dtos/Admin/JobCloneDtos.cs`

```csharp
public record JobCloneRequest
{
    public required Guid SourceJobId { get; init; }

    // Target identity
    public required string JobPathTarget { get; init; }
    public required string JobNameTarget { get; init; }
    public required string YearTarget { get; init; }
    public required string SeasonTarget { get; init; }
    public required string DisplayName { get; init; }
    public required string LeagueNameTarget { get; init; }

    // Target dates
    public required DateTime ExpiryAdmin { get; init; }
    public required DateTime ExpiryUsers { get; init; }

    // Email
    public string? RegFormFrom { get; init; }

    // Flags
    public bool UpAgegroupNamesByOne { get; init; }
    public bool SetDirectorsToInactive { get; init; }
    public bool NoParallaxSlide1 { get; init; }
}

public record JobCloneResponse
{
    public required Guid NewJobId { get; init; }
    public required string NewJobPath { get; init; }
    public required string NewJobName { get; init; }
    public required CloneSummary Summary { get; init; }
}

public record CloneSummary
{
    public int BulletinsCloned { get; init; }
    public int AgeRangesCloned { get; init; }
    public int MenusCloned { get; init; }
    public int MenuItemsCloned { get; init; }
    public int AdminRegistrationsCloned { get; init; }
    public int LeaguesCloned { get; init; }
    public int AgegroupsCloned { get; init; }
    public int DivisionsCloned { get; init; }
}
```

### File: `TSIC.Contracts/Dtos/Admin/JobCloneSourceDto.cs`

For the frontend "source job" picker — lightweight DTO listing jobs the SuperUser can clone from.

```csharp
public record JobCloneSourceDto
{
    public required Guid JobId { get; init; }
    public required string JobPath { get; init; }
    public required string JobName { get; init; }
    public string? Year { get; init; }
    public string? Season { get; init; }
    public string? DisplayName { get; init; }
    public Guid CustomerId { get; init; }
}
```

---

## 6. Repository Interface

### File: `TSIC.Contracts/Repositories/IJobCloneRepository.cs`

```csharp
public interface IJobCloneRepository
{
    // Source data loading (read-only)
    Task<Jobs?> GetSourceJobAsync(Guid jobId, CancellationToken ct = default);
    Task<JobDisplayOptions?> GetSourceDisplayOptionsAsync(Guid jobId, CancellationToken ct = default);
    Task<JobOwlImages?> GetSourceOwlImagesAsync(Guid jobId, CancellationToken ct = default);
    Task<List<Bulletins>> GetSourceBulletinsAsync(Guid jobId, CancellationToken ct = default);
    Task<List<JobAgeRanges>> GetSourceAgeRangesAsync(Guid jobId, CancellationToken ct = default);
    Task<List<JobMenus>> GetSourceMenusWithItemsAsync(Guid jobId, CancellationToken ct = default);
    Task<List<Registrations>> GetSourceAdminRegistrationsAsync(Guid jobId, CancellationToken ct = default);
    Task<Leagues?> GetSourceLeagueAsync(Guid jobId, CancellationToken ct = default);
    Task<List<Agegroups>> GetSourceAgegroupsAsync(Guid leagueId, string? season, CancellationToken ct = default);
    Task<List<Divisions>> GetSourceDivisionsAsync(List<Guid> agegroupIds, CancellationToken ct = default);

    // Validation
    Task<bool> JobPathExistsAsync(string jobPath, CancellationToken ct = default);

    // Listing (for source picker)
    Task<List<JobCloneSourceDto>> GetCloneableJobsAsync(CancellationToken ct = default);

    // Write operations (queue in change tracker)
    void AddJob(Jobs job);
    void AddDisplayOptions(JobDisplayOptions options);
    void AddOwlImages(JobOwlImages images);
    void AddBulletins(IEnumerable<Bulletins> bulletins);
    void AddAgeRanges(IEnumerable<JobAgeRanges> ranges);
    void AddMenu(JobMenus menu);
    void AddMenuItems(IEnumerable<JobMenuItems> items);
    void AddRegistrations(IEnumerable<Registrations> registrations);
    void AddLeague(Leagues league);
    void AddJobLeague(JobLeagues jobLeague);
    void AddAgegroups(IEnumerable<Agegroups> agegroups);
    void AddDivisions(IEnumerable<Divisions> divisions);

    // Transaction + commit
    Task<IDisposable> BeginTransactionAsync(CancellationToken ct = default);
    Task CommitTransactionAsync(CancellationToken ct = default);
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
```

**Note**: Source data loading uses `AsNoTracking()` so cloned entities are detached and can be mutated freely without EF tracking conflicts.

---

## 7. Service Layer — Clone Orchestration

### File: `TSIC.API/Services/Admin/JobCloneService.cs`

Key logic in `CloneJobAsync(JobCloneRequest request, string superUserId, CancellationToken ct)`:

1. **Validate** — check source job exists, target jobPath is unique
2. **Begin transaction** — explicit for 12-step atomicity
3. **Load all source data** — sequential awaits (AsNoTracking)
4. **Clone each entity group** — private helper methods that:
   - Create new entities with `Guid.NewGuid()` for PKs
   - Copy all properties from source
   - Override target-specific fields (JobId, JobPath, etc.)
   - Set `Modified = DateTime.UtcNow`, `LebUserId = superUserId`
   - Return ID mappings where needed (old MenuId → new MenuId, old AgegroupId → new AgegroupId)
5. **Queue all adds** — via repository methods
6. **SaveChangesAsync** — single flush
7. **Commit transaction**
8. **Return JobCloneResponse** with summary counts

### Grad Year Logic (private helper)

```csharp
private static string IncrementYearsInName(string name)
{
    // Regex: find 4-digit year patterns (2020-2039 range)
    return Regex.Replace(name, @"\b(20[2-3]\d)\b", m =>
        (int.Parse(m.Value) + 1).ToString());
}
```

Applied to `AgegroupName` when `UpAgegroupNamesByOne` is true.
Also increments `GradYearMin` and `GradYearMax` (int? fields) by +1.

### Menu Cloning (hierarchical)

```
1. For each source JobMenu:
   - Create new MenuId GUID
   - Clone all properties, set new JobId

2. For each source JobMenuItem where ParentMenuItemId IS NULL (top-level):
   - Create new MenuItemId GUID
   - Set new MenuId
   - Store mapping: oldMenuItemId → newMenuItemId

3. For each source JobMenuItem where ParentMenuItemId IS NOT NULL (children):
   - Create new MenuItemId GUID
   - Set new MenuId
   - Remap ParentMenuItemId using the mapping from step 2
```

### Bulletin Date Shifting

Shift `CreateDate` forward by the same offset as the clone date:
```csharp
var earliestBulletin = sourceBulletins.Min(b => b.CreateDate);
var dateOffset = DateTime.UtcNow - earliestBulletin;
// Each cloned bulletin: newCreateDate = source.CreateDate + dateOffset
```

---

## 8. Controller

### File: `TSIC.API/Controllers/JobCloneController.cs`

```csharp
[ApiController]
[Route("api/job-clone")]
[Authorize(Policy = "SuperUserOnly")]
public class JobCloneController : ControllerBase
{
    [HttpGet("sources")]
    // Returns List<JobCloneSourceDto> — all jobs available to clone from

    [HttpPost]
    // Accepts JobCloneRequest, returns JobCloneResponse
    // Extracts superUserId from JWT claims
    // Catches: ArgumentException → 400, KeyNotFoundException → 404,
    //          InvalidOperationException → 409 (duplicate jobPath)
}
```

---

## 9. Frontend Component

### Route: `/:jobPath/admin/job-clone`

### Files to create:
- `src/app/views/admin/job-clone/job-clone.component.ts` — Standalone component
- `src/app/views/admin/job-clone/job-clone.component.html` — Template
- `src/app/views/admin/job-clone/job-clone.component.scss` — Styles

### Design:
- **Source section**: Dropdown of cloneable jobs (pre-selects current job if navigated from admin)
- **Target section**: Form fields for jobPath, jobName, year, season, displayName, leagueName
- **Dates section**: ExpiryAdmin, ExpiryUsers date pickers
- **Email section**: RegFormFrom input
- **Options section**: Checkboxes for all 3 flags
- **Action**: Clone button → POST → display result with summary counts and link to new job

### State management:
- `sourceJobs = signal<JobCloneSourceDto[]>([])` — loaded on init
- `selectedSource = signal<JobCloneSourceDto | null>(null)` — chosen source job
- `cloning = signal(false)` — loading state
- `result = signal<JobCloneResponse | null>(null)` — clone result
- `error = signal<string | null>(null)` — error message
- Reactive form (`FormGroup`) for target fields

### Smart defaults:
When a source job is selected, auto-populate target fields:
- `jobPathTarget` = source jobPath with year incremented (e.g., `lftc-summer-2025` → `lftc-summer-2026`)
- `yearTarget` = source year + 1
- `jobNameTarget` = source jobName with year incremented
- `displayName` = source displayName
- `expiryAdmin` = source expiryAdmin + 1 year
- `expiryUsers` = source expiryUsers + 1 year

---

## 10. Implementation Phases

### Phase 1: DTOs (Contracts layer)
| Action | File |
|---|---|
| Create | `TSIC.Contracts/Dtos/Admin/JobCloneDtos.cs` |
| Create | `TSIC.Contracts/Dtos/Admin/JobCloneSourceDto.cs` |

### Phase 2: Repository Interface (Contracts layer)
| Action | File |
|---|---|
| Create | `TSIC.Contracts/Repositories/IJobCloneRepository.cs` |

### Phase 3: Repository Implementation (Infrastructure layer)
| Action | File |
|---|---|
| Create | `TSIC.Infrastructure/Repositories/JobCloneRepository.cs` |

Key patterns:
- Constructor: `private readonly SqlDbContext _context;`
- Reads: `_context.Jobs.AsNoTracking().Where(...).ToListAsync(ct)`
- Writes: `_context.Jobs.Add(entity)` / `_context.Bulletins.AddRange(entities)`
- Transaction: `_context.Database.BeginTransactionAsync(ct)` / `_context.Database.CommitTransactionAsync(ct)`
- Menu loading: `.Include(m => m.JobMenuItems)` for hierarchical eager load

### Phase 4: Service Interface + Implementation
| Action | File |
|---|---|
| Create | `TSIC.Contracts/Services/IJobCloneService.cs` |
| Create | `TSIC.API/Services/Admin/JobCloneService.cs` |

Service method signature:
```csharp
Task<JobCloneResponse> CloneJobAsync(JobCloneRequest request, string superUserId, CancellationToken ct = default);
Task<List<JobCloneSourceDto>> GetCloneableJobsAsync(CancellationToken ct = default);
```

### Phase 5: Controller
| Action | File |
|---|---|
| Create | `TSIC.API/Controllers/JobCloneController.cs` |

### Phase 6: DI Registration + Routing
| Action | File |
|---|---|
| Modify | `TSIC.API/Program.cs` — add `AddScoped<IJobCloneRepository, JobCloneRepository>()` and `AddScoped<IJobCloneService, JobCloneService>()` |
| Modify | `src/frontend/.../app.routes.ts` — add `job-clone` child route under admin |
| Modify | Breadcrumb `ROUTE_TITLE_MAP` / `ROUTE_WORKSPACE_MAP` if needed |

### Phase 7: Frontend Component
| Action | File |
|---|---|
| Create | `src/app/views/admin/job-clone/job-clone.component.ts` |
| Create | `src/app/views/admin/job-clone/job-clone.component.html` |
| Create | `src/app/views/admin/job-clone/job-clone.component.scss` |
| Run | `.\scripts\2-Regenerate-API-Models.ps1` (after backend compiles) |

### Phase 8: Build + Verify
- `dotnet build` backend
- Regenerate API models
- `ng build` frontend
- Manual Swagger test: POST `/api/job-clone` with sample payload
- Verify all 12 entity groups cloned correctly in DB
- Verify grad year increment logic on agegroup names
- Verify menu hierarchy preserved (parent→child relationships)
- Verify transaction rollback on failure (e.g., duplicate jobPath)

---

## 11. Key Implementation Details

### Auto-increment columns (DO NOT set manually)
- `Jobs.JobAi` — `ValueGeneratedOnAdd()`, let DB assign
- `Registrations.RegistrationAi` — `ValueGeneratedOnAdd()`, let DB assign
- `JobAgeRanges.AgeRangeId` — `ValueGeneratedOnAdd()`, let DB assign

### GUID generation
All other PKs use `Guid.NewGuid()` in the service layer (not `newsequentialid()` — that's only for DB-generated defaults).

### Properties NOT to clone (set fresh)
- `Modified` → `DateTime.UtcNow`
- `LebUserId` → superUserId from JWT
- `UpdatedOn` (rowversion) → `null` (DB generates)
- All auto-increment fields → omit (let DB assign)

### Admin role GUIDs (from RoleConstants)
```csharp
private static readonly HashSet<string> AdminRoleIds = new(StringComparer.OrdinalIgnoreCase)
{
    RoleConstants.Superuser,       // CD9DC8D7-...
    RoleConstants.SuperDirector,   // 7B9EB503-...
    RoleConstants.Director,        // FF4D1C27-...
};
```

### Season field
- `Season` lives on `Agegroups` (not on `Leagues`)
- When cloning agegroups, set `Season = request.SeasonTarget`
- The `Jobs.Season` field is updated on the cloned Job record

---

## 12. Error Handling

| Condition | Exception | HTTP Status |
|---|---|---|
| Source job not found | `KeyNotFoundException` | 404 |
| Target jobPath already exists | `InvalidOperationException` | 409 Conflict |
| Missing required fields | `ArgumentException` | 400 |
| DB constraint violation | Transaction rollback + rethrow | 500 |

---

## 13. Files Summary

### New files (10)
1. `TSIC.Contracts/Dtos/Admin/JobCloneDtos.cs`
2. `TSIC.Contracts/Dtos/Admin/JobCloneSourceDto.cs`
3. `TSIC.Contracts/Repositories/IJobCloneRepository.cs`
4. `TSIC.Contracts/Services/IJobCloneService.cs`
5. `TSIC.Infrastructure/Repositories/JobCloneRepository.cs`
6. `TSIC.API/Services/Admin/JobCloneService.cs`
7. `TSIC.API/Controllers/JobCloneController.cs`
8. `src/app/views/admin/job-clone/job-clone.component.ts`
9. `src/app/views/admin/job-clone/job-clone.component.html`
10. `src/app/views/admin/job-clone/job-clone.component.scss`

### Modified files (2)
1. `TSIC.API/Program.cs` — DI registration (2 lines)
2. `src/app/.../app.routes.ts` — admin child route
