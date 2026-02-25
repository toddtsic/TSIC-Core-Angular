# 022 — Referee Assignment Module Migration

**Status**: PLANNED
**Date**: 2026-02-25
**Legacy Endpoint**: `RefAssign/Index`, `RefAssignorAdd/Index`, `RefereeCalendar/Index`
**Priority**: High — fully functional legacy feature, maintain all behavior

---

## Overview

Migrate the legacy Razor/Syncfusion referee assignment system to the Angular 21 architecture. Three legacy pages become one unified Angular module with tab-based navigation. The legacy functionality is outstanding — preserve every feature with highest-polish UI.

### Legacy Pages → New Routes

| Legacy | New Route | Purpose |
|--------|-----------|---------|
| `RefAssign/Index` | `/:jobPath/scheduling/referee-assignment` | Assign refs to games |
| `RefereeCalendar/Index` | `/:jobPath/scheduling/referee-calendar` | Calendar/agenda view of assignments |
| `RefAssignorAdd/Index` | `/:jobPath/configure/ref-assignors` | Manage ref assignor role accounts |

---

## Phase 1 — Backend: Repository + DTOs + Service

### 1A. DTOs (`TSIC.Contracts/Dtos/Referees/RefereeDtos.cs`)

```csharp
// ── Read DTOs ──

public record RefereeSummaryDto
{
    public required Guid RegistrationId { get; init; }
    public required string FirstName { get; init; }
    public required string LastName { get; init; }
    public required string? Email { get; init; }
    public required string? Cellphone { get; init; }
    public required string? CertificationNumber { get; init; }    // SportAssnId
    public required DateTime? CertificationExpiry { get; init; }  // SportAssnIdexpDate
    public required bool IsActive { get; init; }
}

public record GameRefAssignmentDto
{
    public required int Gid { get; init; }
    public required Guid? RefRegistrationId { get; init; }
}

public record RefScheduleGameDto
{
    public required int Gid { get; init; }
    public required DateTime GameDate { get; init; }
    public required string? FieldName { get; init; }
    public required Guid? FieldId { get; init; }
    public required string? AgegroupName { get; init; }
    public required string? AgegroupColor { get; init; }
    public required string? DivName { get; init; }
    public required string? T1Name { get; init; }
    public required string? T2Name { get; init; }
    public required string? GameType { get; init; }
    public required List<Guid> AssignedRefIds { get; init; }
}

public record RefGameDetailsDto
{
    public required string RefName { get; init; }
    public required Guid RegistrationId { get; init; }
    public required List<RefGameDetailRow> Games { get; init; }
}

public record RefGameDetailRow
{
    public required int Gid { get; init; }
    public required DateTime GameDate { get; init; }
    public required string FieldName { get; init; }
    public required string AgegroupName { get; init; }
    public required string DivName { get; init; }
    public required string T1Name { get; init; }
    public required string T2Name { get; init; }
}

public record RefereeCalendarEventDto
{
    public required int Id { get; init; }
    public required int GameId { get; init; }
    public required string Subject { get; init; }        // "LastName, FirstName - T1 vs T2"
    public required DateTime StartTime { get; init; }
    public required DateTime EndTime { get; init; }      // Inferred or +50min
    public required string Location { get; init; }       // Field name
    public required string Description { get; init; }    // "Agegroup - Division"
    public required Guid? FieldId { get; init; }
    public required string? FieldName { get; init; }
    public required string RefereeId { get; init; }      // UserId
    public required string RefereeFirstName { get; init; }
    public required string RefereeLastName { get; init; }
    public required string? AgegroupName { get; init; }
    public required string? DivName { get; init; }
    public required string? Team1 { get; init; }
    public required string? Team2 { get; init; }
    public required string Color { get; init; }          // Agegroup color, default #1976d2
    public required string RefsWith { get; init; }       // "solo" or comma-separated names
}

// ── Filter/Search DTOs ──

public record RefScheduleFilterOptionsDto
{
    public required List<FilterOption> GameDays { get; init; }
    public required List<FilterOption> GameTimes { get; init; }
    public required List<FilterOption> Agegroups { get; init; }
    public required List<FilterOption> Fields { get; init; }
}

public record FilterOption
{
    public required string Value { get; init; }
    public required string Text { get; init; }
}

public record RefScheduleSearchRequest
{
    public required List<string>? GameDays { get; init; }
    public required List<string>? GameTimes { get; init; }
    public required List<Guid>? AgegroupIds { get; init; }
    public required List<Guid>? FieldIds { get; init; }
}

// ── Command DTOs ──

public record AssignRefsRequest
{
    public required int Gid { get; init; }
    public required List<Guid> RefRegistrationIds { get; init; }
}

public record CopyGameRefsRequest
{
    public required int Gid { get; init; }
    public required bool CopyDown { get; init; }
    public required int NumberTimeslots { get; init; }
    public required int SkipInterval { get; init; }
}

public record ImportRefereeCsvRow
{
    public required string FirstName { get; init; }
    public required string LastName { get; init; }
    public required string Email { get; init; }
    public required string? CellPhone { get; init; }
    public required string? Street { get; init; }
    public required string? City { get; init; }
    public required string? State { get; init; }
    public required string? Zip { get; init; }
    public required string? Dob { get; init; }
    public required string? Gender { get; init; }
    public required string? CertificationNumber { get; init; }
    public required string? CertificationExpiryDate { get; init; }
}

public record ImportRefereesResult
{
    public required int Imported { get; init; }
    public required int Skipped { get; init; }
    public required List<string> Errors { get; init; }
}

// ── Ref Assignor DTOs ──

public record RefAssignorDto
{
    public required Guid RegistrationId { get; init; }
    public required string Username { get; init; }
    public required string FirstName { get; init; }
    public required string LastName { get; init; }
    public required string? Email { get; init; }
    public required string? Cellphone { get; init; }
    public required bool IsActive { get; init; }
}

public record UpsertRefAssignorRequest
{
    public required string Username { get; init; }
    public required string FirstName { get; init; }
    public required string LastName { get; init; }
    public required string? Email { get; init; }
    public required string? Cellphone { get; init; }
}
```

### 1B. Repository Interface (`TSIC.Contracts/Repositories/IRefAssignmentRepository.cs`)

```csharp
public interface IRefAssignmentRepository
{
    // ── Referees ──
    Task<List<RefereeSummaryDto>> GetRefereesForJobAsync(Guid jobId, CancellationToken ct = default);

    // ── Assignments ──
    Task<List<GameRefAssignmentDto>> GetAllAssignmentsForJobAsync(Guid jobId, CancellationToken ct = default);
    Task<List<RefGameAssigments>> GetAssignmentsForGameAsync(int gid, CancellationToken ct = default);
    Task ReplaceAssignmentsForGameAsync(int gid, List<Guid> refRegistrationIds, string auditUserId, CancellationToken ct = default);
    Task DeleteAllAssignmentsForJobAsync(Guid jobId, CancellationToken ct = default);

    // ── Schedule Queries (referee-specific) ──
    Task<RefScheduleFilterOptionsDto> GetRefScheduleFilterOptionsAsync(Guid jobId, CancellationToken ct = default);
    Task<List<RefScheduleGameDto>> SearchScheduleAsync(Guid jobId, RefScheduleSearchRequest request, CancellationToken ct = default);
    Task<List<RefGameDetailsDto>> GetGameRefDetailsAsync(int gid, CancellationToken ct = default);

    // ── Calendar ──
    Task<List<RefereeCalendarEventDto>> GetCalendarEventsAsync(Guid jobId, CancellationToken ct = default);

    // ── Copy Logic Support ──
    Task<List<Schedule>> GetGamesOnFieldForDateAsync(Guid fieldId, DateTime gameDate, CancellationToken ct = default);

    // ── Persistence ──
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
```

### 1C. Repository Implementation (`TSIC.Infrastructure/Repositories/RefAssignmentRepository.cs`)

Key implementation notes:
- All reads use `AsNoTracking()`
- Calendar event `EndTime` = next game on same field's start time, or start + 50 minutes
- `RefsWith` = comma-separated names of other refs on same game, or "solo"
- `ReplaceAssignmentsForGameAsync`: delete existing → insert new (atomic)
- Filter options: distinct GameDays, GameTimes, Agegroups, Fields from Schedule WHERE JobId matches
- `SearchScheduleAsync`: returns flat list of games (NOT dynamic ExpandoObject — Angular handles layout)
- Schedule.RefCount updated after each assignment change

### 1D. Service (`TSIC.API/Services/Referees/RefAssignmentService.cs`)

```csharp
public interface IRefAssignmentService
{
    // ── Queries ──
    Task<List<RefereeSummaryDto>> GetRefereesAsync(Guid jobId, CancellationToken ct);
    Task<RefScheduleFilterOptionsDto> GetFilterOptionsAsync(Guid jobId, CancellationToken ct);
    Task<List<RefScheduleGameDto>> SearchScheduleAsync(Guid jobId, RefScheduleSearchRequest request, CancellationToken ct);
    Task<List<RefGameDetailsDto>> GetGameRefDetailsAsync(int gid, CancellationToken ct);
    Task<List<RefereeCalendarEventDto>> GetCalendarEventsAsync(Guid jobId, CancellationToken ct);
    Task<List<GameRefAssignmentDto>> GetAllAssignmentsAsync(Guid jobId, CancellationToken ct);

    // ── Commands ──
    Task AssignRefsToGameAsync(Guid jobId, AssignRefsRequest request, string auditUserId, CancellationToken ct);
    Task<List<int>> CopyGameRefsAsync(Guid jobId, CopyGameRefsRequest request, string auditUserId, CancellationToken ct);
    Task<ImportRefereesResult> ImportRefereesAsync(Guid jobId, Stream csvStream, string auditUserId, CancellationToken ct);
    Task<List<RefereeSummaryDto>> SeedTestRefereesAsync(Guid jobId, int count, string auditUserId, CancellationToken ct);
    Task DeleteAllRefAssignmentsAsync(Guid jobId, string auditUserId, CancellationToken ct);

    // ── Ref Assignors ──
    Task<List<RefAssignorDto>> GetRefAssignorsAsync(Guid jobId, CancellationToken ct);
    Task<RefAssignorDto> UpsertRefAssignorAsync(Guid jobId, UpsertRefAssignorRequest request, CancellationToken ct);
    Task ToggleRefAssignorActiveAsync(Guid registrationId, CancellationToken ct);
}
```

**Business rules in service layer** (NOT repository):
- **CopyGameRefs**: Find all games on same field/date, apply skip interval, call ReplaceAssignments for each target game. Direction: CopyDown sorts ascending by GDate, CopyUp sorts descending.
- **ImportReferees**: Parse CSV, create AspNetUser via UserManager, create Registration with RoleId = Referee. Skip duplicates (match by email). Return import summary.
- **SeedTestReferees**: Create N users with username pattern `TestRef-{N}`, password = username. Create registrations with Referee role.
- **DeleteAll**: Delete all RefGameAssignments for job, then delete all Registrations with Referee role for job. Dangerous — require confirmation on frontend.

### 1E. Controller (`TSIC.API/Controllers/RefereeAssignmentController.cs`)

```csharp
[ApiController]
[Route("api/referee-assignment")]
[Authorize(Policy = "RefAdmin")]
public class RefereeAssignmentController : ControllerBase
{
    // GET  api/referee-assignment/referees
    // GET  api/referee-assignment/filter-options
    // POST api/referee-assignment/search
    // GET  api/referee-assignment/assignments
    // POST api/referee-assignment/assign
    // POST api/referee-assignment/copy
    // GET  api/referee-assignment/game-details/{gid}
    // GET  api/referee-assignment/calendar-events
    // POST api/referee-assignment/import        (IFormFile)
    // POST api/referee-assignment/seed-test     (body: { count })
    // DELETE api/referee-assignment/purge       (dangerous)
    // GET  api/referee-assignment/assignors
    // POST api/referee-assignment/assignors
    // PUT  api/referee-assignment/assignors/{registrationId}/toggle-active
}
```

**Authorization**: `[Authorize(Policy = "RefAdmin")]` on the controller. The `RefAdmin` policy allows both the Director and RefAssignor roles. If the policy doesn't exist yet, add it in `Program.cs`.

**jobId resolution**: Use `await User.GetJobIdFromRegistrationAsync(_jobLookupService)` — NEVER `User.FindFirst("jobId")`.

### 1F. DI Registration (`Program.cs`)

```csharp
builder.Services.AddScoped<IRefAssignmentRepository, RefAssignmentRepository>();
builder.Services.AddScoped<IRefAssignmentService, RefAssignmentService>();
```

---

## Phase 2 — Frontend: Angular Service + Models

### 2A. Regenerate API Models

```powershell
.\scripts\2-Regenerate-API-Models.ps1
```

This auto-generates all DTOs from Swagger. NEVER create local TypeScript duplicates.

### 2B. Frontend Service (`referee-assignment.service.ts`)

```typescript
@Injectable({ providedIn: 'root' })
export class RefereeAssignmentService {
    private readonly http = inject(HttpClient);
    private readonly apiUrl = `${environment.apiUrl}/referee-assignment`;

    getReferees():                Observable<RefereeSummaryDto[]>
    getFilterOptions():           Observable<RefScheduleFilterOptionsDto>
    searchSchedule(req):          Observable<RefScheduleGameDto[]>
    getAllAssignments():           Observable<GameRefAssignmentDto[]>
    assignRefs(req):              Observable<void>
    copyGameRefs(req):            Observable<number[]>     // affected GIDs
    getGameDetails(gid):          Observable<RefGameDetailsDto[]>
    getCalendarEvents():          Observable<RefereeCalendarEventDto[]>
    importReferees(file):         Observable<ImportRefereesResult>
    seedTestReferees(count):      Observable<RefereeSummaryDto[]>
    purgeAll():                   Observable<void>
    getAssignors():               Observable<RefAssignorDto[]>
    upsertAssignor(req):          Observable<RefAssignorDto>
    toggleAssignorActive(regId):  Observable<void>
}
```

---

## Phase 3 — Frontend: Referee Assignment Page

### Route: `/:jobPath/scheduling/referee-assignment`

**File location**: `src/app/views/admin/scheduling/referee-assignment/`

### 3A. Component Structure

```
referee-assignment/
├── referee-assignment.component.ts       ← Shell with tab navigation
├── referee-assignment.component.html
├── referee-assignment.component.scss
├── assignment-grid/
│   ├── assignment-grid.component.ts      ← Main assignment matrix
│   ├── assignment-grid.component.html
│   └── assignment-grid.component.scss
├── game-card/
│   ├── game-card.component.ts            ← Individual game card in matrix
│   ├── game-card.component.html
│   └── game-card.component.scss
├── ref-info-modal/
│   ├── ref-info-modal.component.ts       ← Game ref details modal
│   ├── ref-info-modal.component.html
│   └── ref-info-modal.component.scss
├── copy-refs-modal/
│   ├── copy-refs-modal.component.ts      ← Copy refs up/down modal
│   ├── copy-refs-modal.component.html
│   └── copy-refs-modal.component.scss
├── import-refs-modal/
│   ├── import-refs-modal.component.ts    ← CSV upload modal
│   ├── import-refs-modal.component.html
│   └── import-refs-modal.component.scss
└── seed-refs-modal/
    ├── seed-refs-modal.component.ts      ← Create test refs modal
    ├── seed-refs-modal.component.html
    └── seed-refs-modal.component.scss
```

### 3B. Shell Component (`referee-assignment.component.ts`)

**Tabs:**
1. **Assign Referees** (default) — the main assignment grid
2. **Ref Calendar** — agenda/calendar view of all assignments
3. **Manage Assignors** — CRUD for ref assignor role accounts

**Signals:**
```typescript
activeTab = signal<'assign' | 'calendar' | 'assignors'>('assign');
referees = signal<RefereeSummaryDto[]>([]);
assignments = signal<GameRefAssignmentDto[]>([]);
filterOptions = signal<RefScheduleFilterOptionsDto | null>(null);
isLoading = signal(false);
```

### 3C. Assignment Grid Component

This is the core UI — a venue × timeslot matrix with game cards.

**Layout:**
```
┌────────────────────────────────────────────────────────────────────┐
│  [Filter Bar]                                                      │
│  GameDays ▼  |  Times ▼  |  Agegroups ▼  |  Fields ▼  | 🔍  ↺   │
├────────────────────────────────────────────────────────────────────┤
│  Toolbar: [Import CSV] [Seed Test Refs] [Delete All ⚠️]           │
├──────────┬──────────────┬──────────────┬──────────────┬────────────┤
│ Time     │ Field A      │ Field B      │ Field C      │ Field D   │
├──────────┼──────────────┼──────────────┼──────────────┼────────────┤
│ 8:00 AM  │ ┌──────────┐ │ ┌──────────┐ │              │            │
│          │ │ U14 Boys │ │ │ U12 Girls│ │              │            │
│          │ │ GID: 142 │ │ │ GID: 143 │ │              │            │
│          │ │ ↓ ↑ ℹ    │ │ │ ↓ ↑ ℹ    │ │              │            │
│          │ │ Eagles   │ │ │ Hawks    │ │              │            │
│          │ │   vs     │ │ │   vs     │ │              │            │
│          │ │ Falcons  │ │ │ Sharks   │ │              │            │
│          │ │──────────│ │ │──────────│ │              │            │
│          │ │ [Ref ▼]  │ │ │ [Ref ▼]  │ │              │            │
│          │ └──────────┘ │ └──────────┘ │              │            │
├──────────┼──────────────┼──────────────┼──────────────┼────────────┤
│ 9:00 AM  │ ...          │ ...          │ ...          │ ...        │
└──────────┴──────────────┴──────────────┴──────────────┴────────────┘
```

**Key behaviors:**
- Filter bar uses Syncfusion MultiSelect (checkbox mode) for all 4 filters
- Search button fetches filtered games from backend
- Games returned as flat list; frontend groups by (GameDate, Field) into matrix
- Each game card has a multi-select dropdown for assigning referees
- Changing ref selection immediately POSTs to backend (auto-save, no Save button)
- Copy icons (↓↑) open the copy modal
- Info icon (ℹ) opens the ref details modal

**Game Card Visual Design:**
- Header bar: agegroup color background, agegroup/division label left, GID right
- Body: team matchup (T1 vs T2), game type badge if not regular season
- Footer: multi-select referee dropdown with checkboxes
- Action icons: copy-down arrow, copy-up arrow, info circle
- Unassigned games: subtle dashed border or muted background
- Assigned games: solid border, ref count badge

### 3D. Game Card Component

Standalone, OnPush. Inputs: game data, referee list. Outputs: assignment change, copy request, info request.

**Agegroup color**: Applied as accent stripe on left or top border. From `AgegroupColor` field.

**Multi-select dropdown**: Syncfusion MultiSelect with checkbox mode. Pre-populated with currently assigned refs. On change → emit assignment event → parent POSTs.

### 3E. Modals

All modals use `TsicDialogComponent` (native `<dialog>`, focus trap, ESC close).

**Copy Refs Modal:**
- Direction label: "Copy referees DOWN to later games" / "UP to earlier games"
- Number of timeslots input (1-10)
- Skip interval input (0-4) with explanation: "Skip every N timeslots"
- Preview: "Will assign refs to N games on [FieldName]"
- Confirm / Cancel buttons

**Ref Info Modal:**
- Shows all ref assignments for a game in a compact grid
- Columns: Ref Name, Game Day, Game Count (how many games that day)
- Useful for checking ref workload before assigning

**Import Refs Modal:**
- File dropzone (CSV/TXT)
- Expected format display with column headers
- Upload progress indicator
- Result summary: X imported, Y skipped, Z errors
- Error details expandable

**Seed Test Refs Modal:**
- Number input (1-20)
- Warning: "Creates test referee accounts for development"
- Generates with pattern: `TestRef-001`, `TestRef-002`, etc.

**Delete All Confirmation:**
- Danger-styled modal (red border, warning icon)
- Text: "This will permanently delete ALL referee assignments AND all referee registrations for this event."
- Requires typing "DELETE" to enable confirm button
- Two-step: checkbox "I understand this cannot be undone" + confirm button

---

## Phase 4 — Frontend: Referee Calendar Page

### Route: `/:jobPath/scheduling/referee-calendar`

**File location**: `src/app/views/admin/scheduling/referee-calendar/`

### 4A. Component Structure

```
referee-calendar/
├── referee-calendar.component.ts
├── referee-calendar.component.html
├── referee-calendar.component.scss
└── calendar-event-card/
    ├── calendar-event-card.component.ts
    ├── calendar-event-card.component.html
    └── calendar-event-card.component.scss
```

### 4B. UI Layout

**Two-tab view:**

**Tab 1: Agenda View**
```
┌──────────────────────────────────────────────────────────────┐
│  Referee Calendar — Agenda View                              │
│  [Referee ▼ All]        [Export CSV]  [Print/PDF]            │
├──────────────────────────────────────────────────────────────┤
│  Saturday, March 14                                          │
│  ┌────────────────────────────────────────────────────────┐  │
│  │ 8:00 AM — 8:50 AM  |  Field A  |  U14 Boys - Pool A   │  │
│  │ Smith, John — Eagles vs Falcons                        │  │
│  │ Working with: Jones, Davis                             │  │
│  └────────────────────────────────────────────────────────┘  │
│  ┌────────────────────────────────────────────────────────┐  │
│  │ 8:00 AM — 8:50 AM  |  Field B  |  U12 Girls - Pool B  │  │
│  │ Jones, Mike — Hawks vs Sharks                          │  │
│  │ Solo Assignment ⚠️                                      │  │
│  └────────────────────────────────────────────────────────┘  │
│  ...                                                         │
├──────────────────────────────────────────────────────────────┤
│  Sunday, March 15                                            │
│  ...                                                         │
└──────────────────────────────────────────────────────────────┘
```

- No Syncfusion Schedule component needed — build as custom agenda list (more control, better theming)
- Referee filter dropdown at top — filters events client-side
- Each event card shows: time range, field, agegroup/division, ref name, teams, co-refs
- Solo assignments highlighted with warning indicator
- Agegroup color as left accent stripe

**Tab 2: Master Schedule Grid**
```
┌────────┬────────────┬────────────┬────────────┐
│ Time   │ Field A    │ Field B    │ Field C    │
├────────┼────────────┼────────────┼────────────┤
│ 8:00   │ Eagles vs  │ Hawks vs   │            │
│        │ Falcons    │ Sharks     │            │
│        │ [Smith]    │ [Jones]    │            │
│        │ [Davis]    │            │            │
├────────┼────────────┼────────────┼────────────┤
│ 9:00   │ ...        │ ...        │ ...        │
└────────┴────────────┴────────────┴────────────┘
```

- Venues as columns, times as rows
- Each cell: team matchup + ref names in accent-colored badges
- Export to Excel button (generate CSV client-side)

### 4C. Export Features

**CSV Export:**
- Columns: Referee Last, Referee First, Date/Time, Field, Agegroup, Division, Team 1, Team 2, Refs With
- Sorted by: LastName → FirstName → DateTime
- Client-side generation using `Blob` + download link

**Print/PDF:**
- Generate printable HTML table in new window
- Clean typography, no interactive elements
- Group by referee for per-ref printout option
- `window.print()` for native PDF generation

---

## Phase 5 — Frontend: Ref Assignor Management

### Route: `/:jobPath/configure/ref-assignors`

**File location**: `src/app/views/admin/configure/ref-assignors/`

Simple CRUD page — follows the same pattern as other admin configuration pages.

### 5A. UI Layout

```
┌──────────────────────────────────────────────────────────────┐
│  Manage Ref Assignors                [+ Add Assignor]        │
├──────────────────────────────────────────────────────────────┤
│  ┌────────────────────────────────────────────────────────┐  │
│  │ Active │ Username │ Last Name │ First Name │ Actions   │  │
│  ├────────┼──────────┼───────────┼────────────┼───────────┤  │
│  │  ✓     │ jsmith   │ Smith     │ John       │ [Edit]    │  │
│  │  ✗     │ mjones   │ Jones     │ Mike       │ [Edit]    │  │
│  └────────────────────────────────────────────────────────┘  │
└──────────────────────────────────────────────────────────────┘
```

- Syncfusion DataGrid with inline edit support OR card list with edit modal
- Add button opens modal form: username, first, last, email, cellphone
- Edit toggles active status + updates contact info
- Cannot delete SuperUser assignors (prevent in UI)

---

## Phase 6 — Routing & Navigation

### 6A. Route Registration (`app.routes.ts`)

```typescript
{
    path: 'scheduling/referee-assignment',
    canActivate: [authGuard],
    data: { requirePhase2: true },
    loadComponent: () => import('./views/admin/scheduling/referee-assignment/referee-assignment.component')
        .then(m => m.RefereeAssignmentComponent)
},
{
    path: 'scheduling/referee-calendar',
    canActivate: [authGuard],
    data: { requirePhase2: true },
    loadComponent: () => import('./views/admin/scheduling/referee-calendar/referee-calendar.component')
        .then(m => m.RefereeCalendarComponent)
},
{
    path: 'configure/ref-assignors',
    canActivate: [authGuard],
    data: { requireAdmin: true },
    loadComponent: () => import('./views/admin/configure/ref-assignors/ref-assignors.component')
        .then(m => m.RefAssignorsComponent)
},
```

### 6B. Nav Entries (SQL seed)

Add to Director nav under "Scheduling" group:
```sql
-- Assign Referees (under Scheduling group)
INSERT INTO [nav].[NavItem] (NavId, ParentNavItemId, Text, IconName, RouterLink, SortOrder, Active, Modified)
VALUES (@schedulingNavId, @schedulingParentId, N'Assign Referees', N'person-badge', N'scheduling/referee-assignment', 11, 1, GETUTCDATE());

-- Referee Calendar (under Scheduling group)
INSERT INTO [nav].[NavItem] (NavId, ParentNavItemId, Text, IconName, RouterLink, SortOrder, Active, Modified)
VALUES (@schedulingNavId, @schedulingParentId, N'Referee Calendar', N'calendar-event', N'scheduling/referee-calendar', 12, 1, GETUTCDATE());

-- Manage Ref Assignors (under Configure group)
INSERT INTO [nav].[NavItem] (NavId, ParentNavItemId, Text, IconName, RouterLink, SortOrder, Active, Modified)
VALUES (@configureNavId, @configureParentId, N'Ref Assignors', N'people', N'configure/ref-assignors', 4, 1, GETUTCDATE());
```

---

## Phase 7 — Authorization Policy

### 7A. Add `RefAdmin` Policy (`Program.cs`)

If not already present:
```csharp
options.AddPolicy("RefAdmin", policy =>
    policy.RequireAssertion(context =>
        context.User.IsInRole(RoleConstants.DirectorName) ||
        context.User.IsInRole(RoleConstants.RefAssignorName) ||
        context.User.IsInRole(RoleConstants.SuperUserName)));
```

This allows Directors, Ref Assignors, and SuperUsers to manage referee assignments.

---

## UI Polish Checklist

### Design System Compliance
- [ ] All colors via CSS variables — NO hex codes
- [ ] 8px spacing grid (`--space-*` tokens)
- [ ] All shadows via `--shadow-*` tokens
- [ ] Border radius via `--radius-*` tokens
- [ ] Test all 8 palettes via Brand Preview
- [ ] WCAG AA contrast (4.5:1 minimum)
- [ ] `prefers-reduced-motion` respected

### Game Card Polish
- [ ] Agegroup color as accent stripe (left border or top bar)
- [ ] Smooth hover transitions (`cubic-bezier(0.4, 0, 0.2, 1)`)
- [ ] Unassigned state: dashed border, muted background
- [ ] Assigned state: solid border, ref count badge
- [ ] Multi-select dropdown themed to match design system
- [ ] Action icons with tooltip on hover

### Responsive Design
- [ ] Filter bar wraps on mobile (stacked layout)
- [ ] Game matrix scrolls horizontally on mobile
- [ ] Sticky time column on scroll
- [ ] Touch-friendly card interactions (44px minimum targets)
- [ ] Calendar cards full-width on mobile

### Accessibility
- [ ] `aria-label` on all icon buttons (copy, info)
- [ ] Focus-visible styles on cards and buttons
- [ ] Screen reader support for ref assignments
- [ ] Keyboard navigation through game matrix
- [ ] Live region announcements for assignment changes

---

## Execution Order

| Step | Phase | Description | Depends On |
|------|-------|-------------|------------|
| 1 | 1A | Create DTOs | — |
| 2 | 1B | Create repository interface | 1 |
| 3 | 1C | Implement repository | 2 |
| 4 | 1D | Create service interface + impl | 3 |
| 5 | 1E | Create controller | 4 |
| 6 | 1F | Register in DI | 5 |
| 7 | 7A | Add RefAdmin auth policy | — |
| 8 | — | `dotnet build` — verify 0 errors | 6, 7 |
| 9 | 2A | Regenerate API models | 8 |
| 10 | 2B | Create Angular service | 9 |
| 11 | 3 | Build referee assignment page | 10 |
| 12 | 4 | Build referee calendar page | 10 |
| 13 | 5 | Build ref assignor management | 10 |
| 14 | 6 | Add routes + nav entries | 11-13 |
| 15 | — | Full integration test | 14 |

---

## Risk Mitigation

| Risk | Mitigation |
|------|------------|
| Dynamic venue columns (legacy ExpandoObject) | Return flat game list; Angular groups by field client-side |
| Large schedule datasets (1000+ games) | Paginate or virtual scroll; filter options reduce dataset |
| Copy refs edge cases | Validate: same field, same date, respect skip interval |
| CSV import validation | Server-side validation with detailed error reporting |
| Concurrent ref assignment | Last-write-wins (replace all assignments per game) |
| RefAdmin policy missing | Create policy in Program.cs before controller registration |

---

## Legacy Feature Parity Checklist

- [ ] Multi-filter schedule search (days, times, agegroups, fields)
- [ ] Assign multiple refs per game via multi-select
- [ ] Copy refs down/up to adjacent timeslots with skip interval
- [ ] View game ref details (workload per day per ref)
- [ ] Import referees from CSV (12 fields)
- [ ] Seed test referees for development
- [ ] Delete all assignments + registrations (with confirmation)
- [ ] Calendar agenda view with referee filter
- [ ] Master schedule grid (venues × times)
- [ ] CSV export of calendar data
- [ ] Print/PDF export
- [ ] Ref assignor CRUD (add, edit, toggle active)
- [ ] Agegroup color coding on game cards
- [ ] Solo assignment warning indicators

---

**Document Version**: 1.0
**Author**: Claude Code
**Last Updated**: 2026-02-25
